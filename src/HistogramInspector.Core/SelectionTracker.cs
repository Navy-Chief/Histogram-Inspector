namespace HistogramInspector.Core;

/// <summary>Caller serializes access. Identity is consecutive, not a global seen-image set.</summary>
public sealed class SelectionTracker {
    private WeakReference<object>? current;
    private int? currentCaptureId;
    public long Generation { get; private set; }
    public long DistinctImages { get; private set; }
    public bool Select(object? raw, int? captureId = null) {
        if (raw is null && current is null) return false;
        if (raw is not null && current is not null && current.TryGetTarget(out var previous)
            && ReferenceEquals(raw, previous) && currentCaptureId == captureId) return false;
        current = raw is null ? null : new(raw);
        currentCaptureId = raw is null ? null : captureId;
        Generation++;
        if (raw is not null) DistinctImages++;
        return true;
    }
}
