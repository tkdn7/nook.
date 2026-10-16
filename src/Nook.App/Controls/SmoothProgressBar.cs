using System.Windows.Controls.Primitives;
using Nook.App.Services;

namespace Nook.App.Controls;

public sealed class SmoothProgressBar : ProgressBar
{
    public static readonly DependencyProperty TargetValueProperty = DependencyProperty.Register(nameof(TargetValue), typeof(double), typeof(SmoothProgressBar), new PropertyMetadata(0.0, Changed));
    public double TargetValue { get => (double)GetValue(TargetValueProperty); set => SetValue(TargetValueProperty, value); }
    private static void Changed(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        var bar = (SmoothProgressBar)sender;
        double target = Math.Clamp((double)e.NewValue, bar.Minimum, bar.Maximum);
        if (Motion.Enabled && bar.IsLoaded) bar.BeginAnimation(RangeBase.ValueProperty, Motion.To(target, 360));
        else { bar.BeginAnimation(RangeBase.ValueProperty, null); bar.Value = target; }
    }
}
