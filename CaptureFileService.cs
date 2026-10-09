using System.Globalization;
using System.Text;

namespace PcanCanMonitor;

public static class CaptureFileService
{
    private const string Header = "timestampUtc\tchannel\tbitrate\tidentifier\tframeType\tdlc\tdata\tname";

    public static void Export(string path, IReadOnlyList<CanFrame> frames)
    {
        using var writer = new StreamWriter(path, false, new UTF8Encoding(false));
        writer.WriteLine("# PCAN CAN Monitor capture v1");
        writer.WriteLine("# Receive-only retained traffic; tab-separated text");
        writer.WriteLine(Header);
        foreach (var frame in frames)
        {
            var id = $"0x{frame.Identifier:X}";
            var name = (frame.IdentifierName ?? string.Empty).Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
            writer.Write(frame.Timestamp.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
            writer.Write('\t'); writer.Write(frame.Channel);
            writer.Write('\t'); writer.Write(frame.Bitrate.ToString(CultureInfo.InvariantCulture));
            writer.Write('\t'); writer.Write(frame.IsExtended ? "E:" : "S:"); writer.Write(id);
            writer.Write('\t'); writer.Write(frame.IsRemote ? "remote" : "data");
            writer.Write('\t'); writer.Write(frame.Dlc.ToString(CultureInfo.InvariantCulture));
            writer.Write('\t'); writer.Write(frame.Data.Length == 0 ? string.Empty : Convert.ToHexString(frame.Data));
            writer.Write('\t'); writer.WriteLine(name);
        }
    }

    public static IReadOnlyList<CanFrame> Import(string path)
    {
        var frames = new List<CanFrame>();
        foreach (var line in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#') || line.StartsWith("timestampUtc", StringComparison.OrdinalIgnoreCase))
                continue;
            var fields = line.Split('\t');
            if (fields.Length < 8 || !DateTime.TryParse(fields[0], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var timestamp))
                continue;
            if (!int.TryParse(fields[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var bitrate))
                bitrate = 0;

            var idToken = fields[3].Trim();
            var isExtended = idToken.StartsWith("E:", StringComparison.OrdinalIgnoreCase);
            var isRemote = fields[4].Equals("remote", StringComparison.OrdinalIgnoreCase);
            idToken = idToken.Length > 2 ? idToken[2..] : idToken;
            if (idToken.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                idToken = idToken[2..];
            if (!uint.TryParse(idToken, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var identifier))
                continue;

            byte[] data = [];
            var dataToken = fields[6].Trim();
            if (dataToken.Length > 0)
            {
                try { data = Convert.FromHexString(dataToken); }
                catch (FormatException) { continue; }
            }
            frames.Add(new CanFrame(timestamp, fields[1], bitrate, identifier, isExtended, isRemote, data,
                fields[7].Trim().Length == 0 ? null : fields[7].Trim()));
        }
        return frames;
    }
}
