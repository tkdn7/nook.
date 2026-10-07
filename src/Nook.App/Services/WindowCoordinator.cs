using Nook.App.ViewModels;
using Nook.App.Views;
using Microsoft.Win32;

namespace Nook.App.Services;

public sealed class WindowCoordinator : IDisposable
{
    private readonly AppViewModel _vm;
    private readonly Dictionary<Guid, WidgetWindow> _windows = [];
    public WindowCoordinator(AppViewModel vm, MainWindow main)
    {
        _vm = vm;
        vm.GetPlacementArea = () => NativeWindows.WorkArea(main);
        vm.WidgetAdded += Create; vm.WidgetDeleted += Delete; vm.VisibilityChanged += Visibility; vm.SettingsChanged += Apply;
        SystemEvents.DisplaySettingsChanged += OnDisplays;
        foreach (var widget in vm.Widgets) Create(widget);
    }
    private void Create(WidgetViewModel vm)
    {
        var window = new WidgetWindow(_vm, vm) { Topmost = _vm.Settings.AlwaysOnTop };
        window.GetSnapPeers = () => _windows.Where(pair => pair.Key != vm.Model.Id && pair.Value.IsVisible && ((WidgetViewModel)pair.Value.DataContext).Model.Visible)
            .Select(pair => NativeWindows.Bounds(pair.Value)).OfType<ScreenRect>().ToArray();
        _windows.Add(vm.Model.Id, window);
        NativeWindows.Handle(window);
        if (vm.Model.Visible) window.ShowAnimated();
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
        foreach (var window in _windows.Values)
            if (new System.Windows.Interop.WindowInteropHelper(window).Handle != IntPtr.Zero) window.Close();
        _windows.Clear();
    }
}
