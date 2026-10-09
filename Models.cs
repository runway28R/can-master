using System.Globalization;

namespace PcanCanMonitor;

public sealed record CanFrame(
    DateTime Timestamp,
    string Channel,
    int Bitrate,
    uint Identifier,
    bool IsExtended,
    bool IsRemote,
    byte[] Data,
    string? IdentifierName = null)
{
    public int Dlc => IsRemote ? Data.Length : Data.Length;
    public string IdText => IsExtended ? $"{Identifier:X8}" : $"{Identifier:X3}";
    public string DataText => Data.Length == 0 ? "—" : string.Join(' ', Data.Select(b => b.ToString("X2")));
    public string FrameType => IsRemote ? "Remote" : IsExtended ? "Extended" : "Standard";
    public string TimeText => Timestamp.ToLocalTime().ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);
}

public sealed record IdentifierDefinition(uint Identifier, bool IsExtended, string Name)
{
    public string Key => IdentifierRegistry.MakeKey(Identifier, IsExtended);
    public string IdText => IsExtended ? $"0x{Identifier:X8}" : $"0x{Identifier:X3}";
}

public sealed record PcanChannelInfo(string Channel, string Description, bool IsConnected, string? Error = null)
{
    public string DisplayName => string.IsNullOrWhiteSpace(Description) ? Channel : $"{Channel} · {Description}";
}

public enum MonitorMode
{
    Waiting,
    Live,
    Offline,
    DriverUnavailable,
    Error
}
