using System;

using System.IO;

using System.Linq;

using System.Collections.Generic;

using System.Reflection;

using System.Windows;

using System.Windows.Controls;

using System.Windows.Controls.Primitives;

using System.Windows.Media;

using System.Windows.Media.Imaging;

using Nook.Core;

using Nook.App.Controls;

using Nook.App.Services;

using Nook.App.ViewModels;

using Nook.App.Views;

using ThemeMode = Nook.Core.ThemeMode;

internal static partial class Program

{

    private static void RunV12Checks(string output, AppViewModel vm, AppSettings settings, ThemeService theme, ref int passed, ref int failed)

    {

        var today = DateOnly.FromDateTime(DateTime.Now); var oldDays = vm.ScreenTime.Ledger.Days.ToArray();

        vm.ScreenTime.Ledger.Days.Clear();

        for (int i = 0; i < 366; i++) vm.ScreenTime.Ledger.Days.Add(new() { Date = today.AddDays(-i), Apps = [new() { Id = "code", Name = "Code", Color = "#408DC1", Seconds = i == 0 ? 9000 : (i % 5) * 4500 }, new() { Id = "other", Name = "Other", Color = "#B68976", Seconds = i % 5 == 0 ? 0 : 100 }] });

        try

        {

            foreach (var mode in new[] { ThemeMode.Light, ThemeMode.Dark })

            foreach (var period in Enum.GetValues<ActivityPeriod>())

            foreach (var size in new[] { new Size(180,140), new Size(300,200), new Size(420,310), new Size(640,140), new Size(180,480) })

            {

                var widget = new WidgetViewModel(vm, new() { Kind = WidgetKind.Activity, ActivityPeriod = period, Theme = mode, Width = size.Width, Height = size.Height }); var window = new WidgetWindow(vm, widget);

                try

                {

                    var root = (FrameworkElement)window.Content; widget.SetSize(size.Width, size.Height); root.Measure(size); root.Arrange(new Rect(size)); root.UpdateLayout(); ValidateText(root, size);

                    var heat = AllChildren(root).OfType<ActivityHeatmap>().Single(); SaveV10Image(root, Path.Combine(output, $"V12-Activity-{period}-{mode}-{size.Width}x{size.Height}.png"));

                    if (heat.Cells.Count != ActivityHistory.Cells(vm.ScreenTime.Ledger.Days, today, widget.IsStandard ? period : ActivityPeriod.Week).Count || !heat.Cells.Any(x => x.Level == 4)) throw new Exception("Heatmap missing period or intensity");

                    foreach (double scale in new[] { 1.0, 1.5, 2.0 }) { var bitmap = new RenderTargetBitmap((int)(size.Width * scale), (int)(size.Height * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32); bitmap.Render(root); passed++; }

                }

                catch (Exception ex) { failed++; Console.WriteLine($"FAIL V12 heatmap {period}/{size}: {ex}"); }

                finally { window.Close(); }

            }

            foreach (var mode in new[] { ThemeMode.Light, ThemeMode.Dark })

            foreach (var kind in new[] { WidgetKind.ScreenTime, WidgetKind.Disk })

            foreach (double side in new[] { 180.0, 300.0, 480.0 })

            {

                var widget = new WidgetViewModel(vm, new() { Kind = kind, Shape = WidgetShape.Circle, Theme = mode, Width = side, Height = side }); var window = new WidgetWindow(vm, widget);

                try

                {

                    var root = (FrameworkElement)window.Content; widget.SetSize(side, side); root.Measure(new(side,side)); root.Arrange(new(0,0,side,side)); root.UpdateLayout(); ValidateText(root, new(side,side));

                    SaveV10Image(root, Path.Combine(output, $"V12-Circle-{kind}-{mode}-{side}.png"));

                    if (kind == WidgetKind.ScreenTime)

                    { var circle = AllChildren(root).OfType<ScreenTimeCircle>().First(); double radius = circle.ActualWidth * .4; double angle = 160 * Math.PI / 180; if (circle.ShareAt(new(circle.ActualWidth / 2 + Math.Cos(angle) * radius, circle.ActualHeight * .5 + Math.Sin(angle) * radius))?.Id != "code") throw new Exception("Circular hover lost app identification"); }

                    foreach (double scale in new[] { 1.0, 1.5, 2.0 }) { var bitmap = new RenderTargetBitmap((int)(side * scale), (int)(side * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32); bitmap.Render(root); passed++; }

                }

                catch (Exception ex) { failed++; Console.WriteLine($"FAIL V12 circle {kind}/{side}: {ex}"); }

                finally { window.Close(); }

            }

        }

        finally { vm.ScreenTime.Ledger.Days.Clear(); vm.ScreenTime.Ledger.Days.AddRange(oldDays); }

        try

        {

            var widget = new WidgetViewModel(vm, new() { Kind = WidgetKind.Volume, Shape = WidgetShape.Circle, Width = 300, Height = 300 }); var window = new WidgetWindow(vm, widget); window.Show(); Pump(30);

            var root = (FrameworkElement)window.Content;

            for (int i = 0; i < 32; i++)

            { double a = (i * 11.25 + 2) * Math.PI / 180; var hit = root.InputHitTest(new(root.ActualWidth / 2 + Math.Cos(a) * (root.ActualWidth / 2 - 9), root.ActualHeight / 2 + Math.Sin(a) * (root.ActualHeight / 2 - 9))) as DependencyObject; while (hit is not null && hit is not Thumb) hit = VisualTreeHelper.GetParent(hit); if (hit is not Thumb) throw new Exception("Gap in circular resize rim: " + i); }

            var origin = V10Bounds(window); var pointer = (X: origin.Right, Y: origin.Top + origin.Height / 2); double dx = 0;

            typeof(WidgetWindow).GetProperty("GetPointerPosition", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, (Func<(double X,double Y)?>)(() => (pointer.X + dx, pointer.Y)));

            window.GetSnapPeers = () => [new(origin.Right + 50, origin.Top, 300, 300)]; settings.MagneticMode = true;

            var grip = AllChildren(root).OfType<Thumb>().Single(x => x.IsVisible && Equals(x.Tag,"R")); grip.RaiseEvent(new DragStartedEventArgs(0,0));

            dx = 30; grip.RaiseEvent(new DragDeltaEventArgs(30,0)); Pump(15); var snapped = V10Bounds(window); if (Math.Abs(snapped.Right - origin.Right - 34) > 1 || snapped.Width != snapped.Height) throw new Exception("Magnetic resize did not align or preserve circle");

            dx = 60; grip.RaiseEvent(new DragDeltaEventArgs(30,0)); Pump(15); var released = V10Bounds(window); if (Math.Abs(released.Right - origin.Right - 60) > 1 || released.Width != released.Height) throw new Exception("Magnetic resize failed to release");

            grip.RaiseEvent(new DragCompletedEventArgs(60,0,false)); window.Close(); settings.MagneticMode = false; passed++;

        }

        catch (Exception ex) { settings.MagneticMode = false; failed++; Console.WriteLine("FAIL resize rim / magnetic native resize: " + ex); }

        try

        {

            var config = new AppSettings { Theme = ThemeMode.Dark, MainTheme = ThemeMode.Light }; using var own = new AppViewModel(config, new SettingsStore(Path.Combine(output,"separate-themes")), vm.Metrics, theme); var widget = own.Add(WidgetKind.Memo); var main = new MainWindow(own); main.Show(); own.Changed(); Pump(20);

            if (theme.IsDark || !widget.IsDark) throw new Exception("Main and widget themes not separated");

            config.MainTheme = ThemeMode.Dark; own.Changed(); if (!theme.IsDark || !widget.IsDark) throw new Exception("Main theme altered widgets");

            config.Theme = ThemeMode.Light; own.Changed(); if (!theme.IsDark || widget.IsDark) throw new Exception("Widget default altered main");

            main.OpenWidgetEditor(widget); Pump(20); var input = AllChildren(main).OfType<TextBox>().Single(x => System.Windows.Automation.AutomationProperties.GetName(x) == "위젯 이름"); input.Text = "나의 메모"; AllChildren(main).OfType<Button>().Single(x => Equals(x.Content,"이름 적용")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); if (widget.Title != "나의 메모") throw new Exception("Custom name not applied");

            if (main.FindName("CreateGroupButton") is not Button) throw new Exception("Group creation entry missing");
            var window = new WidgetWindow(own, widget); window.Show(); window.ResizeAnimated(1100,850); Pump(30); var bounds = V10Bounds(window); if (Math.Abs(window.ActualWidth - 1100) > 1 || Math.Abs(window.ActualHeight - 850) > 1) throw new Exception("Large memo blocked");

            window.Close(); main.Exiting = true; main.Close(); passed++;

        }

        catch (Exception ex) { failed++; Console.WriteLine("FAIL separate themes / names / memo: " + ex); }

    }
}
