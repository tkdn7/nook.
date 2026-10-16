using System.Diagnostics;
using System.Security.Cryptography;
using Nook.App.Services;
namespace Nook.App.Controls;
public sealed class DiceVisual : FrameworkElement, IDisposable
{
    public int Count { get; init; } = 2;
    public bool Portrait { get; set; }
    public Brush Ink { get; init; } = Brushes.Black;
    public Brush Surface { get; init; } = Brushes.White;
    public bool Rolling { get; private set; }
    public int[] Values { get; private set; } = [1, 1, 1];
    public event Action? Finished;
    private readonly Stopwatch _watch = new();
    private double _progress = 1;
    private int _frame = -1;
    public void Roll()
    {
        if (Rolling) return;
        if (!Motion.Enabled) { Randomize(); InvalidateVisual(); Finished?.Invoke(); return; }
        Rolling = true; _progress = 0; _frame = -1; _watch.Restart(); CompositionTarget.Rendering += Frame;
    }
    private void Randomize() { for (int i = 0; i < Values.Length; i++) Values[i] = RandomNumberGenerator.GetInt32(1, 7); }
    private void Frame(object? sender, EventArgs e)
    {
        _progress = Math.Clamp(_watch.Elapsed.TotalSeconds / .85, 0, 1); int frame = (int)(_watch.Elapsed.TotalSeconds * 12);
        if (frame != _frame) { Randomize(); _frame = frame; }
        if (_progress >= 1) { Rolling = false; CompositionTarget.Rendering -= Frame; _watch.Stop(); Finished?.Invoke(); }
        InvalidateVisual();
    }
    public void Dispose() { CompositionTarget.Rendering -= Frame; Rolling = false; }
    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight; if (w <= 0 || h <= 0) return;
        bool vertical = Portrait && Count > 1;
        double side = Math.Min(vertical ? Math.Min(w * .58, h / (Count * 1.6)) : Math.Min(w / (Count * 1.5), h * .52), 104), gap = side * .32;
        double start = ((vertical ? h : w) - (side * Count + gap * (Count - 1))) / 2;
        for (int i = 0; i < Count; i++)
        {
            var center = vertical ? new Point(w / 2, start + i * (side + gap) + side / 2) : new Point(start + i * (side + gap) + side / 2, h / 2);
            double energy = Math.Pow(1 - _progress, 2), tilt = Math.Sin(_progress * Math.PI * 9 + i * 2) * 35 * energy;
            dc.PushTransform(new TranslateTransform(0, -Math.Abs(Math.Sin(_progress * Math.PI * 5 + i)) * side * .2 * energy));
            dc.PushTransform(new RotateTransform(tilt, center.X, center.Y));
            var box = new Rect(center.X - side / 2, center.Y - side / 2, side, side);
            var shadow = Ink.CloneCurrentValue(); shadow.Opacity = .08; dc.DrawRoundedRectangle(shadow, null, new(box.X + 2, box.Y + 4, side, side), side * .17, side * .17);
            dc.DrawRoundedRectangle(Surface, new Pen(Ink, 1.3), box, side * .17, side * .17);
            foreach (var (x, y) in Pips(Values[i])) dc.DrawEllipse(Ink, null, new(box.X + side * x, box.Y + side * y), side * .057, side * .057);
            dc.Pop(); dc.Pop();
        }
    }
    public static IEnumerable<(double X, double Y)> Pips(int value)
    {
        if (value % 2 == 1) yield return (.5, .5);
        if (value >= 2) { yield return (.27, .27); yield return (.73, .73); }
        if (value >= 4) { yield return (.73, .27); yield return (.27, .73); }
        if (value == 6) { yield return (.27, .5); yield return (.73, .5); }
    }
}
