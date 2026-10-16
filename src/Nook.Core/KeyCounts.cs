namespace Nook.Core;
public sealed class KeyCounts
{
    public long Total { get; set; }
    private readonly HashSet<int> _down = [];
    public bool Record(int key, bool down, bool injected = false)
    {
        if (injected) return false;
        if (!down) { _down.Remove(key); return false; }
        if (!_down.Add(key) || key is 16 or 17 or 18 or 91 or 92 or 160 or 161 or 162 or 163 or 164 or 165 || key < 1 || key > 254) return false;
        Total = Math.Min(254000000000, Total + 1); return true;
    }
    public void ReleaseAll() => _down.Clear();
}
