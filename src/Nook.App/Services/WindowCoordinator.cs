using Nook.App.ViewModels;
using Nook.App.Views;
using Microsoft.Win32;

namespace Nook.App.Services;

public sealed class WindowCoordinator : IDisposable
{
    private readonly AppViewModel _vm;
    private readonly Dictionary<Guid, WidgetWindow> _windows = [];
    private Guid? _moving;
    private ScreenRect _moveOrigin;
    private readonly Dictionary<Guid, ScreenRect> _movePeers = [];
    public WindowCoordinator(AppViewModel vm, MainWindow main)
    {
        _vm = vm;
        vm.GetPlacementArea = () => NativeWindows.WorkArea(main);
        vm.WidgetAdded += Create; vm.WidgetDeleted += Delete; vm.VisibilityChanged += Visibility; vm.SettingsChanged += Apply;
        vm.CaptureLayoutRequested += CaptureAll;
        vm.PreparePresetWindows = PreparePreset;
        SystemEvents.DisplaySettingsChanged += OnDisplays;
        foreach (var widget in vm.Widgets) Create(widget);
    }
    private void Create(WidgetViewModel vm)
    {
        var window = CreateWindow(vm);
        _windows.Add(vm.Model.Id, window);
        if (vm.Model.Visible) window.ShowAnimated();
    }
    private WidgetWindow CreateWindow(WidgetViewModel vm)
    {
        var window = new WidgetWindow(_vm, vm) { Topmost = _vm.Settings.AlwaysOnTop };
        window.GetSnapPeers = () => _windows.Where(pair => pair.Key != vm.Model.Id && !_movePeers.ContainsKey(pair.Key) && pair.Value.IsVisible && ((WidgetViewModel)pair.Value.DataContext).Model.Visible)
            .Select(pair => NativeWindows.Bounds(pair.Value)).OfType<ScreenRect>().ToArray();
        window.DragStarting += () => BeginMove(vm);
        window.DragUpdated += rect => MoveSelection(vm.Model.Id, rect);
        window.DragEnding += () => EndMove(vm.Model.Id);
        try { NativeWindows.Handle(window); return window; }
        catch { ClosePrepared(window); throw; }
    }
    private IPresetWindowChange PreparePreset(IReadOnlyList<WidgetViewModel> widgets)
    {
        var prepared = new Dictionary<Guid, WidgetWindow>();
        var existing = Application.Current.Windows.OfType<WidgetWindow>().ToHashSet();
        try { foreach (var widget in widgets) prepared.Add(widget.Model.Id, CreateWindow(widget)); }
        catch { foreach (var window in Application.Current.Windows.OfType<WidgetWindow>().Where(w => !existing.Contains(w)).ToArray()) ClosePrepared(window); throw; }
        return new PresetWindows(this, prepared);
    }
    private static void ClosePrepared(WidgetWindow window)
    {
        try { window.Close(); }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        { try { window.Hide(); } catch (InvalidOperationException) { } }
    }
    private sealed class PresetWindows(WindowCoordinator owner, Dictionary<Guid, WidgetWindow> prepared) : IPresetWindowChange
    {
        private readonly Dictionary<Guid, WidgetWindow> _previous = new(owner._windows);
        private bool _committed, _completed;
        public void Commit()
        {
            foreach (var window in prepared.Values)
            {
                window.Topmost = owner._vm.Settings.AlwaysOnTop;
                if (((WidgetViewModel)window.DataContext).Model.Visible) window.ShowAnimated();
            }
            owner._windows.Clear();
            foreach (var item in prepared) owner._windows.Add(item.Key, item.Value);
            _committed = true;
        }
        public void Complete()
        {
            _completed = true;
            owner._moving = null; owner._movePeers.Clear();
            foreach (var window in _previous.Values) ClosePrepared(window);
        }
        public void Dispose()
        {
            if (_completed) return;
            if (_committed)
            {
                owner._windows.Clear();
                foreach (var item in _previous) owner._windows.Add(item.Key, item.Value);
            }
            foreach (var window in prepared.Values) ClosePrepared(window);
        }
    }
    private void Delete(WidgetViewModel vm)
    { if (_windows.Remove(vm.Model.Id, out var window)) window.HideAnimated(close: true); }
    private void Visibility(WidgetViewModel vm)
    {
        if (!_windows.TryGetValue(vm.Model.Id, out var window)) return;
        if (vm.Model.Visible) window.ShowAnimated(); else window.HideAnimated();
    }
    private void Apply()
    { foreach (var window in _windows.Values) window.Topmost = _vm.Settings.AlwaysOnTop; }
    private void BeginMove(WidgetViewModel lead)
    {
        _moving = lead.Model.Id; _movePeers.Clear();
        _moveOrigin = NativeWindows.Bounds(_windows[lead.Model.Id])!.Value;
        foreach (var widget in _vm.Widgets.Where(x => x.Selected && x != lead && !x.Model.Locked && x.Model.Visible))
            if (_windows.TryGetValue(widget.Model.Id, out var window) && window.IsVisible)
            { window.FreezeMoveSize(); if (NativeWindows.Bounds(window) is { } rect) _movePeers[widget.Model.Id] = rect; }
    }
    private void MoveSelection(Guid lead, ScreenRect rect)
    {
        if (_moving != lead) return;
        foreach (var (id, origin) in _movePeers)
            if (_windows.TryGetValue(id, out var window)) NativeWindows.Move(window, origin.Left + rect.Left - _moveOrigin.Left, origin.Top + rect.Top - _moveOrigin.Top);
    }
    private void EndMove(Guid lead)
    { if (_moving != lead) return; _moving = null; _movePeers.Clear(); CaptureAll(); _vm.Save(); }
    private void OnDisplays(object? sender, EventArgs e) => Application.Current.Dispatcher.BeginInvoke(() =>
    {
        foreach (var vm in _vm.Widgets)
            if (_windows.TryGetValue(vm.Model.Id, out var window)) NativeWindows.Restore(window, vm.Model);
        _vm.Save();
    });
    public void CaptureAll()
    {
        foreach (var vm in _vm.Widgets)
            if (_windows.TryGetValue(vm.Model.Id, out var window)) NativeWindows.Capture(window, vm.Model);
    }
    public void Dispose()
    {
        SystemEvents.DisplaySettingsChanged -= OnDisplays;
        _vm.WidgetAdded -= Create; _vm.WidgetDeleted -= Delete; _vm.VisibilityChanged -= Visibility; _vm.SettingsChanged -= Apply;
        _vm.CaptureLayoutRequested -= CaptureAll;
        _vm.PreparePresetWindows = null;
        foreach (var window in _windows.Values)
            if (new System.Windows.Interop.WindowInteropHelper(window).Handle != IntPtr.Zero) window.Close();
        _windows.Clear();
    }
}
