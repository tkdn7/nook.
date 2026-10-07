using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Runtime.InteropServices;
using Nook.App.Services;
using Nook.App.ViewModels;
using System.ComponentModel;
using System.Windows.Media.Animation;

namespace Nook.App.Views;

public partial class WidgetWindow : Window
{
    private readonly AppViewModel _app;
    private readonly WidgetViewModel _vm;
    private HwndSource? _source;
    private bool? _paletteDark;
    private int _motionRevision;
    private bool _appearancePending;
    private LayoutMode _lastLayout;
    private bool _gripActive;
    private int _sizeRevision;
    private bool _circleState;
    private readonly MagneticDragSession _drag = new();
    internal Func<(double X, double Y)?> GetPointerPosition { get; set; } = NativeWindows.PointerPosition;
    public Func<IReadOnlyList<ScreenRect>>? GetSnapPeers { get; set; }
    public WidgetWindow(AppViewModel app, WidgetViewModel vm)
    {
        _app = app; _vm = vm; WidgetShapes.Normalize(vm.Model); _circleState = vm.IsCircular;
        InitializeComponent(); DataContext = vm;
        MinHeight = _circleState ? 180 : 140; MaxWidth = _circleState ? 480 : 640;
        ApplyPalette(); vm.PropertyChanged += OnWidgetChanged;
        Title = "Nook · " + vm.Title;
        Width = vm.Model.Width; Height = vm.Model.Height;
        if (App.InspectWidgets) ShowInTaskbar = true;
        SourceInitialized += (_, _) =>
        {
            if (!App.InspectWidgets) NativeWindows.MakeToolWindow(this);
            NativeWindows.Restore(this, vm.Model);
            _source = HwndSource.FromHwnd(NativeWindows.Handle(this));
            _source?.AddHook(OnWindowMessage);
        };
        SizeChanged += (_, _) => UpdateSize();
        MouseEnter += (_, _) => FadeMenus(1, 130);
        MouseLeave += (_, _) => { if (!IsKeyboardFocusWithin) FadeMenus(0, 160); };
        IsKeyboardFocusWithinChanged += (_, _) => FadeMenus(IsKeyboardFocusWithin || IsMouseOver ? 1 : 0, 160);
        app.Metrics.Updated += OnMetrics;
        Closing += (_, _) => { NativeWindows.Capture(this, vm.Model); app.Save(); };
        Closed += (_, _) => { vm.PropertyChanged -= OnWidgetChanged; app.Metrics.Updated -= OnMetrics; _source?.RemoveHook(OnWindowMessage); };
        UpdateSize();
    }
    private void FadeMenus(double opacity, double duration)
    { Motion.Fade(MenuButton, opacity, duration); Motion.Fade(CircleMenuButton, opacity, duration); }
    private void ApplyPalette()
    {
        if (_paletteDark == _vm.IsDark) return;
        _paletteDark = _vm.IsDark;
        foreach (var (key, color) in ThemeService.Palette(_vm.IsDark)) Resources[key] = new SolidColorBrush(color);
    }
    private void OnWidgetChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(WidgetViewModel.IsDark)) ApplyPalette();
        if (e.PropertyName == nameof(WidgetViewModel.IsCircular) && _circleState != _vm.IsCircular)
        {
            _circleState = _vm.IsCircular; MinHeight = _circleState ? 180 : 140; MaxWidth = _circleState ? 480 : 640;
            if (_circleState) { double side = Math.Clamp(Width, 180, 480); ResizeAnimated(side, side, force: true); }
            Motion.Reveal(_circleState ? CircleContent : CardContent, 4);
        }
        if (e.PropertyName is nameof(WidgetViewModel.Radius) or nameof(WidgetViewModel.Background) && IsLoaded && !_appearancePending)
        {
            _appearancePending = true;
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Render, () =>
            { _appearancePending = false; if (Motion.Enabled && IsLoaded) Surface.BeginAnimation(OpacityProperty, new DoubleAnimation(.86, 1, Motion.Duration(160))); });
        }
    }
    public void ShowAnimated()
    {
        _motionRevision++; IsHitTestVisible = true;
        BeginAnimation(OpacityProperty, null); Opacity = 1; Show();
        if (Motion.Enabled) { BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, Motion.Duration(180))); Motion.Reveal(Surface, 5); }
    }
    public void HideAnimated(bool close = false)
    {
        int revision = ++_motionRevision; IsHitTestVisible = false;
        if (!Motion.Enabled || !IsVisible) { Finish(); return; }
        var fade = Motion.To(0, 140); fade.Completed += (_, _) => { if (_motionRevision == revision) Finish(); };
        BeginAnimation(OpacityProperty, fade);
        void Finish() { BeginAnimation(OpacityProperty, null); Opacity = 1; if (close) Close(); else Hide(); }
    }
    private IntPtr OnWindowMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == 0x0232) { _drag.End(); return IntPtr.Zero; } // WM_EXITSIZEMOVE
        if (message != 0x0216 || lParam == IntPtr.Zero || _vm.Model.Locked) return IntPtr.Zero;
        var rect = Marshal.PtrToStructure<NativeWindows.Rect>(lParam);
        if (!_drag.IsActive && !_app.MagneticMode) return IntPtr.Zero;
        var proposed = new ScreenRect(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
        if (_drag.IsActive && GetPointerPosition() is { } pointer)
            proposed = _drag.Proposed(pointer.X, pointer.Y) with { Width = proposed.Width, Height = proposed.Height };
        rect.Left = (int)Math.Round(proposed.Left); rect.Top = (int)Math.Round(proposed.Top);
        rect.Right = rect.Left + (int)proposed.Width; rect.Bottom = rect.Top + (int)proposed.Height;
        var (work, scale) = NativeWindows.WorkArea(rect);
        var snapped = MagneticPlacement.Snap(proposed, work, GetSnapPeers?.Invoke() ?? [], scale,
            _app.MagneticMode && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
        int width = rect.Right - rect.Left, height = rect.Bottom - rect.Top;
        rect.Left = (int)Math.Round(snapped.Left); rect.Top = (int)Math.Round(snapped.Top);
        rect.Right = rect.Left + width; rect.Bottom = rect.Top + height;
        Marshal.StructureToPtr(rect, lParam, false);
        handled = true; return new IntPtr(1);
    }
    private void OnMetrics() => Chart.InvalidateVisual();
    private void UpdateSize()
    {
        _vm.SetSize(ActualWidth > 0 ? ActualWidth : Width, ActualHeight > 0 ? ActualHeight : Height);
        if (_lastLayout != _vm.Layout && IsLoaded) { Motion.Reveal(ValueArea, 4); Motion.Reveal(SupplementArea, 5); }
        _lastLayout = _vm.Layout;
        ChartArea.Height = Math.Max(48, Math.Min(220, (ActualHeight > 0 ? ActualHeight : Height) - 190));
    }
    private void Title_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_vm.Model.Locked || MenuButton.IsMouseOver || CircleMenuButton.IsMouseOver) return;
        if (e.ChangedButton != MouseButton.Left) return;
        e.Handled = true;
        if (NativeWindows.Bounds(this) is { } bounds && GetPointerPosition() is { } pointer)
            _drag.Begin(bounds, pointer.X, pointer.Y);
        try { DragMove(); } catch (InvalidOperationException) { }
        finally { _drag.End(); }
        NativeWindows.Capture(this, _vm.Model); _app.Save();
    }
    private void Surface_MouseDown(object sender, MouseButtonEventArgs e)
    { if (_vm.IsCircular) Title_MouseDown(sender, e); }
    private void Grip_DragDelta(object sender, DragDeltaEventArgs e)
    {
        if (_vm.Model.Locked) return;
        if (!_gripActive) { StopSizingMotion(); _gripActive = true; }
        NativeWindows.Resize(this, (string)((Thumb)sender).Tag, e.HorizontalChange, e.VerticalChange, _vm.IsCircular);
    }
    private void Grip_DragCompleted(object sender, DragCompletedEventArgs e)
    { _gripActive = false; NativeWindows.Capture(this, _vm.Model); _app.Save(); }
    private void Menu_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        var menuButton = (Button)sender;
        var menu = new ContextMenu { PlacementTarget = menuButton, Placement = PlacementMode.Bottom, Resources = Resources };
        AddItem(menu, "외형 설정", () => _app.OpenWidgetEditor(_vm));
        AddItem(menu, "그룹 지정", () => _app.OpenWidgetEditor(_vm));
        menu.Items.Add(new Separator());
        var sizes = _vm.IsCircular ? new[] { ("Small", 220.0, 220.0, LayoutMode.Small), ("Mid", 300.0, 300.0, LayoutMode.Mid), ("Big", 420.0, 420.0, LayoutMode.Big) }
            : new[] { ("Small", 220.0, 160.0, LayoutMode.Small), ("Mid", 300.0, 200.0, LayoutMode.Mid), ("Big", 420.0, 310.0, LayoutMode.Big) };
        foreach (var (name, width, height, mode) in sizes)
        {
            var preset = new MenuItem { Header = $"{name} · {width:F0} × {height:F0}", IsCheckable = true, IsChecked = _vm.Layout == mode, IsEnabled = !_vm.Model.Locked };
            preset.Click += (_, _) => ResizeAnimated(width, height);
            menu.Items.Add(preset);
        }
        menu.Items.Add(new Separator());
        var locked = AddItem(menu, "위치·크기 잠금", () => { _vm.Model.Locked = !_vm.Model.Locked; _app.Changed(); });
        locked.IsCheckable = true; locked.IsChecked = _vm.Model.Locked;
        menu.Items.Add(new Separator());
        AddItem(menu, "숨기기", () => _app.Toggle(_vm));
        AddItem(menu, "삭제", () => _app.Delete(_vm));
        menuButton.ContextMenu = menu; menu.IsOpen = true;
    }
    public void ResizeAnimated(double width, double height, bool force = false)
    {
        if (_vm.Model.Locked && !force) return;
        if (_vm.IsCircular) width = height = Math.Clamp(width, 180, 480);
        int revision = ++_sizeRevision;
        if (!Motion.Enabled) { Width = width; Height = height; NativeWindows.Capture(this, _vm.Model); _app.Save(); return; }
        var horizontal = Motion.To(width, 260); var vertical = Motion.To(height, 260);
        horizontal.From = ActualWidth; vertical.From = ActualHeight;
        vertical.Completed += (_, _) =>
        {
            if (revision != _sizeRevision) return;
            Width = width; Height = height; BeginAnimation(WidthProperty, null); BeginAnimation(HeightProperty, null);
            Dispatcher.BeginInvoke(() => { NativeWindows.Capture(this, _vm.Model); _app.Save(); });
        };
        BeginAnimation(WidthProperty, horizontal); BeginAnimation(HeightProperty, vertical);
    }
    private void StopSizingMotion()
    {
        _sizeRevision++; double width = ActualWidth, height = ActualHeight;
        BeginAnimation(WidthProperty, null); BeginAnimation(HeightProperty, null);
        if (width > 0 && height > 0) { Width = width; Height = height; }
    }
    private static MenuItem AddItem(ContextMenu menu, string text, Action action)
    {
        string icon = text switch { "외형 설정" => "settings", "그룹 지정" => "board", "위치·크기 잠금" => "lock_closed", "숨기기" => "eye", _ => "delete" };
        var item = new MenuItem { Header = text, Icon = new Nook.App.Controls.FluentIcon { Glyph = icon, Width = 16, Height = 16 } };
        item.Click += (_, _) => action(); menu.Items.Add(item); return item;
    }
}
