namespace Nook.Core;

public static class BreatheMotion
{
    public const double PeriodSeconds = 4.5;
    public static double Expansion(double elapsed)
    {
        if (!double.IsFinite(elapsed)) return .5;
        double phase = Math.Max(0, elapsed) % PeriodSeconds;
        return phase < 2 ? .5 - .5 * Math.Cos(Math.PI * phase / 2)
            : .5 + .5 * Math.Cos(Math.PI * (phase - 2) / 2.5);
    }
    public static string Color(BreatheTone tone) => tone switch
    { BreatheTone.Lavender => "#B8A4CE", BreatheTone.Mint => "#93BCAE", BreatheTone.Peach => "#D1AD9D", _ => "#97BACC" };
}
