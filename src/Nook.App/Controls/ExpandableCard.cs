using Nook.App.Services;
using System.Windows.Media.Animation;

namespace Nook.App.Controls;

/// <summary>A single card grows in place. The clipping region animates layout for its neighbors.</summary>
public sealed class ExpandableCard : Border
{
    private readonly Border _body = new() { Height = 0, ClipToBounds = true, Visibility = Visibility.Collapsed };
    private readonly StackPanel _shell = new();
    private int _revision;
    public bool IsExpanded { get; private set; }
    public FrameworkElement? Editor => _body.Child as FrameworkElement;
    public double EditorHeight => _body.ActualHeight;
    public ExpandableCard(UIElement header)
    {
        CornerRadius = new(18); BorderThickness = new(1); Margin = new(0, 0, 0, 12);
        SetResourceReference(BackgroundProperty, "SurfaceBrush"); SetResourceReference(BorderBrushProperty, "LineBrush");
        _shell.Children.Add(new Border { Child = header, Padding = new(16) });
        _shell.Children.Add(_body); Child = _shell;
        Loaded += (_, _) => Motion.Reveal(this, 4);
        SizeChanged += (_, e) => { if (IsExpanded && Math.Abs(e.NewSize.Width - e.PreviousSize.Width) > 1) ResizeEditor(); };
    }
    public void Expand(StackPanel editor)
    {
        _revision++; IsExpanded = true; _body.Visibility = Visibility.Visible; _body.IsHitTestVisible = true;
        SetResourceReference(BorderBrushProperty, "MutedBrush");
        editor.Margin = new(20, 4, 20, 20); _body.Child = editor;
        ResizeEditor(); Motion.Reveal(editor, 4);
    }
    private void ResizeEditor()
    {
        if (Editor is not { } content) return;
        double width = Math.Max(320, ActualWidth > 0 ? ActualWidth - 2 : 760);
        content.Measure(new Size(width, double.PositiveInfinity));
        double target = content.DesiredSize.Height;
        if (!Motion.Enabled) { _body.BeginAnimation(HeightProperty, null); _body.Height = target; return; }
        _body.BeginAnimation(HeightProperty, Motion.To(target, 260), HandoffBehavior.SnapshotAndReplace);
    }
    public void Collapse()
    {
        int revision = ++_revision; IsExpanded = false; _body.IsHitTestVisible = false;
        SetResourceReference(BorderBrushProperty, "LineBrush");
        if (!Motion.Enabled) { Finish(); return; }
        if (Editor is { } editor) Motion.Fade(editor, 0, 130);
        var animation = Motion.To(0, 220);
        animation.Completed += (_, _) => { if (_revision == revision && !IsExpanded) Finish(); };
        _body.BeginAnimation(HeightProperty, animation, HandoffBehavior.SnapshotAndReplace);
        void Finish() { _body.BeginAnimation(HeightProperty, null); _body.Height = 0; _body.Child = null; _body.Visibility = Visibility.Collapsed; }
    }
    public void Dismiss(Action completed)
    {
        IsHitTestVisible = false;
        if (!Motion.Enabled || !IsLoaded) { completed(); return; }
        Motion.Fade(this, 0, 130);
        var animation = Motion.To(0, 200); animation.From = ActualHeight;
        animation.Completed += (_, _) => completed();
        BeginAnimation(HeightProperty, animation);
        BeginAnimation(MarginProperty, new ThicknessAnimation(Margin, new Thickness(0), Motion.Duration(200)));
    }
}
