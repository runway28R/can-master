using Peak.Can.Basic;

namespace PcanCanMonitor;

internal static class PcanBasicPackageAdapter
{
    public static readonly IReadOnlyList<(int Rate, Bitrate Value)> CommonBitrates =
    [
        (1_000_000, Bitrate.Pcan1000),
        (500_000, Bitrate.Pcan500),
        (250_000, Bitrate.Pcan250),
        (125_000, Bitrate.Pcan125),
        (100_000, Bitrate.Pcan100),
        (50_000, Bitrate.Pcan50),
        (20_000, Bitrate.Pcan20),
        (10_000, Bitrate.Pcan10),
    ];

    public static readonly IReadOnlyList<PcanChannel> UsbChannels =
    [
        PcanChannel.Usb01,
        PcanChannel.Usb02,
        PcanChannel.Usb03,
        PcanChannel.Usb04,
        PcanChannel.Usb05,
        PcanChannel.Usb06,
        PcanChannel.Usb07,
        PcanChannel.Usb08,
    ];

    public static string GetErrorTextSafe(PcanStatus status)
    {
        try
        {
            Api.GetErrorText(status, out var text);
            return string.IsNullOrWhiteSpace(text) ? status.ToString() : text;
        }
        catch
        {
            return status.ToString();
        }
    }
}
