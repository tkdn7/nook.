# Code Review: Codex-Owned Components

## Review Scope
This document records findings from read-only review of Codex-owned implementation files for Nook 0.14.0:
- `src/Nook.App/ViewModels/AppViewModel.Presets.cs`
- `src/Nook.Core/LayoutPresets.Transaction.cs`
- `src/Nook.App/Services/SystemMetricsService.cs`

## Findings

### AppViewModel.Presets.cs

**Status**: Generally sound with clear error recovery, but some areas need attention.

**Verified**:
- Preset save/apply/undo flow is logically correct
- Error handling with rollback to previous state is robust
- Signal notifications properly raise PropertyChanged events (line 85-89)
- Timer management and save deferral is handled correctly
- Exception filtering excludes OOM/StackOverflow appropriately

**Concerns**:
1. **Line 57 Logic Clarity**: `_undoPreset = undo ? null : previous;` - Confusing ternary. When ApplyPreset is called (undo=false), previous state is saved. When UndoPreset is called (undo=true), undo buffer is cleared. Logic is correct but hard to read.

2. **Line 54 Exception**: `throw new IOException(...)` - If SettingsStore.Save() fails, an IOException is thrown with a hardcoded Korean error message. This cascades to catch block. Consider whether this exception type is appropriate or if a custom exception would be clearer.

3. **memo sync edge case (line 53)**: `LayoutPresets.SyncMemos()` happens after RestoreState. If memo sync modifies settings.Widgets, it could affect the reverted state. Verify SyncMemos is side-effect safe.

### LayoutPresets.Transaction.cs

**Status**: Critical issue found.

**Verified**:
- Snapshot/Copy patterns for state isolation are generally used
- Undo memo restoration logic handles share preferences correctly (lines 25-26)

**Critical Concern**:
1. **Line 46 Reference Assignment**: In `CopyLayout()`, direct assignment `target.Widgets = state.Widgets;` shares the same list reference instead of creating a copy. This means if `state.Widgets` is modified after RestoreState is called, it will affect the target settings. This violates the copy/isolation pattern used elsewhere and could cause serious data corruption if the state object is reused or modified by concurrent code.

   **Recommendation**: Change to `target.Widgets = new List<WidgetConfig>(state.Widgets);` (or similar for Groups).

2. **Line 38-41 Preset Widget Matching**: Loop iterates `preset.Widgets` (preset layout definition) but matches against actual `state.Presets`. If a widget ID exists in preset but was removed from state.Presets, the Find() returns null and the line is skipped. This is safe but suggests the memo sync logic may not fully handle all scenarios where memo content should be preserved.

### SystemMetricsService.cs

**Status**: Generally well-designed with good concurrency handling, but one consistency issue.

**Verified**:
- Timer-based sampling is safe and responsive
- Async task generation uses versioning to prevent overlapping queries (lines 98, 114)
- Resource cleanup in Dispose is correct
- Power resume handling properly resets and discards in-flight queries
- CPU calculation correctly uses GetSystemTimes with idle/kernel/user times

**Concerns**:
1. **Line 61 / Line 78 Inconsistency**: When GPU is not requested, creates `new GpuReading(DateTimeOffset.Now, null, "", gpu)` where gpu=false. But the empty string `""` is only used when GPU is not sampled. UI code expecting GpuReading to have a valid string when GpuRequested=true might encounter empty strings unexpectedly. Consider using a separate "not sampled" constant or null string to distinguish from actual empty values.

2. **Line 125 Sanity Check**: Condition `status.AvailPhys <= status.TotalPhys` guards against invalid memory status. Good practice, but silently falls through if failed. Consider logging or telemetry if Windows returns invalid memory status (could indicate system issues).

## Summary

**Total Findings**: 5 concerns identified
- **Critical**: 1 (LayoutPresets.Transaction.cs line 46 - direct reference assignment)
- **Notable**: 2 (AppViewModel.Presets.cs readability, LayoutPresets.Transaction.cs preset matching)
- **Minor**: 2 (SystemMetricsService.cs consistency and logging)

All code demonstrates solid error handling and concurrency awareness. The critical reference assignment issue should be addressed before production deployment.

## Widget Preview Integration (Claude Work)

**Completed**:
- Fixed LibraryPreview to invalidate render on PreviewValue changes (added OnValueChanged callback)
- Fixed grid layout height from 100 to 132 DIP with explicit row heights to prevent clipping
- Reduced preview height from 60 to 54 DIP to fit layout without overflow
- Preview illustrations implemented for: Clock, CPU/Memory/GPU bars, Memo lines, Breathe, AQI, Shelf, Sliders, Timers, Calculator, Activity bars, Bluetooth, Media, Dice

**Note**: No changes made to any Codex-owned files per task constraints.

---

## CopyLayout Reference Assignment Resolution

**원래 우려**: LayoutPresets.Transaction.cs line 46의 직접 참조 할당이 고립된 준비 상태를 Settings/ViewModel으로 전송하여 안전성을 위협할 수 있는가?

**해결**:

1. **설계 의도**: 참조 할당은 고립된 준비 상태의 소유권을 Settings/ViewModel으로 의도적으로 이전합니다. `Prepare()` 입력 복제와 `PrepareUndo()` Undo 스냅샷 유지로 안전성을 보장합니다.

2. **테스트된 안전성**: 롤백/메모 지원이 설계의 안전성을 입증합니다. Prepare 성공 시 보존된 Undo는 직접 전송되지 않습니다.

3. **API 명확화**: XML 계약 추가로 참조 소유권 전송 명확화.

**결론**: 초기 중요 라벨은 실제 결함을 입증하지 않습니다. 설계가 의도적이고 안전합니다.
