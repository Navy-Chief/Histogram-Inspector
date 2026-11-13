namespace HistogramInspector.Core;

public enum OutputMapping { ConfiguredDataDepth, NativeUnsigned, LeftShiftTo16, ExplicitCeiling }
public sealed record ConfiguredOutputRange(int Ceiling, string Description);

public static class OutputRangePolicy {
    public static ConfiguredOutputRange? Calculate(int nativeBits, OutputMapping mapping, int? explicitCeiling = null) {
        if (nativeBits is < 1 or > 16) return null;
        long nativeMaximum = (1L << nativeBits) - 1;
        long maximum = mapping switch {
            OutputMapping.NativeUnsigned => nativeMaximum,
            OutputMapping.LeftShiftTo16 => nativeMaximum << (16 - nativeBits),
            OutputMapping.ExplicitCeiling => explicitCeiling ?? -1,
            _ => -1
        };
        return maximum is > 0 and <= 65535 ? new((int)maximum,
            $"User-specified {nativeBits}-bit ADC and {mapping} mapping (this image only)") : null;
    }
}
