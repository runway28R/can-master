using System.Globalization;

namespace PcanCanMonitor;

public sealed record CanDecodedField(string Name, string Value);
public sealed record CanDecodedRange(string Name, string Minimum, string Maximum);

public sealed record CanFrameDecode(
    string Sender,
    string Length,
    string Message,
    IReadOnlyList<CanDecodedField> Fields);

public enum RtdState
{
    Unknown,
    FalseValue,
    TrueValue
}

public static class CanFrameDecoder
{
    public static string? GetKnownMessage(CanFrame frame)
    {
        var isRtdFalse = frame.Identifier == 0x509 &&
                         !frame.IsExtended &&
                         frame.Data.Length == 8 &&
                         frame.Data.All(byteValue => byteValue == 0);

        return isRtdFalse ? "RTD False" : null;
    }

    public static string? GetKnownMessage(CanFrame frame, CanMessageDefinition? definition)
        => definition?.Name ?? GetKnownMessage(frame);

    public static string? GetTableMessage(CanFrame frame, CanMessageDefinition? definition)
    {
        if (definition is null)
            return GetKnownMessage(frame);

        var fields = Decode(frame, definition).Fields
            .Where(field => !field.Value.Equals("not present", StringComparison.OrdinalIgnoreCase))
            .Take(2)
            .Select(field => $"{field.Name}: {field.Value}")
            .ToArray();
        return fields.Length == 0 ? definition.Name : string.Join("; ", fields);
    }

    public static bool TryGetRtdState(CanFrame frame, out RtdState state)
    {
        if (frame.IsExtended || frame.Identifier != 0x509 || frame.Data.Length == 0)
        {
            state = RtdState.Unknown;
            return false;
        }

        state = frame.Data.All(byteValue => byteValue == 0)
            ? RtdState.FalseValue
            : frame.Data[^1] == 0x01
                ? RtdState.TrueValue
                : RtdState.Unknown;
        return true;
    }

    public static CanFrameDecode Decode(CanFrame frame, CanMessageDefinition? definition = null)
    {
        var isRtdFalse = GetKnownMessage(frame) is not null;
        var messageName = definition?.Name ?? (isRtdFalse ? "RTD False" : "application mapping unavailable");
        var decodedSignals = new List<string>();
        var decodedFields = new List<CanDecodedField>();

        if (definition is not null)
        {
            foreach (var signal in definition.Signals)
            {
                var decoded = TryDecodeSignal(frame, signal, out _, out var value);
                var displayValue = decoded ? value : "not present";
                decodedFields.Add(new CanDecodedField(signal.Name, displayValue));
                if (decoded)
                    decodedSignals.Add($"{signal.Name}: {value}");
            }
        }

        var message = decodedSignals.Count == 0
            ? messageName
            : $"{messageName} · {string.Join("; ", decodedSignals)}";

        return new CanFrameDecode(
            Sender: "not encoded in CAN",
            Length: $"DLC {frame.Dlc} · {frame.Data.Length} bytes",
            Message: message,
            Fields: decodedFields);
    }

    public static IReadOnlyList<CanDecodedRange> GetRanges(IEnumerable<CanFrame> frames, CanMessageDefinition definition)
    {
        var ranges = definition.Signals
            .Select(signal => new SignalRange(signal))
            .ToArray();

        foreach (var frame in frames)
        {
            for (var index = 0; index < definition.Signals.Count; index++)
            {
                var signal = definition.Signals[index];
                if (TryDecodeSignal(frame, signal, out var numericValue, out _))
                    ranges[index].Add(numericValue);
            }
        }

        return ranges
            .Where(range => range.HasValue)
            .Select(range => new CanDecodedRange(
                range.Signal.Name,
                FormatRangeValue(range.Signal, range.Minimum),
                FormatRangeValue(range.Signal, range.Maximum)))
            .ToArray();
    }

    private static bool TryDecodeSignal(CanFrame frame, CanSignalDefinition signal, out double numericValue, out string value)
    {
        numericValue = 0;
        value = string.Empty;
        if (signal.StartByte < 0 || signal.Length is < 1 or > 8 || signal.EndByte >= frame.Data.Length)
            return false;

        ulong raw = 0;
        if (signal.ByteOrder == CanSignalByteOrder.LittleEndian)
        {
            for (var offset = signal.Length - 1; offset >= 0; offset--)
                raw = (raw << 8) | frame.Data[signal.StartByte + offset];
        }
        else
        {
            for (var offset = 0; offset < signal.Length; offset++)
                raw = (raw << 8) | frame.Data[signal.StartByte + offset];
        }

        if (signal.ValueKind == CanSignalValueKind.Boolean)
        {
            numericValue = raw;
            value = (raw != 0).ToString();
            return true;
        }

        var bits = signal.Length * 8;
        var numeric = signal.ValueKind == CanSignalValueKind.Signed
            ? SignExtend(raw, bits)
            : (long)raw;
        var scaled = numeric * signal.Scale;
        numericValue = scaled;
        value = FormatRangeValue(signal, scaled, numeric);
        return true;
    }

    private static string FormatRangeValue(CanSignalDefinition signal, double scaledValue, long? unscaledValue = null)
    {
        if (signal.ValueKind == CanSignalValueKind.Boolean)
            return scaledValue == 0 ? "False" : "True";
        if (signal.Scale == 1 && signal.Unit.Length == 0 && unscaledValue.HasValue)
            return unscaledValue.Value.ToString(CultureInfo.InvariantCulture);
        return $"{scaledValue:0.###} {signal.Unit}".TrimEnd();
    }

    private sealed class SignalRange(CanSignalDefinition signal)
    {
        public CanSignalDefinition Signal { get; } = signal;
        public bool HasValue { get; private set; }
        public double Minimum { get; private set; }
        public double Maximum { get; private set; }

        public void Add(double value)
        {
            if (!HasValue)
            {
                Minimum = value;
                Maximum = value;
                HasValue = true;
                return;
            }

            Minimum = Math.Min(Minimum, value);
            Maximum = Math.Max(Maximum, value);
        }
    }

    private static long SignExtend(ulong value, int bits)
    {
        if (bits == 64)
            return unchecked((long)value);
        var signBit = 1UL << (bits - 1);
        return (long)((value ^ signBit) - signBit);
    }
}
