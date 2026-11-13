# Validation record

Date: 2026-11-13. Product: Histogram Inspector 0.3.8 interactive inspector.

## 0.3.8 simplified chart header

Removed the top Ready/status/skipped-analysis line and the Visible ADU/scale-source
line at the user's request. Calculations, axis rendering, range controls and
statistics remain unchanged. Status/range diagnostic properties remain internal.

## 0.3.7 host star-analysis statistics

Added #Stars, HFR and HFR SD from the selected raw image's public
IStarDetectionAnalysis.DetectedStars/HFR/HFRStDev properties. Values are host
image-level detection results, repeated as capture context in channel sections,
not independently calculated raw-CFA star metrics. Negative count and NaN/invalid
HFR values display Unknown. No star detection is initiated by Histogram Inspector.

Watch the selected analysis object's PropertyChanged and refresh compact context
on the UI dispatcher. Changing images/teardown unsubscribes. Late detection updates
do not advance identity or recompute histograms, and cannot publish under a newer
selection. All 99 checks pass, including initial known values, later same-image
updates without new histogram work, missing defaults and ignoring old-image updates.
Release build has zero warnings/errors. Live 0.3.7 host validation remains pending.

## 0.3.6 N.I.N.A.-style statistics

User requested removal of ADU suffixes and P1/P10/P90/P99. Detailed statistics now
show Width/Height, Mean/population SD, Median/MAD, Min/Max occurrence counts,
Samples/Peak, Bit depth, and Gain/Offset in paired label/value columns. Dimensions
describe the full input image even for CFA subsets; sample counts describe the channel.
Capture fields are snapshotted with each analysis request, never read from current
equipment when publishing. Missing values remain Unknown. Star/HFR values are not
fabricated because the histogram engine does not perform star detection.

P1/P10/P90/P99 queries are removed from production statistics formatting/display.
Generic quantile support remains for Auto framing and median/core tests. Mean/SD
use existing histogram counts, without another pixel scan; population SD matches
N.I.N.A.'s denominator and centered deviations avoid cancellation at low variance.
Independent sorted-data reference checks now verify Mean/SD as well.

## 0.3.5 statistics-first layout

Removed the redundant Combined/channel summary line below the plot. Detailed
statistics now immediately follow the chart and are expanded by default. Warning
configuration and warning summaries follow statistics, with raw capture context last.
Threshold controls retain the same chart bindings after moving into the panel template.

## 0.3.4 simplified range controls

User confirmed 0.3.3 behavior and requested removal of the output-ceiling override,
mapping selector and verified ADC input. Those UI sections are removed. Full Scale
continues to follow configured/per-image data depth. Visible ADU endpoints and scale
provenance remain displayed. Generic calculation APIs remain internal capability,
not exposed controls. This supersedes prior instructions to use those override fields.

## 0.3.3 min/max markers, statistics and configured data depth

User validated theme/controls, requested Custom range placement (0.3.2), per-channel
min/max markers and offscreen edge indicators, and aligned N.I.N.A.-style statistics.
These are implemented. User also requested the default 16-bit display convention
and use of configured camera data depth, with no built-in camera specifications.
The profile depth is snapshotted for live images; history uses its own reported depth.
Generic mapping overrides remain available, without pixel rescaling or camera presets.

95 checks pass including profile/history isolation, same-image settings stability,
native/shifted arithmetic, immediate Full Scale updates, channel markers, edge
indicators, structured extrema counts and actual WPF statistics layout rendering.
Zero warnings/errors. Previews visually inspected. Live 0.3.3 validation is pending.
Earlier sections describe historical versions; unknown-only Full Scale behavior
is superseded by the user's explicit change to the configured/default display scale.

## 0.3.1 host theme and labels

User screenshots exposed unlabeled switches (host CheckBox template ignores Content)
and a white PlotView behind light-colored curves. Controls now use separate visible
TextBlock labels plus automation names. ChartView binds to N.I.N.A. BackgroundBrush
and PrimaryBrush; PlotView explicitly inherits that background, overriding its default
white style. PlotModel background/plot area/text/ticks/combined curve follow the same
palette. CFA colors adapt for dark/light backgrounds. Dynamic resource property
updates rebuild the model with the current colors.

All 82 checks pass, including a host-like content-hiding checkbox template, a forced
white PlotView style, and dark-to-light resource changes in a detached WPF view.
Controls render checked for labels/dark background; OxyPlot renderer checked separately.
This corrects the earlier test gap: exporting PlotModel alone cannot detect host
PlotView styling. Zero build warnings/errors. Live N.I.N.A. 0.3.1 retest is pending.

