using System.Windows.Documents;

namespace Nook.App.Controls;

/// <summary>Original Microsoft Fluent System Icon geometry, recolored by the theme.</summary>
public sealed class FluentIcon : FrameworkElement
{
    public static readonly DependencyProperty GlyphProperty = DependencyProperty.Register(nameof(Glyph), typeof(string), typeof(FluentIcon), new FrameworkPropertyMetadata("apps", FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty ForegroundProperty = TextElement.ForegroundProperty.AddOwner(typeof(FluentIcon), new FrameworkPropertyMetadata(Brushes.Black, FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsRender));
    public string Glyph { get => (string)GetValue(GlyphProperty); set => SetValue(GlyphProperty, value); }
    public Brush Foreground { get => (Brush)GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }
    public FluentIcon() { Width = 20; Height = 20; IsHitTestVisible = false; }
    protected override void OnRender(DrawingContext dc)
    {
        if (TryFindResource("Fluent." + Glyph) is not Geometry geometry) return;
        double scale = Math.Min(ActualWidth, ActualHeight) / 24;
        dc.PushTransform(new TranslateTransform((ActualWidth - 24 * scale) / 2, (ActualHeight - 24 * scale) / 2));
        dc.PushTransform(new ScaleTransform(scale, scale));
        dc.DrawGeometry(Foreground, null, geometry);
        dc.Pop(); dc.Pop();
    }
}
