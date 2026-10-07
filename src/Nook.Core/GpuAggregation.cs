namespace Nook.Core;

public readonly record struct GpuEngineSample(string Instance, double Usage);
public readonly record struct GpuUsage(double? Percentage, string Engine);

public static class GpuAggregation
{
    // The same physical engine has one counter instance per process. Sum processes,
    // then select the busiest engine; summing independent engines would overcount.
    public static GpuUsage Resolve(IEnumerable<GpuEngineSample> samples)
    {
        var engines = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var sample in samples)
        {
            int start = sample.Instance.IndexOf("luid_", StringComparison.OrdinalIgnoreCase);
            if (start < 0 || !double.IsFinite(sample.Usage) || sample.Usage < 0) continue;
            string key = sample.Instance[start..];
            int duplicate = key.IndexOf('#');
            if (duplicate >= 0) key = key[..duplicate];
            engines[key] = engines.GetValueOrDefault(key) + sample.Usage;
        }
        if (engines.Count == 0) return new(null, "");
        var busiest = engines.MaxBy(p => p.Value);
        int type = busiest.Key.IndexOf("engtype_", StringComparison.OrdinalIgnoreCase);
        string label = type >= 0 ? busiest.Key[(type + 8)..] : "GPU Engine";
        return new(Math.Clamp(busiest.Value, 0, 100), label);
    }
}
