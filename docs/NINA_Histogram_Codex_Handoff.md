# NINA Advanced Raw Histogram Plugin — Codex Handoff

Prepared: November 13, 2026  
Owner: Jeff Alfeld  
Purpose: A self-contained handoff for implementing a standalone NINA plugin. No previous chat is required.

## Start here

Read this entire file before implementation. It contains the agreed product requirements, source analysis, limitations, and staged plan. User requirements take priority over proposed implementation choices. Verify API assumptions against the pinned release before coding.

**Current state:** Requirements interview and source review are complete. No plugin solution, compiled DLL, working prototype, or runtime benchmark has been produced. Repository checkouts were used for analysis only. Do not mistake the plan for implemented functionality.

**Immediate task:** Implement stage 1 only: a minimal NINA 3.2 dockable plugin that follows the selected Imaging image and reports raw array representation, dimensions, reported bit depth, range interpretation, and capture metadata. Establish correct image identity and lifecycle handling before adding the complete histogram UI. Stage 1 is intentionally an integration proof; the later stages remain the overall project goal.

Suggested opening instruction to Codex:

> Read NINA_Histogram_Codex_Handoff.md. Inspect the workspace and any AGENTS.md instructions, then implement the stage-1 integration proof against NINA Version-3.2. Use a separate plugin repository, supported public interfaces, .NET 8 and WPF. Do not change NINA core or introduce a nightly dependency. Build and run the checks available in this environment, and distinguish source/build checks from Windows host tests that have actually run. Document exact installation and manual validation steps for remaining Windows tests. Do not build the full chart until image tracking and raw access are proven.

## Why this project exists

NINA's existing histogram makes typical deep-sky background distributions look compressed against the left boundary. Jeff wants to inspect the actual raw linear distribution at meaningful ADU resolution to choose exposure length, check black clipping, and assess highlight clipping. A larger version of the existing coarse chart is insufficient. The stretched preview is a visual reference, not the data source for this tool.

This is a diagnostic tool. It must not modify saved images, camera drivers, camera settings, or exposure sequencing. Avoid claiming that absence of clipping proves an adequate exposure length.

## Development environment and working preferences

- Use C#, .NET 8 Windows and WPF for the plugin; isolate the histogram core so it can be tested without NINA or WPF.
- A Windows machine with NINA 3.2 is needed to validate plugin loading, WPF behavior and camera integration. Linux can support core development and tests; cross-compilation alone does not validate Windows runtime behavior.
- Work in a dedicated Git repository. Keep upstream NINA source separate and pinned; do not make a NINA fork a requirement.
- Jeff prioritizes imaging stability: release builds only on the data rigs. No nightly/dev dependencies.
- Target modest MeLE N5105/N95 imaging PCs, not just a powerful development workstation. CPU and thermal overhead matter.
- Representative cameras: ZWO ASI294MM mono at BIN1 (large frames, approximately 89 MB per sub) and ToupTek/Ogma IMX571 OSC/mono cameras. Do not hard-code camera identity, CFA pattern, bit depth or ADU range.
- Communicate directly and concisely. Report evidence and limitations plainly. Give progress updates during sustained work; do not imply background work is running after a turn ends.
- No need to repeat the completed requirements interview. Resolve ordinary implementation details with sensible defaults. Ask only when a real user decision or missing access blocks progress.

## Authoritative follow-up: ADU range and clipping

This section supplements and takes precedence over any weaker range assumptions in the source-review plan below.

**User requirement:** Full Scale must follow the actual imaging camera/output ADU range for the image's capture mode, not default to 65,535 merely because samples are stored as ushort. Auto and Custom remain zoom modes within the same explicit ADU coordinate system. Internal allocation of up to 65,536 bins is acceptable; allocation capacity does not define the displayed camera range.

Source findings from NINA Version-3.2:

