using System.Text;

namespace PcanCanMonitor;

public sealed class IdentifierRegistry
{
    private readonly object _sync = new();
    private readonly Dictionary<string, IdentifierDefinition> _definitions = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _filePath;

    public IdentifierRegistry()
    {
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PcanCanMonitor");
        Directory.CreateDirectory(directory);
        _filePath = Path.Combine(directory, "identifiers.txt");
        Load();
    }

    public string FilePath => _filePath;

    public static string MakeKey(uint identifier, bool isExtended) => $"{(isExtended ? 'E' : 'S')}:{identifier:X8}";

    public IReadOnlyList<IdentifierDefinition> GetAll()
    {
        lock (_sync)
        {
            return _definitions.Values
                .OrderBy(item => item.IsExtended)
                .ThenBy(item => item.Identifier)
                .ToArray();
        }
    }

    public string? GetName(uint identifier, bool isExtended)
    {
        lock (_sync)
        {
            return _definitions.TryGetValue(MakeKey(identifier, isExtended), out var definition)
                ? definition.Name
                : null;
        }
    }

    public void Set(uint identifier, bool isExtended, string name)
    {
        var cleanName = name.Trim();
        var key = MakeKey(identifier, isExtended);
        lock (_sync)
        {
            if (cleanName.Length == 0)
                _definitions.Remove(key);
            else
                _definitions[key] = new IdentifierDefinition(identifier, isExtended, cleanName);
        }
    }

    public void Replace(IEnumerable<IdentifierDefinition> definitions)
    {
        lock (_sync)
        {
            _definitions.Clear();
            foreach (var definition in definitions.Where(item => !string.IsNullOrWhiteSpace(item.Name)))
                _definitions[definition.Key] = definition with { Name = definition.Name.Trim() };
        }
    }

    public void Save()
    {
        var snapshot = GetAll();
        var builder = new StringBuilder();
        builder.AppendLine("# PCAN CAN Monitor identifier names");
        builder.AppendLine("# Format: standard|extended<TAB>identifier<TAB>name");
        foreach (var definition in snapshot)
        {
            var type = definition.IsExtended ? "extended" : "standard";
            var escapedName = definition.Name.Replace("\t", " ").Replace("\r", " ").Replace("\n", " ");
            builder.Append(type).Append('\t')
                .Append($"0x{definition.Identifier:X}").Append('\t')
                .AppendLine(escapedName);
        }
        File.WriteAllText(_filePath, builder.ToString(), new UTF8Encoding(false));
    }

    private void Load()
    {
        if (!File.Exists(_filePath))
            return;

        foreach (var line in File.ReadLines(_filePath))
        {
            if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith('#'))
                continue;
            var fields = line.Split('\t', 3);
            if (fields.Length != 3)
                continue;
            var isExtended = fields[0].Trim().Equals("extended", StringComparison.OrdinalIgnoreCase);
            var value = fields[1].Trim();
            if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                value = value[2..];
            if (!uint.TryParse(value, System.Globalization.NumberStyles.HexNumber, null, out var identifier))
                continue;
            Set(identifier, isExtended, fields[2]);
        }
    }
}
