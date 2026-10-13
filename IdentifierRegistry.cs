using System.Text;

namespace PcanCanMonitor;

public sealed class IdentifierRegistry
{
    public const string DefaultListName = "SRE-7b";

    private readonly object _sync = new();
    private readonly Dictionary<string, Dictionary<string, IdentifierDefinition>> _lists = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _filePath;
    private string _activeListName = DefaultListName;

    public IdentifierRegistry()
    {
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PcanCanMonitor");
        Directory.CreateDirectory(directory);
        _filePath = Path.Combine(directory, "identifiers.txt");
        Load();
    }

    public string FilePath => _filePath;

    public string ActiveListName
    {
        get
        {
            lock (_sync)
                return _activeListName;
        }
    }

    public IReadOnlyList<string> ListNames
    {
        get
        {
            lock (_sync)
                return _lists.Keys.ToArray();
        }
    }

    public static string MakeKey(uint identifier, bool isExtended) => $"{(isExtended ? 'E' : 'S')}:{identifier:X8}";

    public IReadOnlyList<IdentifierDefinition> GetAll()
    {
        lock (_sync)
        {
            return GetActiveDefinitionsUnsafe()
                .Values
                .OrderBy(item => item.IsExtended)
                .ThenBy(item => item.Identifier)
                .ToArray();
        }
    }

    public string? GetName(uint identifier, bool isExtended)
    {
        lock (_sync)
        {
            return GetActiveDefinitionsUnsafe().TryGetValue(MakeKey(identifier, isExtended), out var definition)
                ? definition.Name
                : null;
        }
    }

    public void SelectList(string name)
    {
        var cleanName = NormalizeListName(name);
        lock (_sync)
        {
            if (!_lists.ContainsKey(cleanName))
                throw new ArgumentException($"Identifier list '{cleanName}' does not exist.", nameof(name));
            _activeListName = cleanName;
        }
        Save();
    }

    public void CreateList(string name)
    {
        var cleanName = NormalizeListName(name);
        lock (_sync)
        {
            if (_lists.ContainsKey(cleanName))
                throw new ArgumentException($"An identifier list named '{cleanName}' already exists.", nameof(name));

            _lists[cleanName] = new Dictionary<string, IdentifierDefinition>(StringComparer.OrdinalIgnoreCase);
            _activeListName = cleanName;
        }
        Save();
    }

    public void Set(uint identifier, bool isExtended, string name)
    {
        var cleanName = name.Trim();
        var key = MakeKey(identifier, isExtended);
        lock (_sync)
        {
            var definitions = GetActiveDefinitionsUnsafe();
            if (cleanName.Length == 0)
                definitions.Remove(key);
            else
                definitions[key] = new IdentifierDefinition(identifier, isExtended, cleanName);
        }
    }

    public void Replace(IEnumerable<IdentifierDefinition> definitions)
    {
        lock (_sync)
        {
            var replacement = new Dictionary<string, IdentifierDefinition>(StringComparer.OrdinalIgnoreCase);
            foreach (var definition in definitions.Where(item => !string.IsNullOrWhiteSpace(item.Name)))
                replacement[definition.Key] = definition with { Name = definition.Name.Trim() };
            _lists[_activeListName] = replacement;
        }
    }

    public void Save()
    {
        string activeListName;
        KeyValuePair<string, Dictionary<string, IdentifierDefinition>>[] lists;
        lock (_sync)
        {
            activeListName = _activeListName;
            lists = _lists.ToArray();
        }

        var builder = new StringBuilder();
        builder.AppendLine("# PCAN CAN Monitor identifier names");
        builder.AppendLine("# Format: [list name] followed by standard|extended<TAB>identifier<TAB>name");
        builder.AppendLine($"# Active list: {activeListName}");
        foreach (var list in lists)
        {
            builder.AppendLine();
            builder.Append('[').Append(list.Key).AppendLine("]");
            foreach (var definition in list.Value.Values
                         .OrderBy(item => item.IsExtended)
                         .ThenBy(item => item.Identifier))
            {
                var type = definition.IsExtended ? "extended" : "standard";
                var escapedName = definition.Name.Replace("\t", " ").Replace("\r", " ").Replace("\n", " ");
                builder.Append(type).Append('\t')
                    .Append($"0x{definition.Identifier:X}").Append('\t')
                    .AppendLine(escapedName);
            }
        }
        File.WriteAllText(_filePath, builder.ToString(), new UTF8Encoding(false));
    }

    private void Load()
    {
        var needsMigration = false;
        lock (_sync)
        {
            _lists.Clear();
            GetOrCreateListUnsafe(DefaultListName);

            if (!File.Exists(_filePath))
                return;

            var currentListName = DefaultListName;
            string? savedActiveListName = null;
            var sawSection = false;
            foreach (var line in File.ReadLines(_filePath))
            {
                var trimmed = line.Trim();
                if (trimmed.Length == 0)
                    continue;

                if (trimmed.StartsWith("# Active list:", StringComparison.OrdinalIgnoreCase))
                {
                    savedActiveListName = trimmed[14..].Trim();
                    continue;
                }

                if (trimmed.StartsWith('[') && trimmed.EndsWith(']'))
                {
                    var sectionName = trimmed[1..^1].Trim();
                    if (sectionName.Length == 0)
                        continue;
                    sawSection = true;
                    currentListName = NormalizeListName(sectionName);
                    GetOrCreateListUnsafe(currentListName);
                    continue;
                }

                if (trimmed.StartsWith('#'))
                    continue;

                var fields = line.Split('\t', 3);
                if (fields.Length != 3)
                    continue;

                var type = fields[0].Trim();
                if (!type.Equals("standard", StringComparison.OrdinalIgnoreCase) &&
                    !type.Equals("extended", StringComparison.OrdinalIgnoreCase))
                    continue;

                var value = fields[1].Trim();
                if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                    value = value[2..];
                if (!uint.TryParse(value, System.Globalization.NumberStyles.HexNumber, null, out var identifier))
                    continue;

                var isExtended = type.Equals("extended", StringComparison.OrdinalIgnoreCase);
                var definition = new IdentifierDefinition(identifier, isExtended, fields[2].Trim());
                if (definition.Name.Length != 0)
                {
                    if (!sawSection)
                        needsMigration = true;
                    GetOrCreateListUnsafe(currentListName)[definition.Key] = definition;
                }
            }

            if (!string.IsNullOrWhiteSpace(savedActiveListName) && _lists.ContainsKey(savedActiveListName))
                _activeListName = savedActiveListName;
            else
                _activeListName = DefaultListName;
        }

        if (needsMigration)
            Save();
    }

    private Dictionary<string, IdentifierDefinition> GetActiveDefinitionsUnsafe()
        => GetOrCreateListUnsafe(_activeListName);

    private Dictionary<string, IdentifierDefinition> GetOrCreateListUnsafe(string name)
    {
        if (!_lists.TryGetValue(name, out var definitions))
        {
            definitions = new Dictionary<string, IdentifierDefinition>(StringComparer.OrdinalIgnoreCase);
            _lists[name] = definitions;
        }
        return definitions;
    }

    private static string NormalizeListName(string name)
    {
        var cleanName = name.Trim();
        if (cleanName.Length == 0)
            throw new ArgumentException("The identifier list name cannot be empty.", nameof(name));
        if (cleanName.IndexOfAny(['\r', '\n', '\t', '[', ']']) >= 0)
            throw new ArgumentException("The identifier list name cannot contain tabs, line breaks, or square brackets.", nameof(name));
        return cleanName;
    }
}
