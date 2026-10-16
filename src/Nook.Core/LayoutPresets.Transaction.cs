namespace Nook.Core;

public static partial class LayoutPresets
{
    // The working copy contains memo updates; preparation never changes live state.
    public static AppSettings Snapshot(AppSettings settings) => Copy(settings);

    public static AppSettings Prepare(AppSettings settings, LayoutPreset preset)
    {
        var candidate = Snapshot(settings);
        Apply(candidate, Copy(preset));
        SettingsStore.ValidateAndNormalize(candidate);
        return candidate;
    }

    public static AppSettings PrepareUndo(AppSettings settings, AppSettings previous)
    {
        var candidate = Snapshot(settings);
        SyncMemos(candidate);
        var timers = candidate.Widgets.ToDictionary(w => w.Id, w => w.Countdown);
        CopyLayout(candidate, Snapshot(previous));
        var active = candidate.Presets.Find(p => p.Id == candidate.ActivePresetId);
        foreach (var widget in candidate.Widgets)
        {
            if (widget.Kind == WidgetKind.Memo && (active is null || active.ShareMemoContents))
                widget.MemoText = candidate.MemoArchive.GetValueOrDefault(widget.Id, widget.MemoText);
            if (timers.TryGetValue(widget.Id, out var timer)) widget.Countdown = timer;
        }
        SettingsStore.ValidateAndNormalize(candidate);
        return candidate;
    }

    /// <summary>
    /// Transfer an isolated prepared state into the live settings. The caller gives up
    /// ownership of state; widget view models must reference these same transferred models.
    /// Prepare/PrepareUndo clone their inputs, including the retained Undo snapshot.
    /// </summary>
    public static void RestoreState(AppSettings settings, AppSettings state)
    {
        CopyLayout(settings, state);
        settings.MemoArchive = state.MemoArchive;
        // Preserve preset names, sharing preferences and membership; only carry memo edits.
        foreach (var preset in settings.Presets)
            foreach (var memo in preset.Widgets.Where(w => w.Kind == WidgetKind.Memo))
                if (state.Presets.Find(p => p.Id == preset.Id)?.Widgets.Find(w => w.Id == memo.Id && w.Kind == WidgetKind.Memo) is { } saved)
                    memo.MemoText = saved.MemoText;
    }

    private static void CopyLayout(AppSettings target, AppSettings state)
    {
        target.Widgets = state.Widgets; target.Groups = state.Groups;
        target.Radius = state.Radius; target.Opacity = state.Opacity; target.Theme = state.Theme;
        target.AlwaysOnTop = state.AlwaysOnTop; target.MagneticMode = state.MagneticMode;
        target.ActivePresetId = state.ActivePresetId;
    }
}