## 0.3.0 interactive chart

User validated 0.2.0 live snapshots, sequences, re-stretch, history and hidden-panel
numeric updates. Stage 3 adds the chart/controls/warnings in [stage3-chart.md](stage3-chart.md).
All 79 standard checks pass with zero build warnings/errors. OxyPlot PNG rendering
was visually checked. Live 0.3.0 host and resource validation remains pending.
User explicitly set simulator counting aside as a likely simulator artifact. It is
no longer a Stage 3 blocker and is not claimed fixed.

## 0.2.0 background analysis integration

The single worker, bounded pending slot, cancellation, generation checks, compact
metadata snapshots and bounded dispatcher publication are connected. Numeric
combined/eligible CFA results are cached while hidden. Teardown cancels, unsubscribes
and awaits worker completion without waiting synchronously for the UI dispatcher.

All 61 standard automated checks pass, including ten new worker checks: unsupported
integer handling, hidden ready/cache, re-stretch reuse, CFA eligibility, binned
exclusion, failure containment, clear during a large scan, rapid arrival/latest
context publication, active-work shutdown, and no post-stop publication. The malformed
dimension test intentionally logs an ArgumentException. Build: zero warnings/errors.
Two optional full-image worker checks also pass: source XISF combined median 600,
MAD 66; R/B populations 6,485,408 each and Green 12,970,816. This uses real pixels,
fake public host interfaces and a real WPF dispatcher, not a running N.I.N.A. host.

Live 0.2.0 snapshot/sequence/history/re-stretch/hidden/clear/shutdown validation,
buffer stability and MeLE acquisition/resource measurements remain pending.
Simulator intermediate-capture loss remains unresolved. No chart or configured
clipping thresholds yet. Input arrays are read zero-copy and provider immutability
cannot be universally guaranteed.

## Stage 1 accepted; Stage 2 core implemented

User accepted Stage 1 as an integration proof after live ATR2600C snapshot,
four-exposure sequence, re-stretch deduplication, history selection and 0.1.4
metadata fallback validation. Hidden-panel tracking was also user-validated.
Simulator intermediate-capture counting remains open, as do remaining lifecycle,
driver coverage and performance acceptance checks.

Stage 2's standalone histogram engine and correctness checks are implemented.
See [stage2-core.md](stage2-core.md) for contracts, executed real-image validation,
statistic definitions and subsequent integration gates. The numeric panel is now
connected in 0.2.0; live-host histogram and throughput validation remain pending.

## 0.1.4 reopened-image metadata

User live ATR2600C screenshots confirm snapshot generation 1, four sequence
captures advancing to 5, re-stretch remaining at 5, and history selection advancing
to 6. Simulator intermediate-capture counting remains unresolved.
The supplied live XISF header explicitly contains LIGHT, 5 seconds, RGGB, zero
Bayer offsets and 1x1 binning. N.I.N.A. XISF extraction leaves Image.ImageType and
Image.Binning unset while retaining generic headers and loading Camera.BinX/Y.
The panel now falls back to per-image IMAGETYP and validated XBINNING/YBINNING
headers, labeling their source. Explicit Image fields retain precedence so the
simulator's capture context is not overwritten by source-file headers. Missing
values remain Unknown. All 35 automated checks pass; Release builds without
warnings/errors. Reopened-file display of 0.1.4 remains pending user validation.

## 0.1.3 simulator capture identity

User confirmed four sequence exposures saved and appeared in history, while the
panel stayed at generation 2. The host log confirms four completed 0.5-second LIGHT
exposures. Source inspection found that SimulatorCamera IMAGE mode returns the
same SimulatorImage.RawImageData through CachedExposureData on every capture.
This differs from FileCamera, which loads a new object from a watched file.
ImagingVM.AddMetaData assigns a new Image.Id each capture, even on reused metadata.

Identity now combines raw reference with a snapshotted nonnegative Image.Id;
missing IDs fall back to raw identity. Re-stretch with unchanged ID is ignored.
The panel displays the host image ID for verification. Seven additional checks
exercise four captures on one raw object, refreshed metadata, and subsequent
re-stretch deduplication. All 32 checks pass; build has zero warnings/errors.
Actual sequence retest of 0.1.3 remains pending. Stage 1 sequence acceptance is open.
This also confirms that simulator metadata can mutate in place: later histogram
work must snapshot metadata rather than retain mutable metadata objects.

