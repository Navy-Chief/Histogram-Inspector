# Stage 2: exact histogram core

Implemented in HistogramInspector.Core, with no N.I.N.A. or WPF dependencies.
Version 0.2.0 connects the calculation library to a single background worker and
numeric panel. Install both rebuilt DLLs to use it in N.I.N.A. Interactive chart
display and configurable clipping thresholds remain subsequent work.

## Contract

`HistogramEngine.Analyze` accepts an unsigned-16 or signed-32 sample span, positive
dimensions, optional explicit `CfaLayout`, and cancellation token. Input is read
without modification or a full-frame copy. The caller owns input stability for the
duration of the call; this API does not establish third-party buffer immutability.
Use it off the acquisition/UI notification thread when integrating the plugin.

- Exactly 65,536 one-ADU bins; allocation capacity does not define camera Full Scale.
- Signed integer input is inspected without conversion. Any value below 0 or above
  65,535 produces an unsupported result containing observed min/max and separate
  out-of-domain counts. No partial histogram or clamped statistics are published.
- Combined counts are always available for supported input. RGB counts require
  an explicitly supplied RGGB/BGGR/GRBG/GBRG layout with 1x1 binning. Unknown layout
  or binned input stays combined-only. Offsets identify the image origin's parity
  relative to the named mosaic. Both green sites contribute to Green.
- The caller must resolve CFA provenance; the engine never guesses from camera name,
  default metadata or pixel values. Declaring a layout is an explicit caller decision.
- Each immutable result retains counts and cumulative counts, not the input image.
  No public writable count arrays are exposed. Empty CFA channels have null statistics.

## Statistics and queries

- Min/max: exact occupied-bin endpoints.
- Median: mean of both central samples for even populations, including half-ADU values.
- Percentiles: linear interpolation at rank `(N - 1) * p / 100`, conventionally type 7.
- MAD: unscaled median absolute deviation from the exact median. Doubled-distance
  bins retain half-ADU medians and exact central interpolation; no Gaussian scaling.
- Peak ADU: five-bin triangular smoothing with weights 1,2,3,2,1, evaluated at
  occupied bins. Lowest ADU wins ties. This is a distribution peak, not proof of sky
  background. Counts used for all other statistics are unchanged.
- Low threshold is inclusive `value <= threshold`; high is `value >= threshold`.
  Overlapping thresholds can count the same sample in both warnings. Percentages
  use the selected channel's own population. Empty populations return 0 counts/percent.
- `CountBetween` sums inclusive contiguous intervals for later count-preserving
  chart aggregation. Threshold changes and range sums do not rescan input pixels.
- Reported depth, sensor ADC depth, scaling, camera output ceiling and physical
  saturation are not inferred by this library. Warning thresholds are explicit ADU
  coordinates supplied by the caller.

## Verification executed on 2026-11-13

Release build: zero warnings/errors. All existing identity and WPF/MEF checks pass.
Core checks include endpoints, zero/constant frames, half-ADU median/MAD, inclusive
thresholds, unsupported integer range, invalid dimensions, cancellation and empty
CFA channels. Thirty deterministic random images are compared with independent
sorted-array statistics for unsigned and integer input and all four CFA patterns
with odd dimensions and positive/negative offsets. Aggregation preserves population.

Two real, uncompressed UInt16 XISF images (6224x4168, 25,941,632 samples each) were
read by a test-only reader. Both attachment SHA-256 checksums matched. Every combined
bin and all R/G/B bins matched independent reference counts. Combined median,
percentiles and MAD matched independently sorted sample calculations.

| Input | Combined median (ADU) | Combined unscaled MAD (ADU) |
| --- | ---: | ---: |
| Supplied 300-second LIGHT source image | 600 | 66 |
| Supplied 5-second live ATR2600C LIGHT image | 48401 | 769 |

These values are not a clipping/saturation assessment or a MeLE performance benchmark.
The connected worker reproduced the source XISF's median/MAD and CFA populations
using fake public host interfaces and a real WPF dispatcher. No 0.2.0 live-host
histogram or acquisition-throughput validation has run yet.

## Reproduce

```powershell
./build.ps1
# Optional real-image check; use the workspace SDK or dotnet on PATH:
./.tools/dotnet/dotnet.exe run --project tests/HistogramInspector.Checks -c Release -- --xisf 'C:\path\sample.xisf'
```

The optional test reader deliberately supports only a single uncompressed,
little-endian UInt16 XISF attachment with declared RGGB, zero offsets and a matching
SHA-256 checksum. It is not shipped as a general image loader. The plugin will
continue to use N.I.N.A.'s raw arrays. Real samples are local, ignored by Git, and
not bundled in the release.

## Next integration gates

The single background worker, bounded pending input, generation checks, cancellation
and immutable capture context are implemented. Confirm stable input ownership and
resolve the simulator's reused-object/metadata timing before claiming every
simulator capture is analyzed. Validate the numeric panel, then add the Stage 3 chart.
