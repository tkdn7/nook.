namespace Nook.Core;

public static class LevelMath
{
    public static double FromAngle(double degrees)
    {
        double sweep = (degrees - 135 + 720) % 360;
        return sweep <= 270 ? sweep / 270 * 100 : sweep < 315 ? 100 : 0;
    }
    public static double DragAngle(double value, double previous, double next, double sweep = 270)
    {
        double delta = (next - previous + 540) % 360 - 180;
        return Math.Clamp(value + delta / sweep * 100, 0, 100);
    }
    public static bool Vertical(ControlOrientation orientation, double width, double height)
        => orientation == ControlOrientation.Vertical || orientation == ControlOrientation.Automatic && height > width * 1.15;
}
