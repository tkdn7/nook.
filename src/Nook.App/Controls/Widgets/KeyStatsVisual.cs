using Nook.App.Services;
using Nook.App.ViewModels;

namespace Nook.App.Controls;

public sealed class KeyStatsVisual(AppViewModel app, WidgetViewModel vm) : FrameworkElement
{
    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0) return;
        DeskDrawing.Text(this, dc, app.Keys.Data.Total.ToString("N0"), new(w / 2, h / 2),
            Math.Clamp(Math.Min(w * .22, h * .44), 23, 38), w * .94, vm.Ink, true);
    }
}
