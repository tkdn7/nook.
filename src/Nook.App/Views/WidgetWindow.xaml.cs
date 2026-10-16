using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Runtime.InteropServices;
using Nook.App.Services;
using Nook.App.ViewModels;
using System.ComponentModel;
using System.Windows.Media.Animation;
using Nook.App.Controls;

namespace Nook.App.Views;

public partial class WidgetWindow : Window
{
    public static readonly DependencyProperty AnimatedWidthProperty = DependencyProperty.Register("AnimatedWidth", typeof(double), typeof(WidgetWindow), new PropertyMetadata(300.0, (o, e) => ((WidgetWindow)o).Width = (double)e.NewValue));
    public static readonly DependencyProperty AnimatedHeightProperty = DependencyProperty.Register("AnimatedHeight", typeof(double), typeof(WidgetWindow), new PropertyMetadata(200.0, (o, e) => ((WidgetWindow)o).Height = (double)e.NewValue));
    private readonly AppViewModel _app;
    private readonly WidgetViewModel _vm;
    private HwndSource? _source;
    private bool? _paletteDark;
    private int _motionRevision;
    private bool _appearancePending;
    private LayoutMode _lastLayout;
    private bool _gripActive;
    private ScreenRect _resizeOrigin;
    private (double X, double Y)? _resizePointer;
    private double _resizeDx, _resizeDy;
    private int _sizeRevision;
    private bool _circleState;
    private AirQualityGlow? _airGlow;
    private readonly MagneticDragSession _drag = new();
    internal Func<(double X, double Y)?> GetPointerPosition { get; set; } = NativeWindows.PointerPosition;
    public Func<IReadOnlyList<ScreenRect>>? GetSnapPeers { get; set; }
    public event Action? DragStarting;
    public event Action<ScreenRect>? DragUpdated;
    public event Action? DragEnding;
    public void FreezeMoveSize() => StopSizingMotion();
    public WidgetWindow(AppViewModel app, WidgetViewModel vm)
    {
        _app = app; _vm = vm; WidgetShapes.Normalize(vm.Model); _circleState = vm.IsCircular;
        InitializeComponent(); DataContext = vm;
        PreviewMouseLeftButtonDown += (_, e) => { if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) { app.SelectWidget(vm, true); e.Handled = true; } };
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { app.ClearSelection(); e.Handled = true; } };
        if (vm.IsAir)
        {
            _airGlow = new(vm); GlowHost.Child = _airGlow;
            void GlowBounds() => _airGlow.Margin = vm.IsCircular ? new Thickness(0) : new Thickness(-16, -10, -16, -12);
            GlowBounds(); vm.FeatureUpdated += GlowBounds;
            Closed += (_, _) => { vm.FeatureUpdated -= GlowBounds; _airGlow.Dispose(); };
        }
        if (vm.Model.Kind == WidgetKind.Breathe)
        {
            var breathe = new BreatheVisual(vm); GlowHost.Child = breathe;
            void BreatheBounds() => breathe.Margin = vm.IsCircular ? new Thickness(0) : new Thickness(-16, -10, -16, -12);
            BreatheBounds(); vm.FeatureUpdated += BreatheBounds;
            Closed += (_, _) => { vm.FeatureUpdated -= BreatheBounds; breathe.Dispose(); };
        }
        if (vm.HasFeature)
        {
            FeatureHost.Child = new WidgetFeaturesView(app, vm);
            if (WidgetShapes.SupportsCircle(vm.Model.Kind)) CircleFeatureHost.Child = new WidgetFeaturesView(app, vm);
        }
        MinWidth = WidgetLimits.MinWidth(vm.Model.Kind);
        MinHeight = _circleState ? 180 : WidgetLimits.MinHeight(vm.Model.Kind); MaxWidth = _circleState ? 480 : WidgetLimits.MaxWidth(vm.Model.Kind); MaxHeight = _circleState ? 480 : WidgetLimits.MaxHeight(vm.Model.Kind);
        ApplyPalette(); vm.PropertyChanged += OnWidgetChanged;
        Loaded += (_, _) => UpdateSurfaceAppearance();
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
        IsVisibleChanged += (_, _) => { if (IsVisible) { vm.RefreshMetrics(); OnMetrics(); } };
        Closing += (_, _) => { if (!app.IsApplyingPreset) { NativeWindows.Capture(this, vm.Model); app.Save(); } };
        Closed += (_, _) => { (FeatureHost.Child as IDisposable)?.Dispose(); (CircleFeatureHost.Child as IDisposable)?.Dispose(); vm.PropertyChanged -= OnWidgetChanged; app.Metrics.Updated -= OnMetrics; _source?.RemoveHook(OnWindowMessage); };
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
        if (e.PropertyName == nameof(WidgetViewModel.Title)) Title = "Nook · " + _vm.Title;
        if (e.PropertyName == nameof(WidgetViewModel.IsDark)) ApplyPalette();
        if (e.PropertyName == nameof(WidgetViewModel.IsCircular) && _circleState != _vm.IsCircular)
        {
            _circleState = _vm.IsCircular; MinHeight = _circleState ? 180 : 140; MaxWidth = _circleState ? 480 : WidgetLimits.MaxWidth(_vm.Model.Kind); MaxHeight = _circleState ? 480 : WidgetLimits.MaxHeight(_vm.Model.Kind);
            if (_circleState) { double side = Math.Clamp(Width, 180, 480); ResizeAnimated(side, side, force: true); }
            CircleContent.RenderTransform = Transform.Identity;
            Motion.Fade(_circleState ? CircleContent : CardContent, 1, 180);
        }
        if (e.PropertyName == nameof(WidgetViewModel.IsOpacityNone))
        {
            UpdateSurfaceAppearance();
        }
        else if (e.PropertyName is nameof(WidgetViewModel.Radius) or nameof(WidgetViewModel.Background) && IsLoaded && !_appearancePending)
        {
            _appearancePending = true;
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Render, () =>
            { _appearancePending = false; if (Motion.Enabled && IsLoaded) Surface.BeginAnimation(OpacityProperty, new DoubleAnimation(.86, 1, Motion.Duration(160))); });
        }
    }
    private void UpdateSurfaceAppearance()
    {
        if (_vm.IsOpacityNone)
        {
            Surface.Effect = null;
        }
        else
        {
            if (Surface.Effect == null) Surface.Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = System.Windows.Media.Colors.Black, Opacity = 0.16, BlurRadius = 8, ShadowDepth = 2 };
        }
    }
    public void ShowAnimated()
    {
        _motionRevision++; IsHitTestVisible = true;
        BeginAnimation(OpacityProperty, null); Opacity = 1; Show();
        Surface.RenderTransform = Transform.Identity;
        if (Motion.Enabled) BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, Motion.Duration(180)));
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
        if (message == 0x0046 && lParam != IntPtr.Zero && _vm.Model.Kind is WidgetKind.Dice or WidgetKind.KeyStats)
        {
            var position = Marshal.PtrToStructure<WindowPosition>(lParam);
            if ((position.Flags & 1) == 0)
            {
                double nativeScale = VisualTreeHelper.GetDpi(this).DpiScaleX;
                var size = _vm.Model.Kind == WidgetKind.Dice ? WidgetLimits.ConstrainDice(new(0, 0, position.Width, position.Height), "RB", nativeScale)
                    : new ScreenRect(0, 0, Math.Clamp(position.Width, MinWidth * nativeScale, MaxWidth * nativeScale), Math.Clamp(position.Height, MinHeight * nativeScale, MaxHeight * nativeScale));
                position.Width = (int)Math.Round(size.Width); position.Height = (int)Math.Round(size.Height);
                Marshal.StructureToPtr(position, lParam, false);
            }
        }
        if (message == 0x0232) { _drag.End(); DragEnding?.Invoke(); return IntPtr.Zero; } // WM_EXITSIZEMOVE
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
        DragUpdated?.Invoke(new(rect.Left, rect.Top, width, height));
        handled = true; return new IntPtr(1);
    }
    private void OnMetrics()
    { if (IsVisible && _vm.Model.Visible && _vm.ShowGraph && !_vm.IsCircular) Chart.InvalidateVisual(); }
    [StructLayout(LayoutKind.Sequential)] private struct WindowPosition { public IntPtr Hwnd, After; public int X, Y, Width, Height; public uint Flags; }
    private void UpdateSize()
    {
        double side = Math.Min(ActualWidth > 0 ? ActualWidth : Width, ActualHeight > 0 ? ActualHeight : Height) - 12;
        CircleTitleLabel.MaxWidth = Math.Max(24, side * .56 - (_vm.IsMetricCircle ? 22 : 0));
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
        StopSizingMotion();
        if (!_vm.Selected) _app.ClearSelection();
        DragStarting?.Invoke();
        if (NativeWindows.Bounds(this) is { } bounds && GetPointerPosition() is { } pointer)
            _drag.Begin(bounds, pointer.X, pointer.Y);
        try { DragMove(); } catch (InvalidOperationException) { }
        finally { _drag.End(); DragEnding?.Invoke(); }
        NativeWindows.Capture(this, _vm.Model); _app.Save();
    }
    private void Surface_MouseDown(object sender, MouseButtonEventArgs e)
    { if (_vm.IsCircular || _vm.Model.Kind == WidgetKind.Breathe) Title_MouseDown(sender, e); }
    private void Grip_DragStarted(object sender, DragStartedEventArgs e)
    { if (_vm.Model.Locked) return; StopSizingMotion(); _gripActive = true; _resizeOrigin = NativeWindows.Bounds(this) ?? new(Left, Top, Width, Height); _resizePointer = GetPointerPosition(); _resizeDx = _resizeDy = 0; }
    private void Grip_DragDelta(object sender, DragDeltaEventArgs e)
    {
        if (_vm.Model.Locked) return;
        if (!_gripActive) { Grip_DragStarted(sender, null!); _resizePointer = null; }
        string edge = (string)((Thumb)sender).Tag; var (work, scale) = NativeWindows.WorkArea(this);
        _resizeDx += e.HorizontalChange * scale; _resizeDy += e.VerticalChange * scale;
        double dx = _resizeDx, dy = _resizeDy; if (_resizePointer is { } origin && GetPointerPosition() is { } point) { dx = point.X - origin.X; dy = point.Y - origin.Y; }
        var raw = _vm.Model.Kind == WidgetKind.Dice ? WidgetLimits.ResizeDice(_resizeOrigin, edge, dx, dy, scale)
            : MagneticResize.Propose(_resizeOrigin, edge, dx, dy, scale, _vm.IsCircular, MaxWidth, MaxHeight);
        var snapped = MagneticResize.Snap(raw, edge, work, GetSnapPeers?.Invoke() ?? [], scale, _vm.IsCircular, _app.MagneticMode && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift), MaxWidth, MaxHeight);
        if (_vm.Model.Kind == WidgetKind.Calculator)
        {
            double width = Math.Max(MinWidth * scale, snapped.Width), height = Math.Max(MinHeight * scale, snapped.Height);
            snapped = new(edge.Contains('L') ? snapped.Right - width : snapped.Left, edge.Contains('T') ? snapped.Bottom - height : snapped.Top, width, height);
        }
        if (_vm.Model.Kind == WidgetKind.Dice) snapped = WidgetLimits.ConstrainDice(snapped, edge, scale);
        NativeWindows.SetBounds(this, snapped);
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
        if (_vm.Model.Kind == WidgetKind.ProgramShelf)
        {
            menu.Items.Add(new Separator());
            var shelf = FeatureHost.Child as WidgetFeaturesView;
            AddItem(menu, "프로그램 / 파일 추가", () => shelf?.AddFiles());
            AddItem(menu, "폴더 추가", () => shelf?.AddFolders());
        }
        menu.Items.Add(new Separator());
        var sizes = _vm.IsCircular ? new[] { ("Small", 220.0, 220.0, LayoutMode.Small), ("Mid", 300.0, 300.0, LayoutMode.Mid), ("Big", 420.0, 420.0, LayoutMode.Big) }
            : new[] { ("Small", 220.0, 160.0, LayoutMode.Small), ("Mid", 300.0, 200.0, LayoutMode.Mid), ("Big", 420.0, 310.0, LayoutMode.Big) };
        if (_vm.Model.Kind == WidgetKind.Dice) sizes = [("가로 · Small", 220, 150, LayoutMode.Small), ("가로 · Mid", 300, 200, LayoutMode.Mid), ("가로 · Big", 420, 280, LayoutMode.Big), ("세로 · Small", 180, 240, LayoutMode.Small), ("세로 · Mid", 200, 300, LayoutMode.Mid), ("세로 · Big", 280, 420, LayoutMode.Big)];
        if (_vm.Model.Kind == WidgetKind.KeyStats) sizes = [("Small", 220, 160, LayoutMode.Small)];
        if (_vm.Model.Kind == WidgetKind.Calculator) sizes = [("Small", 260, 320, LayoutMode.Small), ("Mid", 320, 380, LayoutMode.Mid), ("Big", 480, 460, LayoutMode.Big)];
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
        var sizeModel = new WidgetInstance { Kind = _vm.Model.Kind, Shape = _vm.Model.Shape, Width = width, Height = height };
        WidgetShapes.Normalize(sizeModel); width = sizeModel.Width; height = sizeModel.Height;
        int revision = ++_sizeRevision;
        if (!Motion.Enabled) { Width = width; Height = height; NativeWindows.Capture(this, _vm.Model); _app.Save(); return; }
        var horizontal = Motion.To(width, 260); var vertical = Motion.To(height, 260);
        horizontal.From = ActualWidth; vertical.From = ActualHeight;
        vertical.Completed += (_, _) =>
        {
            if (revision != _sizeRevision) return;
            BeginAnimation(AnimatedWidthProperty, null); BeginAnimation(AnimatedHeightProperty, null); Width = width; Height = height;
            Dispatcher.BeginInvoke(() => { NativeWindows.Capture(this, _vm.Model); _app.Save(); });
        };
        // Native Window size changes can replace Width/Height animation values.
        // Animate independent properties and apply each frame to the native window.
        SetValue(AnimatedWidthProperty, ActualWidth); SetValue(AnimatedHeightProperty, ActualHeight);
        BeginAnimation(AnimatedWidthProperty, horizontal); BeginAnimation(AnimatedHeightProperty, vertical);
    }
    private void StopSizingMotion()
    {
        _sizeRevision++; double width = ActualWidth, height = ActualHeight;
        BeginAnimation(AnimatedWidthProperty, null); BeginAnimation(AnimatedHeightProperty, null);
        if (_vm.IsCircular) width = height = Math.Min(width, height);
        if (width > 0 && height > 0) { Width = width; Height = height; }
    }
    private static MenuItem AddItem(ContextMenu menu, string text, Action action)
    {
        string icon = text switch { "외형 설정" => "settings", "그룹 지정" => "board", "위치·크기 잠금" => "lock_closed", "숨기기" => "eye", "프로그램 / 파일 추가" => "add", "폴더 추가" => "folder", _ => "delete" };
        var item = new MenuItem { Header = text, Icon = new Nook.App.Controls.FluentIcon { Glyph = icon, Width = 16, Height = 16 } };
        item.Click += (_, _) => action(); menu.Items.Add(item); return item;
    }
}
