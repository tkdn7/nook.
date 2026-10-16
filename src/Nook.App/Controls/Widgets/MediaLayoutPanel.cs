namespace Nook.App.Controls;

public sealed class MediaLayoutPanel : Panel
{
    // Allocate transport first; fit artwork and readable metadata into the remaining space.
    private static Rect Bounds(FrameworkElement child, Size size)
    {
        double w = Math.Max(1, size.Width), h = Math.Max(1, size.Height);
        bool showTime = h >= 120, portrait = h >= 215 && w < 480;
        double controlsHeight = Math.Min(34, h * .48);
        double timeTop = h - 16;
        double progressTop = showTime ? timeTop - 7 : h - 3;
        double controlsTop = Math.Max(0, progressTop - 10 - controlsHeight);
        double bodyHeight = Math.Max(1, controlsTop - (showTime ? 12 : 4));
        double titleHeight = h >= 160 ? 26 : 21, artistHeight = 18;
        bool showArtist = showTime && bodyHeight >= titleHeight + artistHeight + 5;
        double metadataHeight = titleHeight + (showArtist ? artistHeight + 5 : 0);
        double cover = portrait
            ? Math.Max(1, Math.Min(Math.Min(w * .56, 164), bodyHeight - metadataHeight - 12))
            : Math.Max(1, Math.Min(Math.Min(w * .34, 144), bodyHeight));
        double coverTop = portrait ? Math.Max(0, (bodyHeight - cover - metadataHeight - 12) / 2) : 0;
        double titleTop = portrait ? coverTop + cover + 12 : Math.Max(0, (bodyHeight - metadataHeight) / 2);
        double textLeft = portrait ? 0 : cover + 12;
        double textWidth = portrait ? w : Math.Max(1, w - textLeft);

        return (child.Tag as string) switch
        {
            "cover" => new(portrait ? (w - cover) / 2 : 0, coverTop, cover, cover),
            "title" => new(textLeft, titleTop, textWidth, Math.Min(titleHeight, bodyHeight)),
            "artist" when showArtist => new(textLeft, titleTop + titleHeight + 5, textWidth, artistHeight),
            "controls" => new(0, controlsTop, w, controlsHeight),
            "progress" => new(0, progressTop, w, 3),
            "time" when showTime => new(0, timeTop, w, 16),
            _ => Rect.Empty
        };
    }
    protected override Size MeasureOverride(Size available)
    {
        var size = new Size(double.IsFinite(available.Width) ? available.Width : 256, double.IsFinite(available.Height) ? available.Height : 140);
        foreach (FrameworkElement child in InternalChildren)
        { var bounds = Bounds(child, size); child.Measure(bounds.IsEmpty ? new Size() : bounds.Size); }
        return size;
    }
    protected override Size ArrangeOverride(Size size)
    {
        foreach (FrameworkElement child in InternalChildren)
        {
            var bounds = Bounds(child, size); child.Visibility = bounds.IsEmpty ? Visibility.Collapsed : Visibility.Visible;
            if (!bounds.IsEmpty) child.Arrange(bounds);
        }
        return size;
    }
}