| Path | Finding | Consequence |
|---|---|---|
| Built-in histogram | `ImageStatistics.Create` uses `(ushort)((1 << imageProperties.BitDepth) - 1)` as `maxPossibleValue`. | It follows reported image bit depth rather than universally assuming 16-bit. It does not establish an exact sensor saturation threshold. |
| Per-image data | `RawImageData.Properties.BitDepth` is accessible. | Read this without querying the camera for every frame. Preserve the interpretation with each result. |
| Connected-camera information | `CameraInfo.BitDepth` is exposed. | Useful capture context, but must not overwrite the interpretation of an older loaded image. |
| Native ToupTek/Ogma | `ToupTekAlikeCamera.BitDepth` reports native depth unless profile `BitScaling` is enabled, then 16. Download shifts samples left by `16 - nativeBitDepth` when scaling is enabled. | Reported depth alone may conceal native depth and exact highest attainable code. Verify per-image scaling provenance. |
| Native ZWO | `ASICamera.BitDepth` returns 16 with a comment that camera values are stretched to 16-bit. | Do not automatically label this output with the sensor's smaller ADC range. Actual mapping needs verification for accurate ceiling flags. |
| ASCOM | `AscomCamera.MaxADU` exists on the concrete camera implementation, but standard `CameraInfo` does not expose it. `AscomCamera.BitDepth` comes from the profile setting. | Do not assume MaxADU is a standard plugin-importable field. Avoid concrete-driver casts/reflection as the default design. Verify a supported route or use explicit override. |
| Reopened FITS | `FITS.LoadInternal` creates image data with bit depth 16. | This property alone cannot recover native ADC depth or capture-time scaling. Unknown provenance must remain visible. |

Example: for native 14-bit codes shifted left by two bits, the highest attainable code is `16,383 << 2 = 65,532`, not 65,535. This is a mathematical example and matches the inspected ToupTek shift approach; do not generalize that mapping to every vendor. Checking only for 65,535 could miss a ceiling pileup.

Keep these concepts distinct in implementation and labels:

1. Sensor ADC depth.
2. Delivered sample representation and scaling.
3. Full-scale output range / highest attainable digital code.
4. User-configured near-clipping warning threshold.
5. Physical full-well saturation, which cannot be inferred universally from the above.

Use verified metadata/mapping where available. Provide a per-camera/capture-mode override when required; its exact UI is a design choice, not yet specified. Do not infer the output ceiling from the brightest pixel in one exposure. Unknown range must not prevent exact pixel counts or an explicitly labeled observed-data view. Manual warning thresholds remain available.

Pinned additional source locations (same NINA commit as the plan below):

- `NINA.Equipment/Equipment/MyCamera/CameraInfo.cs`, BitDepth property around line 80.
- `NINA.Equipment/Equipment/MyCamera/ASICamera.cs`, BitDepth around line 305.
- `NINA.Equipment/Equipment/MyCamera/ToupTekAlikeCamera.cs`, download/scaling around lines 877–895; BitDepth around line 1047.
- `NINA.Equipment/Equipment/MyCamera/AscomCamera.cs`, MaxADU around line 482; BitDepth around line 851.
- `NINA.WPF.Base/ViewModel/Equipment/Camera/CameraVM.cs`, CameraInfo population around line 417.
- `NINA.Image/FileFormat/FITS/FITS.cs`, image-data creation around line 163.

## Requirements versus recommendations

The behavior listed as agreed in the plan below comes from the completed interview. Architecture, algorithms, performance targets, percentage denominator, threshold inclusivity and component names are engineering proposals and can be refined without reopening settled product choices.

In particular:

- User explicitly selected Counts as default; Percentage is an additional selectable mode.
- User explicitly selected CFA RGB, not a G1/G2 UI requirement.
- User explicitly requested a reference checkbox, session-only reference lifetime, and both Reference/Previous comparison modes.
- User explicitly requested readiness when the hidden panel opens. Always-on background analysis with cached compact results is the proposed way to achieve that; exact implementation depends on measurement.
- Comparison Auto resets to include both distributions. A manually selected Custom range remains fixed, per the earlier explicit requirement.
- Never advance Previous solely because the same raw image is re-stretched or re-rendered. Returning to a different historical image does count as a new displayed image.

## Stage-1 deliverables and acceptance checklist

Deliver source, pinned dependency configuration, README installation/rollback instructions, and a clear validation record. A diagnostic panel is sufficient initially; a complete chart is not required in this stage.

