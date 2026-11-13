# Stage 3 — interactive raw histogram (0.3.4)

0.3.4 removes the output-ceiling override, output-mapping selector and verified ADC
input at the user's request. Full Scale follows configured/per-image depth; the
visible ADU readout remains. Override-control instructions below are historical.

## Current 0.3.3 behavior (supersedes earlier range policy below)

Each enabled channel now has matching dashed min/median/max markers. Offscreen
values use clamped edge lines and directional labels with real ADU values; the zoom
does not expand. Labels are staggered and aligned inward. Detailed statistics use
channel headings and aligned label/value columns for sample count, peak, median,
MAD, min/max occurrence counts, and P1/P10/P90/P99.

At the user's request, Full Scale now uses ConfiguredDataDepth: snapshot the public
IProfileService.ActiveProfile.CameraSettings.BitDepth for live images with a valid
host image ID; history uses its own reported ImageProperties depth. Invalid/missing
depth falls back to the labeled 16-bit display scale. Settings and image-depth
disagreement is disclosed. 16-bit means 65,536 codes with endpoints 0–65,535 ADU.
No camera names/specifications are hardcoded, and pixels are never rescaled.

Generic NativeUnsigned and LeftShiftTo16 overrides compute `(2^bits)-1` and
`((2^bits)-1) << (16-bits)`. ExplicitCeiling accepts a supplied code. Overrides are
image-only and reset on selection changes. Full Scale updates on an existing
histogram immediately. The scale is configured context, not physical saturation or
proof of an exact sensor ceiling. Native ADC depth can legitimately remain Unknown.
The supplied XISF headers expose UInt16 storage without an authoritative ADC ceiling.

This default follows the user-selected convention documented at
[N.I.N.A. camera settings](https://nighttime-imaging.eu/docs/master/site/quickstart/settings/).
Earlier unknown-range-only Full Scale instructions below are historical and superseded.

95 automated checks pass; zero warnings/errors. Plot/edge/statistics previews were
rendered and visually inspected. Live 0.3.3 retest is pending. Install the two DLLs;
there is no camera-preset configuration file.

0.3.1 fixes host-themed switches hiding labels and the PlotView's white background.
Labels are independent of the switch template. Plot control and model use the host
BackgroundBrush/PrimaryBrush palette, including axes and the combined curve.

## Controls

- Defaults: Auto X, linear Y, Counts, Combined. Auto uses the union of active channels'
  P0.1–P99.9 intervals with 5% padding (at least one ADU). Include Zero is optional.
- Custom uses explicit inclusive integer ADU endpoints and stays fixed across new
  images. Invalid inputs are explained; native plot pan/zoom is disabled so the
  range controls remain authoritative. Narrow ranges show individual one-ADU bins.
- Full Scale needs a verified manual output ceiling. Unknown range produces an
  explicitly labeled observed-data Auto fallback, never a guessed 65,535 scale.
  Manual ceiling applies only to the selected image and resets on new selection.
  Saved per-camera/mode overrides and automatic verified mappings remain future work.
- Combined or independently selectable raw CFA R/G/B; Green combines both sites.
  Unavailable CFA falls back to labeled Combined. Counts are summed during viewport
  aggregation, never averaged. Percentages normalize each channel independently.
- Click near a curve for cursor interval/count/percentage and channel denominator.
  Tracker interpolation is disabled. Log Y renders zero bins as gaps without changing
  the underlying histogram. Legend and staggered dashed median markers label channels.
- Always-visible median/peak/image min/max/population are distinct from visible plot
  endpoints. Expand details for MAD/percentiles; capture context is separately expandable.
- Independent inclusive low/high ADU thresholds and trigger values. The shared trigger
  unit selects count or percentage. Equality activates a warning. Counts/percentages
  stay visible outside zoom, and denominators are explicit. Thresholds begin disabled.
  These are digital-code diagnostics, not physical full-well saturation claims.
- Actual WPF visibility defers rendering while analysis/cache continue. Updating,
  unsupported and cleared selections remove obsolete plots. Status/skips stay visible.

## Validation executed

Release build: zero warnings/errors; 79 standard checks pass. New checks cover
degenerate Auto ranges, count conservation at multiple widths, unknown Full Scale,
tracker denominators, log zeros, per-channel medians, green percentages, fixed
Custom ranges, clearing image-only overrides, inclusive/percentage triggers, invalid
input, Include Zero, hidden redraw, stale clearing and OxyPlot WPF PNG rendering.
Synthetic RGB curves, axes, legend and staggered labels were visually inspected.
This is automated renderer/adapter validation, not a live N.I.N.A. 0.3.0 host test.

OxyPlot references are pinned host assemblies with Private=false. Only the two
HistogramInspector DLLs are packaged; no duplicate OxyPlot/N.I.N.A. DLLs are shipped.

## Manual acceptance

1. Close N.I.N.A., replace both DLLs with 0.3.0, restart and capture. Confirm Ready
   and Auto chart plus matching image metadata. Re-stretch must reuse the result.
2. Switch Combined/RGB/channel toggles, Counts/Percentage, linear/log. Confirm
   channel populations/denominators and no zero-region log errors.
3. Click curves for interval readout. Narrow Custom bounds around a median to see
   one-ADU bins. Capture/select a different image: Custom endpoints must stay fixed.
4. Choose Full Scale without override: expect explicit unknown-range fallback.
   Enter a verified delivered ceiling to view 0–ceiling; new selection clears it.
5. Enable low/high explicit ADU thresholds and triggers. Inspect inclusive counts;
   zoom away and confirm warning summaries remain visible.
6. Hide, capture/select and reopen: latest cached chart should appear. Repeat
   sequence/history/clear/shutdown checks, inspect logs and measure imaging overhead.

## Deferred

Stage 4 comparisons (Off/Reference/Previous), reference checkbox, joint Auto range,
persistent profile preferences and per-camera/mode output overrides. Native-driver
range/scaling remains unknown until verified. At the user's direction, simulator
intermediate-capture counting is a known simulator limitation, not a Stage 3 blocker;
it is not claimed fixed. MeLE resource/acquisition-cadence tests remain pending.
