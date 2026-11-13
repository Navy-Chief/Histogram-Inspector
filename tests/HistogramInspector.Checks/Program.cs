using HistogramInspector.Core;
var tracker = new SelectionTracker();
var a = new object(); var b = new object();
void Require(bool condition, string description) { if (!condition) throw new Exception(description); Console.WriteLine("PASS " + description); }
Require(!tracker.Select(null), "initial empty selection is stable");
Require(tracker.Select(a) && tracker.DistinctImages == 1, "first image counted");
Require(!tracker.Select(a) && tracker.Generation == 1, "re-stretch of same raw object ignored");
Require(tracker.Select(b) && tracker.Select(a) && tracker.DistinctImages == 3, "return to history counts as distinct selection");
Require(tracker.Select(null) && tracker.Generation == 4 && tracker.DistinctImages == 3, "destruction invalidates generation without counting an image");
Require(!tracker.Select(null), "repeated destruction is stable");
Require(tracker.Select(a) && tracker.Generation == 5, "selection after destruction accepted");
GC.KeepAlive(a); GC.KeepAlive(b);
var reused = new SelectionTracker();
Require(reused.Select(a, 10) && !reused.Select(a, 10), "same raw and capture ID deduplicates re-stretch");
Require(reused.Select(a, 11) && reused.Select(a, 12) && reused.Select(a, 13) && reused.DistinctImages == 4,
    "four captures counted when simulator reuses a raw object");
HistogramChecks.Run(args);