- [ ] Plugin composes and loads in NINA 3.2 without nightly dependencies or bundled conflicting NINA assemblies.
- [ ] Panel appears as an Imaging dockable and uses supported MEF exports/imports.
- [ ] Selected image metadata is shown, including dimensions, array representation and reported bit depth.
- [ ] Unsaved manual snapshot updates the panel.
- [ ] Saved sequence exposure updates the panel.
- [ ] Reopened image/history selection updates the panel.
- [ ] Re-stretching the same raw image does not increment distinct-image tracking.
- [ ] Closing/hiding the panel does not lose tracking; reopening shows the current image context.
- [ ] Image destruction clears current context safely.
- [ ] Unknown metadata/range is explicit; no fabricated capture settings or universal 65,535 ceiling.
- [ ] Notification callback does no pixel scan, file I/O, synchronous UI wait or full-frame copy.
- [ ] Teardown unsubscribes handlers and stops work without disturbing acquisition.
- [ ] Tests distinguish executed automated checks from pending Windows/manual/camera validation.

If NINA cannot be run in the session, still complete buildable source and all available checks, then document the exact host validation steps. Do not claim runtime success or performance measurements that have not occurred.

## Full source review and staged implementation plan

The following is the consolidated implementation plan, including source links and the complete first-release behavior.

---

# NINA Advanced Raw Histogram — Code Review and Implementation Plan

Date: 2026-11-13  
Status: source analysis and implementation plan; no plugin has been built or runtime-tested.

## Conclusion

The agreed first release is feasible as a standalone NINA 3.2 plugin, without a NINA fork or nightly dependency. Use the displayed image's original pixel data, independent histogram computation, and a dockable WPF panel. Do not use the pre-save event as the primary input.

This conclusion is based on inspection of release source, not a working prototype. Buffer behavior with other plugins, performance on the MeLE, file-loading edge cases, and exact packaged dependency compatibility remain validation gates.

## Source baseline

- Repository: https://github.com/isbeorn/nina
- Tag: `Version-3.2`
- Commit: `2393eae581145ed5b8114bf07c48ca2580540fd5`
- NINA.Plugin project declares version `3.2.0.9001` and targets `net8.0-windows`.
- Current plugin template inspected separately at commit `4c90fac4ecbe0e179c16a16a5103d28c69407dd5`. Its generated project targets .NET 10 and NINA `3.3.0.1060-nightly`; do not adopt those defaults for this project. Use its structure as guidance and adapt against 3.2 APIs.

All NINA source links below are pinned to the inspected commit.

## Verified integration points

