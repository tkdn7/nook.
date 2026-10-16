using System.Windows.Media.Animation;

namespace Nook.App.Services;

public static class Motion
{
    // Tests may disable motion for deterministic still renders. Respect Windows in production.
    public static bool? EnabledOverride { get; set; }
    public static bool Enabled => EnabledOverride ?? SystemParameters.ClientAreaAnimation;
    public static TimeSpan Duration(double milliseconds = 220) => TimeSpan.FromMilliseconds(Enabled ? milliseconds : 0);
    public static System.Windows.Duration InteractionDuration => new(Duration(120));
    public static DoubleAnimation To(double value, double milliseconds = 220) => new(value, Duration(milliseconds))
    { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
    public static void Fade(UIElement element, double value, double milliseconds = 160)
    {
        if (!Enabled) { element.BeginAnimation(UIElement.OpacityProperty, null); element.Opacity = value; return; }
        element.BeginAnimation(UIElement.OpacityProperty, To(value, milliseconds), HandoffBehavior.SnapshotAndReplace);
    }
    public static void Reveal(FrameworkElement element, double offset = 8)
    {
        if (!Enabled) return;
        if (!element.IsLoaded)
        {
            RoutedEventHandler? loaded = null;
            loaded = (_, _) => { element.Loaded -= loaded; Reveal(element, offset); };
            element.Loaded += loaded; return;
        }
        var translate = new TranslateTransform(); element.RenderTransform = translate;
        element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, Duration(200)));
        translate.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(offset, 0, Duration(240)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
    }
}
