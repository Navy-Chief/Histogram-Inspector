# Histogram Inspector

Future development: [blinking low/high clipping-pixel display](docs/future-clipping-overlay.md).
The selected direction uses cached display-bitmap switching through the public image
control interface; implementation is deferred pending lifecycle and performance validation.

Version **0.3.8** interactive histogram inspector for N.I.N.A. **3.2.0.9001**, using .NET 8
and WPF. The Imaging dockable calculates exact combined and eligible CFA RGB
statistics on a background worker alongside raw-array and capture context.
It does not modify images or camera settings.

The platform-neutral **Stage 2 histogram core is now implemented and tested**.
It provides exact combined/CFA counts, median, MAD, percentiles and inclusive
threshold queries. The worker is connected to the chart and numeric panel.
See [core behavior and real-image
validation](docs/stage2-core.md).

## Build and checks

Install the .NET 8.0.428 SDK and release N.I.N.A. 3.2.0.9001. Run:

```powershell
./build.ps1
# For a non-default N.I.N.A. installation:
./build.ps1 -NinaPath 'D:\Applications\NINA'
```

The script also accepts the workspace-local SDK at `.tools/dotnet/dotnet.exe`.
It verifies the host assembly hashes in `docs/host-assemblies.lock.json`, builds
Release, runs the identity and WPF/MEF adapter checks, and stages only this plugin's
two DLLs plus documentation in `artifacts/HistogramInspector`.
There are no NuGet package dependencies. Host references are non-copying references
to the installed stable release; they must never be shipped with this plugin.
The source baseline is N.I.N.A. tag `Version-3.2`, commit
`2393eae581145ed5b8114bf07c48ca2580540fd5`.

## Install for manual validation

1. Close N.I.N.A. and build the Release files above.
2. Back up any existing `HistogramInspector` plugin folder outside N.I.N.A.'s
   plugin directories.
3. Create `%LOCALAPPDATA%\NINA\Plugins\3.0.0\HistogramInspector` and copy
   `HistogramInspector.dll` and `HistogramInspector.Core.dll` from the artifacts
   folder into it. The `3.0.0` directory is the plugin compatibility generation
   used by the pinned N.I.N.A. 3.2 release, not a typo for the application version.
4. Start N.I.N.A.; confirm Histogram Inspector is listed in Plugins, then enable
   its panel in the Imaging workspace's information panel selection.
5. Follow [the manual validation checklist](docs/validation.md). This is an
   integration prototype; acquisition stability has not yet been validated.

Rollback: close N.I.N.A., move the `HistogramInspector` folder outside the Plugins
tree, and restart. Restore your backup if applicable. Do not remove other plugins
or modify files in the N.I.N.A. application installation.

## Design and limitations

- Uses public `IImageControlVM.PropertyChanged` and `RenderedImage.RawImageData`.
- Consecutive raw-object identity plus the per-image N.I.N.A. history ID deduplicates
  re-stretching while allowing simulator captures that reuse the same raw object.
  Without a valid history ID, identity falls back to the raw object. A -> B -> A counts
  three distinct selections. Weak identity references avoid retaining image buffers.
- Metadata is snapshotted without pixel scans, full-frame copies, file I/O, or
  synchronous dispatcher waits. Pending UI publication is bounded to one snapshot.
- Hidden panels keep tracking. Null selection invalidates pending context.
  Plugin teardown unsubscribes the source and prevents queued publication.
- Integer-array access precedes unsigned-array access, avoiding N.I.N.A.'s lossy
  integer-to-ushort conversion. Unsupported integer ranges display observed endpoints
  and out-of-domain counts without publishing clamped histogram statistics.
- Actual output ceiling, sensor ADC depth and scaling remain **Unknown**. Nominal
  reported-depth codes are labeled separately and are not a Full Scale decision.
- Metadata defaults can be ambiguous. Missing/sentinel fields remain Unknown;
  the adapter does not borrow settings from currently connected equipment.
- CFA sensor/pattern metadata, Bayer selection and offsets are displayed separately
  from the host's Bayered flag. Auto is unresolved and default values have unknown
  provenance. File-camera simulator capture context can differ from source headers.
- Chart: Auto/Custom/Full Scale, Include Zero, combined/CFA RGB, linear/log Y,
  counts/percentage, cursor bin readout, median markers and explicit warning thresholds.
  Full Scale uses snapshotted live-camera configured depth, or history-image reported
  depth, with a labeled 16-bit fallback. This is not a verified physical/output ceiling.
  See [Stage 3 controls and checks](docs/stage3-chart.md).
- No comparison modes, saved per-camera/mode range override, persistent settings,
  MeLE measurements or public submission yet. Simulator counting is set aside as a
  known simulator limitation at the user's direction, not claimed fixed.

## Panel validation

Install both rebuilt DLLs with N.I.N.A. closed. A selected image should progress
from Updating to Ready, showing combined min/max, median, unscaled MAD, peak and
P1/P10/P90/P99. RGB additionally requires the host's raw-CFA flag, a concrete
per-image pattern and explicit 1x1 binning from image context or saved headers.
Otherwise combined statistics remain available with an explanation.

One worker and one pending slot bound input retention. Superseded work is canceled,
stale results discarded and skipped analyses counted. Re-stretch reuses the cache.
Check snapshots, sequences, history, hidden operation, clearing and normal shutdown.
Results and metadata must always describe the same selection. Warning thresholds
begin disabled; configure explicit ADU boundaries and count/percentage triggers.
The range source is labeled. Native ADC depth may remain Unknown. Generic native-code,
left-shift and explicit-ceiling overrides are image-only; no camera specs are hardcoded
and no raw pixel values are rescaled. Reopened images do not borrow live equipment settings.

Pixel access is provisionally read-only zero-copy. Strong array references preserve
storage lifetime but cannot prevent third-party mutation. The simulator's reused
image can hide intermediate captures; this remains unresolved. Gaps in host image
IDs are not used to fabricate missing selections or analyses.

See [the original handoff](docs/NINA_Histogram_Codex_Handoff.md) and
[the validation record](docs/validation.md) for scope and remaining gates.

See [the Git workflow guide](docs/git-workflow.md) for repository setup and everyday commands.
