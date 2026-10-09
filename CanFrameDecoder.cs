namespace PcanCanMonitor;

public sealed record CanByteDecode(int Index, byte? Value, string Meaning);

public sealed record CanFrameDecode(
    string Sender,
    string Length,
    string Message,
    IReadOnlyList<CanByteDecode> Bytes);

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

    public static CanFrameDecode Decode(CanFrame frame)
    {
        var isRtdFalse = GetKnownMessage(frame) is not null;

        var bytes = Enumerable.Range(0, 8)
            .Select(index =>
            {
                if (index >= frame.Data.Length)
                    return new CanByteDecode(index, null, "not present");

                var meaning = isRtdFalse && index == 0
                    ? "RTD False"
                    : "application meaning not defined";
                return new CanByteDecode(index, frame.Data[index], meaning);
            })
            .ToArray();

        return new CanFrameDecode(
            Sender: "not encoded in CAN",
            Length: $"DLC {frame.Dlc} · {frame.Data.Length} bytes",
            Message: isRtdFalse ? "RTD False" : "application mapping unavailable",
            Bytes: bytes);
    }
}