## 0.1.2 CFA diagnostics

Shows per-image SensorType/pattern, Bayer selection and offsets separately from
the N.I.N.A. Bayered flag. Auto remains unresolved; default Monochrome/zero values
are not proof of source-file provenance. No CFA inference or pixel conversion is
performed. Three additional adapter checks cover RGGB with a false Bayered flag,
Auto plus nonzero offsets, and replacing a CFA selection without stale metadata.
All 25 checks pass; Release compilation has zero warnings/errors.

User screenshots confirm 0.1.1 renders in Imaging, keeps distinct selections at
2 after re-stretching, and advances to 3 for another simulator image. The supplied
XISF declares RGGB and zero offsets. The user clarified that its 300-second LIGHT
was presented through the file-camera simulator as a 1-second SNAPSHOT; that is
expected capture context, not evidence of incorrect metadata handling. These are
simulator tests, not direct FITS/XISF reopen validation or physical-camera tests.

## 0.1.1 thread-ownership fix

The user installed 0.1.0 and confirmed its Plugins entry, but reported an unhandled
DependencySource thread exception when opening Imaging. The original automated
checks composed on the UI thread and missed this host-loading condition.
Background MEF composition reproduced an inaccessible worker-owned GeometryGroup.
The icon is now frozen before publication. The updated checks compose exports on
a worker and bind the icon and render both panel and plugin-options templates on
the UI dispatcher. All 22 checks pass; the Release build has zero warnings/errors.
Actual N.I.N.A. loading of 0.1.1 still needs a user retest.

The previously blank Plugins detail area now has a keyed options template with
stage status and directions to the Imaging panel.

## Automated checks executed

- Release compilation against installed stable N.I.N.A. 3.2.0.9001.
- Seven platform-neutral identity checks: initial empty state, first selection,
  repeated same raw identity, A -> B -> A, destruction, repeated destruction,
  and selection after destruction.
- Fifteen Windows adapter checks using the real MEF container, WPF resource dictionary
  and dispatcher with fake public host interfaces: composition/template lookup,
  integer representation without ushort conversion, unknown range/metadata,
  re-render deduplication, hidden tracking, history reselection, queued clear
  superseding an older context, event unsubscribe, teardown invalidation,
  startup with an already selected image, unsigned-array dimensions, worker-created
  icon access, icon binding, panel rendering, and plugin-options rendering.

These adapter checks are **not an actual N.I.N.A. plugin-loading or camera test**.
They do not establish driver buffer immutability, runtime compatibility with other
plugins, real capture metadata completeness, or performance on imaging PCs.

## Pending manual host acceptance

Use a controlled N.I.N.A. 3.2.0.9001 session. Install as described in README.
Record host version, driver, camera, plugins, test image and outcome for each row.

| Action | Expected evidence | Status |
| --- | --- | --- |
| Launch with plugin installed | Plugin composes, Imaging panel appears, no load errors in N.I.N.A. log | Pending |
| Open panel before any image | No selected image; acquisition unaffected | Pending |
| Capture unsaved manual snapshot | Dimensions, array representation, depth and available capture metadata update | Pending |
| Capture and save a sequence exposure | Panel follows selected exposure; metadata agrees with capture | Pending |
| Run a snapshot loop | Latest selected context displayed; no queued context backlog | Pending |
| Select saved image from history | Context follows selected file without borrowing current camera settings | Pending |
| Reopen mono/CFA FITS and XISF | Representation/depth reported; unknown provenance remains explicit | Pending |
| Re-stretch same image repeatedly | Distinct-selection counter stays constant | Pending |
| Select A, B, then A again | Counter advances for all three selections | Pending |
| Hide/close panel, capture, reopen | Latest selected context already available | Pending |
| Destroy/clear image | Current context clears; no stale publication | Pending |
| Shut down and restart N.I.N.A. | No teardown errors; session counter resets | Pending |
| Native ZWO and ToupTek/Ogma, then ASCOM | Raw access and metadata checked separately per path/mode | Pending |
| Representative large frames on MeLE | Acquisition cadence, CPU, memory and UI measured | Pending |

Compare diagnostic metadata against FITS/XISF headers where available. Unknown
output ceiling is expected in stage 1, including when reported bit depth is 16.
Do not treat this prototype as a physical-saturation detector. No host runtime
test of 0.1.1 or benchmark has been completed in this implementation session.

## Follow-on gate

Complete host image-identity/raw-access acceptance before stage 2 histogram work.
The full chart and automatic range interpretation are intentionally deferred.