| Finding | Evidence | Implementation consequence |
|---|---|---|
| Plugins can receive the imaging mediator and image control interface through MEF composition. | [PluginLoader.GetContainer](https://github.com/isbeorn/nina/blob/2393eae581145ed5b8114bf07c48ca2580540fd5/NINA.Plugin/PluginLoader.cs#L491) exports both. | Constructor-import supported interfaces; no reflection into internal view models. |
| A prepared-image notification exists in stable 3.2. | [IImagingMediator](https://github.com/isbeorn/nina/blob/2393eae581145ed5b8114bf07c48ca2580540fd5/NINA.Equipment/Interfaces/Mediator/IImagingMediator.cs#L62) exposes `ImagePrepared`; its arguments contain `IRenderedImage`. | Saving is unnecessary for receiving an image notification. |
| The image control exposes the selected rendered image and property-change notifications. | [IImageControlVM](https://github.com/isbeorn/nina/blob/2393eae581145ed5b8114bf07c48ca2580540fd5/NINA.Equipment/Interfaces/ViewModel/IImageControlVM.cs#L33) inherits `IDockableVM`, hence `INotifyPropertyChanged`; [RenderedImage setter](https://github.com/isbeorn/nina/blob/2393eae581145ed5b8114bf07c48ca2580540fd5/NINA/ViewModel/ImageControlVM.cs#L446) raises notification. | Prefer `PropertyChanged("RenderedImage")` as the display-selection signal. Read the existing value at startup, and handle null on image destruction. |
| The prepared event happens before the selected rendered-image property is assigned. | [ProcessAndUpdateImage](https://github.com/isbeorn/nina/blob/2393eae581145ed5b8114bf07c48ca2580540fd5/NINA/ViewModel/ImageControlVM.cs#L631). | Do not read the image control's current image inside `ImagePrepared` and assume it is the event's new image. Property notification avoids that mismatch. |
| Raw pixels remain available after debayering and stretching. | [IRenderedImage.RawImageData](https://github.com/isbeorn/nina/blob/2393eae581145ed5b8114bf07c48ca2580540fd5/NINA.Image/Interfaces/IRenderedImage.cs#L31), [RenderedImage](https://github.com/isbeorn/nina/blob/2393eae581145ed5b8114bf07c48ca2580540fd5/NINA.Image/ImageData/RenderedImage.cs), [DebayeredImage](https://github.com/isbeorn/nina/blob/2393eae581145ed5b8114bf07c48ca2580540fd5/NINA.Image/ImageData/DebayeredImage.cs). | Analyze `RenderedImage.RawImageData.Data`, never `Image` or `OriginalImage` bitmaps. |
| Manual snapshots are prepared whether or not saving is enabled; looping uses the same path. | [AnchorableSnapshotVM.SnapImage](https://github.com/isbeorn/nina/blob/2393eae581145ed5b8114bf07c48ca2580540fd5/NINA/ViewModel/Imaging/AnchorableSnapshotVM.cs#L370). | Unsaved test exposures are supported by the chosen design. Looping feasibility depends on measured throughput. |
| Image-history selection loads the original file and sends it through preparation. | [ThumbnailVM.SelectImage](https://github.com/isbeorn/nina/blob/2393eae581145ed5b8114bf07c48ca2580540fd5/NINA/ViewModel/ThumbnailVM.cs#L147). | Previously saved images selected in NINA follow the same histogram path. This does not prove every arbitrary file-open action in every NINA workspace uses this display. |
| Display reprocessing also runs `ProcessAndUpdateImage`. | [ProcessImageHelper](https://github.com/isbeorn/nina/blob/2393eae581145ed5b8114bf07c48ca2580540fd5/NINA/ViewModel/ImageControlVM.cs#L526). | Deduplicate consecutive notifications by raw-image object identity; changing stretch must not advance Previous. |
| The existing histogram discards its full-resolution counts after constructing a coarse plot. | [ImageStatistics](https://github.com/isbeorn/nina/blob/2393eae581145ed5b8114bf07c48ca2580540fd5/NINA.Image/ImageData/ImageStatistics.cs#L26) declares resolution 100 and builds local 65,536-value counts before grouping. | Compute an independent histogram. Enlarging or reusing the existing plotted histogram cannot recover the lost detail. Endpoint grouping may produce an extra endpoint group; “100” is the resolution constant, not a guarantee of exactly 100 plotted points. |
| Plugin dockables and XAML dictionaries are supported. | [PluginLoader.Compose](https://github.com/isbeorn/nina/blob/2393eae581145ed5b8114bf07c48ca2580540fd5/NINA.Plugin/PluginLoader.cs#L462), [DockManagerVM](https://github.com/isbeorn/nina/blob/2393eae581145ed5b8114bf07c48ca2580540fd5/NINA/ViewModel/DockManagerVM.cs#L135). | Export `IDockableVM` and a `ResourceDictionary` containing its data template. |

## Agreed first-release behavior

- Follow the main Imaging display's selected image, including unsaved snapshots, sequence images, and disk-loaded images routed through that display.
- Analyze lights, flats, darks, bias, and snapshots identically. Threshold flags describe data, not whether a frame is acceptable.
- Preserve one-ADU bins for unsigned 16-bit input; rendering may aggregate bins while preserving counts.
- Auto zoom by default; Custom and Full Scale selectable. Include Zero is an Auto option.
- Full Scale and default ADU clipping boundaries follow the actual camera/output range for the image's capture mode, not the storage container's 65,535 ceiling. Preserve this range with each comparison result.
- Always display visible X-range endpoints, a labeled median marker, and statistics. Distinguish plot endpoints from image minimum/maximum.
- Keep Custom fixed until the user explicitly changes the mode or range.
- Offer linear/log Y and Counts/Percentage, with Counts as the default unit.
- User-selectable combined or CFA RGB distributions. Green combines both raw green site populations; this is not debayered RGB.
- Independently configurable low/high threshold flags, including ADU threshold and count/percentage trigger. Show counts and percentages with explicit inclusive boundary semantics: low <= threshold, high >= threshold.
- Always-visible statistics: median, peak ADU, minimum/maximum, low/high counts and percentages. Expandable MAD and percentiles.
- Comparison modes: Off, Reference, Previous. Reference is set with a checkbox on the currently displayed histogram, retained until cleared/replaced, and reset at NINA restart. Previous is the immediately preceding distinct displayed-and-analyzed image.
- Auto comparison zoom includes the useful distributions of both images and all enabled channels. Custom range remains fixed.
- Current/comparison metadata includes exposure, filter, gain, offset, binning, camera, image type, capture time and readout mode where supplied. Missing fields display Unknown, not fabricated values from current equipment settings.
- Compute while hidden and cache results. Defer chart work while hidden; opening the panel displays the latest completed result without waiting for the next capture. Show explicit Updating status if that image is still being analyzed.

## Recommended architecture

Use a separate plugin solution with three projects: a platform-neutral histogram core, a .NET 8 Windows/WPF NINA adapter and panel, and core/integration tests. Keep NINA dependencies out of the core so exact histogram and comparison behavior can be tested independently.

| Component | Responsibility |
|---|---|
| Plugin manifest/lifecycle | Stable GUID, version metadata, initialize/teardown, coordinated service disposal. |
| Display image adapter | Import `IImageControlVM`; observe selected raw data; capture metadata and CFA interpretation; assign display generation IDs. |
| Analysis coordinator | Single background worker, bounded pending slot, cancellation, stale-result checks, error containment. |
| Histogram engine | Integer counts, statistics, threshold queries, CFA splitting, full-resolution result. |
| Comparison state | Current, previous completed result, pinned reference, reference checkbox state. |
| View-range policy | Auto/Custom/Full, Include Zero, comparison ranges, active channels. |
| Plot adapter | OxyPlot series, bin aggregation, axes, cursor readout and median markers. |
| Settings | Persist UI preferences and thresholds per NINA profile; keep image results/reference session-only. |

### Display event handling

1. Subscribe once to the imported image control's property changes; inspect its current `RenderedImage` after subscribing.
2. For a new selection, take the `RawImageData` reference and a small metadata snapshot. Assign a monotonic generation number. Consecutive notifications carrying the same raw-data object do not create a new comparison step.
3. Place the work in a bounded latest-pending slot and return immediately. No pixel scan, array clone, blocking wait, dispatcher wait or file I/O in the host notification.
4. The worker computes off the UI thread, using the actual integer input representation.
5. Publish immutable histogram results on the WPF dispatcher only if their generation still matches the selected image. Do not show statistics for an obsolete image under the new image's metadata.
6. On null selection, invalidate current work and clear the current display; retain an explicitly pinned reference until cleared/replaced or shutdown.

Rapid-loop overload must be visible: coalesce obsolete pending work, count skipped analyses, and show Updating/Skipped status. “Previous” means the previous displayed image whose analysis completed, consistent with the agreed wording; skipped frames cannot be compared. For ordinary snapshots/sequence cadence, every distinct displayed frame should complete. Test this explicitly rather than silently claiming every loop frame is analyzed.

### Ownership and memory

The inspected `BaseImageData`, `ImageArray`, and `ImageArrayInt` hold managed arrays. The interfaces expose no disposal contract; inspected rendering code retains the raw image and creates separate display representations. A strong reference keeps managed storage alive. This supports a provisional read-only zero-copy implementation; it does not establish immutability against every third-party image provider/plugin.

Retain raw input only during active/pending analysis. Store Current, Previous and Reference as histogram results plus metadata, not full image buffers. A 65,536-bin `long[]` is 512 KiB; combined plus R/G/B is 2 MiB per result before small ancillary data. Reuse result objects when reference/current/previous coincide. Bound queued input references to avoid retaining a stack of large camera frames. Do not add forced garbage collections.

Validate unchanged input values across asynchronous analysis with native and ASCOM cameras and installed image-processing plugins. If a provider demonstrably reuses/mutates arrays, isolate that provider with a measured snapshot strategy; do not assume an asynchronous copy solves concurrent mutation.

### Data formats and clipping correctness

[IImageArray](https://github.com/isbeorn/nina/blob/2393eae581145ed5b8114bf07c48ca2580540fd5/NINA.Image/Interfaces/IImageArray.cs) exposes both `FlatArrayInt` and `FlatArray`. Inspect the integer array first. [ImageArrayInt.FlatArray](https://github.com/isbeorn/nina/blob/2393eae581145ed5b8114bf07c48ca2580540fd5/NINA.Image/ImageData/ImageArray.cs#L60) allocates a converted array and maps values below zero or above 65,535 to 65,535. Using it blindly could produce false high-clipping reports.

First release guarantees exact histograms for unsigned 16-bit samples and integer arrays whose values are within that domain. For integer data outside it, report Unsupported range and observed out-of-range information; never silently clamp and claim exactness. A sparse/wider integer histogram is a later extension unless actual test-camera data makes it necessary.

Full Scale and default clipping boundaries must use the actual camera ADU range for the image's capture/readout mode, not a default 0–65,535 storage range. For unscaled unsigned output this ordinarily means 0–4,095 for 12-bit, 0–16,383 for 14-bit, and 0–65,535 for 16-bit. Verify the driver's output mapping: sensor ADC bit depth alone does not establish whether delivered values have been scaled or shifted. Axis values, histogram samples and clipping thresholds must use the same ADU coordinate system; any conversion must be explicit and validated.

Resolve the range from reliable per-image/capture metadata and verified camera/driver output semantics. Snapshot it for Current, Previous and Reference, so opening an older image does not borrow the connected camera's range. If the range cannot be established, label it Unknown and offer a user override; do not silently assume 65,535 or infer the camera ceiling from the brightest pixel in that exposure. Any observed-data view must be identified as such, rather than labeled Full Scale. User-defined warning thresholds remain selectable within the established range. A digital output ceiling is not proof of physical full-well saturation.

[FITS.LoadInternal](https://github.com/isbeorn/nina/blob/2393eae581145ed5b8114bf07c48ca2580540fd5/NINA.Image/FileFormat/FITS/FITS.cs#L117) reads pixels through a ushort path and warns that multidimensional images are reduced to the first two axes. [BaseImageData.FromFile](https://github.com/isbeorn/nina/blob/2393eae581145ed5b8114bf07c48ca2580540fd5/NINA.Image/ImageData/BaseImageData.cs#L563) also converts common bitmap formats to Gray16. Therefore disk analysis means the data exposed by NINA's loader, not guaranteed original FITS floating-point precision or original RGB channels. Raw mono/CFA integer FITS and XISF are the intended initial validation inputs; do not claim an arbitrary JPEG is raw linear sensor data.

### CFA interpretation

Count raw mosaic sites directly in the same scan as the combined histogram. Resolve known per-image pattern and parity offsets first; use an explicit profile override when appropriate, with the source visible. `ImageMetaData.Camera` contains SensorType, BayerPattern, BayerOffsetX/Y. FITS header parsing reads BAYERPAT and XBAYROFF/YBAYROFF. Account for ROI parity and odd dimensions. Disable separated channels with an explanation when the pattern is unknown or binning destroys the CFA mosaic; do not guess RGGB.

Counts shows true populations, so green naturally has roughly twice as many samples as red or blue. Percentage normalizes each channel by its own sample count, and each frame independently. Tooltips must state that denominator. Give each enabled channel its own median marker/statistics selection to avoid presenting a mixed-CFA median as a channel median.

### Statistics, zoom and plotting

Compute exact median and percentiles from cumulative counts, including both central samples for an even-sized population. Compute exact MAD from the histogram, preserving half-ADU medians. Prefix counts make threshold changes cheap and avoid rescanning pixels.

Estimate a display peak from a lightly smoothed copy of counts; preserve original counts for statistics and plotted values. Describe it as a distribution peak, not guaranteed sky background: large nebulae, gradients and flats can dominate the population.

Start Auto with configurable central-percentile bounds plus padding; derive bounds separately per enabled channel and compared image, then take their union. Handle zero-width, all-zero, all-saturated and multimodal distributions explicitly. Include Zero extends the lower limit to zero. Threshold flags remain visible even when their pixels lie outside the zoom. Reference/previous activation recalculates Auto bounds.

Reuse the host-compatible OxyPlot version first: NINA 3.2 references OxyPlot.Wpf 2.2.0 and Contrib 2.1.2. Verify actual runtime binding before packaging; avoid shipping competing host assemblies. Aggregate contiguous ADU bins to match chart width and zoom; sum rather than average counts. Show represented ADU interval and its count in cursor readout. At sufficient zoom, render individual bins. Handle zero counts correctly on log Y without changing the underlying data.

Reference curves use distinct line style as well as color. Display one shared X/Y scale for both frames, and label metadata differences. Do not normalize exposure time automatically: a change in distribution is part of the diagnostic evidence.

## Implementation stages and completion gates

1. **Stable host integration spike.** Build a minimal .NET 8 plugin against 3.2-compatible assemblies/package; export panel and template; observe selected raw image; display dimensions, representation and metadata. Validate unsaved snapshot, saved sequence, loop, thumbnail reload, stretch change, image destruction and hidden panel. Confirm exact 3.2 package availability and installation path rather than assuming current template defaults.
2. **Exact histogram core.** Implement combined/CFA counts, median/MAD/percentiles, threshold queries and format checks. Verify against independently generated expected data and real raw frames. Cover values 0/65,535, half-ADU median, odd image dimensions, Bayer offsets, non-16-bit integer data and missing metadata.
3. **Interactive display.** Add Auto/Custom/Full, Include Zero, labeled range endpoints and median, linear/log Y, counts/percentage, RGB toggles, details and configurable clipping flags. Verify that aggregation preserves total populations at every zoom.
4. **Comparison and settings.** Add Off/Reference/Previous, reference checkbox/clear, metadata comparison and joint Auto framing. Test A -> B -> opened C, re-stretch B, pinned reference replacement, rapid arrivals, missing metadata and restart reset. Persist preferences, never the reference image.
5. **Performance and stability.** Benchmark representative ASI294MM BIN1 and IMX571 frames on the MeLE N5105/N95, using the real native/ASCOM paths. Compare plugin enabled/disabled for preparation, download/save cadence, CPU, allocations, working set and UI responsiveness. Include the 15-second loop and a sustained session; verify memory plateaus and no stale results appear. Keep one worker initially; add parallel counting only if measurements justify it.
6. **Private release package.** Pin dependencies; include installation/removal instructions, compatibility, known format limits and changelog. Run NINA 3.2 Windows loading/teardown checks, then a controlled imaging test before unattended use. Public manifest submission is a separate release action.

Provisional performance goals, to be accepted or revised from measurements: notification overhead below 1 ms at the 95th percentile; analysis typically below 1 second for the listed frames on a MeLE; no material acquisition-cadence regression; no unbounded memory growth. These are design targets, not measured claims.

## Deferred work

- Per-filter saved zoom presets, defaulting to Auto.
- Flashing clipping-pixel overlays on NINA's displayed image. The reviewed public imaging interfaces have bitmap setters and a replaceable star-annotation behavior, but no dedicated independent overlay contract was found. Replacing annotation risks coupling clipping to star detection or conflicting with other annotators; it is not the first-release approach.
- Wider-range/float-original file analysis, historical trends, ROI diagnostics and separate G1/G2 display.

## Specific remaining engineering uncertainties

- Exact package/deployment compatibility with the installed 3.2 build and existing plugins.
- Buffer reuse or mutation outside the inspected built-in data/rendering classes.
- CFA metadata correctness for each camera, driver, ROI and binned capture mode.
- Arbitrary file-open routes outside the main Imaging display; if needed, add a plugin Open Image command using the imported image factory and imaging mediator, rather than a second independent viewer.
- CPU and thermal impact under MeLE workload. Background threading prevents UI-thread scanning but does not eliminate CPU contention.

The first integration spike should resolve these practical risks before investing in the finished chart UI.
