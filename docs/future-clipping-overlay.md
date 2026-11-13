# Future development: blinking low/high clipping pixels

Status: investigated and deferred. User selected this approach for future development
on 2026-11-13. No overlay implementation or runtime prototype has been produced.

## Selected approach

Use the public plugin-importable `IImageControlVM.Image` setter to alternate the
current normal display bitmap with a separately generated clipping-highlighted copy.
Preserve existing star annotations in the normal base bitmap. Keep the selected
star annotator and star-detection behavior unchanged.

Calculate masks from `RenderedImage.RawImageData.Data`, not from the stretched
display bitmap. Low/high boundaries follow the configured inclusive semantics:
low `<= threshold`, high `>= threshold`. Do not modify raw arrays, saved images,
camera settings, or acquisition sequencing.

## Proposed behavior

- Independently enable low/high highlighting, with distinguishable colors.
- Build/cache masks and highlighted display copies off the UI thread when image,
  thresholds, or display processing change. Never scan pixels on every blink.
- Blink by switching cached normal/highlighted views on the UI dispatcher.
- Track image identity/generation and the normal display revision. Cancel obsolete
  work and discard stale frames after captures, history selections, clearing,
  re-stretching, or annotation changes.
- Guard plugin-originated display changes to prevent feedback loops.
- Stop timers while hidden/disabled and at teardown. Restore the normal bitmap only
  when the currently displayed frame is still the plugin's own highlighted frame;
  never overwrite a newer host/plugin display update.
- Decide blink cadence, colors, overlapping low/high masks, and representation of
  isolated pixels at reduced zoom before implementation.

## Validation gates

Confirm that same-dimension display-bitmap swaps preserve pan, zoom, rotation,
flip, crosshair and other viewer interactions. Test monochrome/CFA images,
debayering and star annotations, and coexistence with other image-processing plugins.
Verify raw/saved data remain unchanged and thumbnails/history are not polluted.
Measure mask construction, repeated redraws, allocations, working set and acquisition
cadence on representative large frames and MeLE hardware. Cached full-frame color
bitmaps can consume substantial memory; select the representation from measurements.

## Source findings (pinned N.I.N.A. 3.2)

Baseline commit: `2393eae581145ed5b8114bf07c48ca2580540fd5`.

- [IImageControlVM](https://github.com/isbeorn/nina/blob/2393eae581145ed5b8114bf07c48ca2580540fd5/NINA.Equipment/Interfaces/ViewModel/IImageControlVM.cs)
  exposes writable `Image` and selected `RenderedImage`.
- [ImageControlVM](https://github.com/isbeorn/nina/blob/2393eae581145ed5b8114bf07c48ca2580540fd5/NINA/ViewModel/ImageControlVM.cs)
  publishes display changes and assigns the processed bitmap after preparation.
- [ImageView](https://github.com/isbeorn/nina/blob/2393eae581145ed5b8114bf07c48ca2580540fd5/NINA.WPF.Base/View/ImageView.xaml)
  applies viewer transforms to the displayed image. Its ImageAreaContent layer exists
  in the view, but no dedicated plugin overlay registration interface was established.
- [StarAnnotator](https://github.com/isbeorn/nina/blob/2393eae581145ed5b8114bf07c48ca2580540fd5/NINA.Image/ImageAnalysis/StarAnnotator.cs)
  creates a separate annotated display bitmap.
- [RenderedImage](https://github.com/isbeorn/nina/blob/2393eae581145ed5b8114bf07c48ca2580540fd5/NINA.Image/ImageData/RenderedImage.cs)
  invokes the selected annotator through star detection when annotation is enabled.

Replacing `IStarAnnotator` is not the selected approach: that is a single selected
behavior tied to the star-detection/annotation pipeline, with no independent blinking
contract. The display-bitmap route is source-supported feasibility, not yet proven
safe or performant in the live host.
