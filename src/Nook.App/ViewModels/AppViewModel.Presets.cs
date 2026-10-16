using Nook.App.Services;

namespace Nook.App.ViewModels;

public sealed partial class AppViewModel
{
    private AppSettings? _undoPreset;
    public bool CanUndoPreset => _undoPreset is not null;
    public string PresetError { get; private set; } = "";
    public bool IsApplyingPreset { get; private set; }
    public Func<IReadOnlyList<WidgetViewModel>, IPresetWindowChange>? PreparePresetWindows { get; set; }

    public LayoutPreset SavePreset(string name, LayoutPreset? replace = null)
    {
        CaptureLayoutRequested?.Invoke();
        var preset = LayoutPresets.Capture(Settings, name);
        if (replace is not null) { preset.Id = replace.Id; preset.ShareMemoContents = replace.ShareMemoContents; Settings.Presets[Settings.Presets.IndexOf(replace)] = preset; }
        else Settings.Presets.Add(preset);
        Settings.ActivePresetId = preset.Id;
        Changed(); return preset;
    }

    public bool ApplyPreset(LayoutPreset preset) => SwitchPreset(() => LayoutPresets.Prepare(Settings, preset), false);
    public bool UndoPreset() => _undoPreset is { } previous && SwitchPreset(() => LayoutPresets.PrepareUndo(Settings, previous), true);

    private bool SwitchPreset(Func<AppSettings> prepare, bool undo)
    {
        if (IsApplyingPreset) return false;
        bool pendingSave = _saveTimer.IsEnabled;
        _saveTimer.Stop();
        AppSettings? previous = null;
        var oldWidgets = Widgets.ToArray(); var oldGroups = Groups.ToArray();
        IPresetWindowChange? windows = null;
        bool fallbackChanged = false, persisted = false;
        IsApplyingPreset = true;
        try
        {
            CaptureLayoutRequested?.Invoke();
            previous = LayoutPresets.Snapshot(Settings);
            var candidate = prepare();
            var next = candidate.Widgets.Select(w => new WidgetViewModel(this, w)).ToArray();
            windows = PreparePresetWindows?.Invoke(next);
            LayoutPresets.RestoreState(Settings, candidate);
            ReplaceCollections(next, candidate.Groups);
            foreach (var widget in next) widget.RefreshAppearance();
            if (windows is not null) windows.Commit();
            else
            {
                fallbackChanged = true;
                foreach (var widget in oldWidgets) WidgetDeleted?.Invoke(widget);
                foreach (var widget in next) WidgetAdded?.Invoke(widget);
            }
            LayoutPresets.SyncMemos(Settings);
            if (!_store.Save(Settings)) throw new IOException(_store.Warning ?? "설정을 저장할 수 없습니다.");
            persisted = true;
            windows?.Complete();
            _undoPreset = undo ? null : previous;
            PresetError = ""; Status = "";
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            if (previous is not null)
            {
                var unsuccessful = Widgets.ToArray();
                LayoutPresets.RestoreState(Settings, previous);
                Settings.Widgets = oldWidgets.Select(w => w.Model).ToList(); Settings.Groups = oldGroups.ToList();
                ReplaceCollections(oldWidgets, oldGroups);
                if (fallbackChanged)
                {
                    foreach (var widget in unsuccessful) WidgetDeleted?.Invoke(widget);
                    foreach (var widget in oldWidgets) WidgetAdded?.Invoke(widget);
                }
            }
            PresetError = "프리셋을 적용하지 못해 이전 배치를 유지했습니다. " + ex.Message;
            if (persisted && !_store.Save(Settings)) PresetError += " 이전 설정 파일을 저장하지 못했습니다.";
            Status = PresetError;
            return false;
        }
        finally
        {
            try { windows?.Dispose(); }
            finally { IsApplyingPreset = false; }
            if (PresetError.Length > 0 && pendingSave) _saveTimer.Start();
            Theme.Apply(Settings.MainTheme ?? Settings.Theme);
            Raise(nameof(CanUndoPreset)); Raise(nameof(PresetError));
            Raise(nameof(MagneticMode)); Raise(nameof(MagneticModeStateLabel));
            Raise(nameof(WidgetCount)); Raise(nameof(HasWidgets));
            UpdateMetricDemand();
            SettingsChanged?.Invoke(); PresetApplied?.Invoke();
        }
        return true;
    }

    private void ReplaceCollections(IEnumerable<WidgetViewModel> widgets, IEnumerable<WidgetGroup> groups)
    {
        Widgets.Clear(); Groups.Clear();
        foreach (var group in groups) Groups.Add(group);
        foreach (var widget in widgets) Widgets.Add(widget);
    }
}
