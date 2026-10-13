using System.Text;

namespace PcanCanMonitor;

public sealed class IdentifierRegistry
{
    public const string DefaultListName = "SRE-7b";

    private readonly object _sync = new();
    private readonly Dictionary<string, Dictionary<string, IdentifierDefinition>> _lists = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Dictionary<string, CanMessageDefinition>> _messageLists = new(StringComparer.OrdinalIgnoreCase);
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
                : GetMessageUnsafe(identifier, isExtended)?.Name;
        }
    }

    public CanMessageDefinition? GetMessage(uint identifier, bool isExtended)
    {
        lock (_sync)
            return GetMessageUnsafe(identifier, isExtended);
    }

    public IReadOnlyList<CanMessageDefinition> GetMessages()
    {
        lock (_sync)
        {
            return _messageLists.TryGetValue(_activeListName, out var messages)
                ? messages.Values
                    .OrderBy(item => item.IsExtended)
                    .ThenBy(item => item.Identifier)
                    .ToArray()
                : [];
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

    public void ReplaceMessages(IEnumerable<CanMessageDefinition> messages)
    {
        lock (_sync)
        {
            var replacement = new Dictionary<string, CanMessageDefinition>(StringComparer.OrdinalIgnoreCase);
            foreach (var message in messages)
                replacement[message.Key] = message;
            _messageLists[_activeListName] = replacement;
        }
    }

    public void Save()
    {
        string activeListName;
        KeyValuePair<string, Dictionary<string, IdentifierDefinition>>[] lists;
        Dictionary<string, Dictionary<string, CanMessageDefinition>> messageLists;
        lock (_sync)
        {
            activeListName = _activeListName;
            lists = _lists.ToArray();
            messageLists = _messageLists.ToDictionary(
                item => item.Key,
                item => new Dictionary<string, CanMessageDefinition>(item.Value, StringComparer.OrdinalIgnoreCase),
                StringComparer.OrdinalIgnoreCase);
        }

        var builder = new StringBuilder();
        builder.AppendLine("# PCAN CAN Monitor identifier names");
        builder.AppendLine("# Identifier: standard|extended<TAB>identifier<TAB>name");
        builder.AppendLine("# Message: message<TAB>standard|extended<TAB>identifier<TAB>name");
        builder.AppendLine("# Signal: signal<TAB>standard|extended<TAB>identifier<TAB>start byte<TAB>length<TAB>unsigned|signed|boolean<TAB>little|big<TAB>scale<TAB>unit<TAB>name");
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
                builder.Append(type).Append('\t')
                    .Append($"0x{definition.Identifier:X}").Append('\t')
                    .AppendLine(EscapeField(definition.Name));
            }

            if (messageLists.TryGetValue(list.Key, out var messages))
            {
                foreach (var message in messages.Values
                             .OrderBy(item => item.IsExtended)
                             .ThenBy(item => item.Identifier))
                {
                    var messageType = message.IsExtended ? "extended" : "standard";
                    builder.Append("message\t").Append(messageType).Append('\t')
                        .Append($"0x{message.Identifier:X}").Append('\t')
                        .AppendLine(EscapeField(message.Name));
                    foreach (var signal in message.Signals)
                    {
                        builder.Append("signal\t").Append(messageType).Append('\t')
                            .Append($"0x{message.Identifier:X}").Append('\t')
                            .Append(signal.StartByte).Append('\t')
                            .Append(signal.Length).Append('\t')
                            .Append(signal.ValueKind.ToString().ToLowerInvariant()).Append('\t')
                            .Append(signal.ByteOrder == CanSignalByteOrder.BigEndian ? "big" : "little").Append('\t')
                            .Append(signal.Scale.ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append('\t')
                            .Append(EscapeField(signal.Unit)).Append('\t')
                            .AppendLine(EscapeField(signal.Name));
                    }
                }
            }
        }
        File.WriteAllText(_filePath, builder.ToString(), new UTF8Encoding(false));
    }

    private void Load()
    {
        var needsSave = false;
        lock (_sync)
        {
            _lists.Clear();
            _messageLists.Clear();
            GetOrCreateListUnsafe(DefaultListName);

            var currentListName = DefaultListName;
            string? savedActiveListName = null;
            var sawSection = false;
            var fileExists = File.Exists(_filePath);
            var hasMessageSchema = false;
            if (fileExists)
            {
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

                    if (trimmed.StartsWith("# Message:", StringComparison.OrdinalIgnoreCase))
                    {
                        hasMessageSchema = true;
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

                    var fields = line.Split('\t', StringSplitOptions.None);
                    if (fields.Length == 0)
                        continue;

                    var type = fields[0].Trim();
                    if (type.Equals("message", StringComparison.OrdinalIgnoreCase))
                    {
                        if (TryParseMessage(fields, out var message))
                            AddMessageUnsafe(currentListName, message);
                        continue;
                    }

                    if (type.Equals("signal", StringComparison.OrdinalIgnoreCase))
                    {
                        if (TryParseSignal(fields, out var signalMessageKey, out var signal))
                            AddSignalUnsafe(currentListName, signalMessageKey, signal);
                        continue;
                    }

                    if (fields.Length != 3 ||
                        (!type.Equals("standard", StringComparison.OrdinalIgnoreCase) &&
                         !type.Equals("extended", StringComparison.OrdinalIgnoreCase)))
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
                            needsSave = true;
                        GetOrCreateListUnsafe(currentListName)[definition.Key] = definition;
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(savedActiveListName) && _lists.ContainsKey(savedActiveListName))
                _activeListName = savedActiveListName;
            else
                _activeListName = DefaultListName;

            if ((!fileExists || !hasMessageSchema) &&
                (!_messageLists.TryGetValue(DefaultListName, out var defaults) || defaults.Count == 0))
            {
                foreach (var message in CreateDefaultSre7bMessages())
                    AddMessageUnsafe(DefaultListName, message);
                needsSave = true;
            }
        }

        if (needsSave)
            Save();
    }

    private Dictionary<string, IdentifierDefinition> GetActiveDefinitionsUnsafe()
        => GetOrCreateListUnsafe(_activeListName);

    private CanMessageDefinition? GetMessageUnsafe(uint identifier, bool isExtended)
        => _messageLists.TryGetValue(_activeListName, out var messages) &&
           messages.TryGetValue(MakeKey(identifier, isExtended), out var message)
            ? message
            : null;

    private Dictionary<string, IdentifierDefinition> GetOrCreateListUnsafe(string name)
    {
        if (!_lists.TryGetValue(name, out var definitions))
        {
            definitions = new Dictionary<string, IdentifierDefinition>(StringComparer.OrdinalIgnoreCase);
            _lists[name] = definitions;
        }
        return definitions;
    }

    private Dictionary<string, CanMessageDefinition> GetOrCreateMessageListUnsafe(string name)
    {
        if (!_messageLists.TryGetValue(name, out var messages))
        {
            messages = new Dictionary<string, CanMessageDefinition>(StringComparer.OrdinalIgnoreCase);
            _messageLists[name] = messages;
        }
        return messages;
    }

    private void AddMessageUnsafe(string listName, CanMessageDefinition message)
    {
        GetOrCreateMessageListUnsafe(listName)[message.Key] = message;
    }

    private void AddSignalUnsafe(string listName, string messageKey, CanSignalDefinition signal)
    {
        var messages = GetOrCreateMessageListUnsafe(listName);
        if (!messages.TryGetValue(messageKey, out var message))
            return;
        messages[messageKey] = message with { Signals = message.Signals.Append(signal).ToArray() };
    }

    private static bool TryParseMessage(string[] fields, out CanMessageDefinition message)
    {
        message = null!;
        if (fields.Length < 4 || !TryParseFrameAddress(fields[1], fields[2], out var identifier, out var isExtended))
            return false;
        var name = fields[3].Trim();
        if (name.Length == 0)
            return false;
        message = new CanMessageDefinition(identifier, isExtended, name, []);
        return true;
    }

    private static bool TryParseSignal(string[] fields, out string messageKey, out CanSignalDefinition signal)
    {
        messageKey = string.Empty;
        signal = null!;
        if (fields.Length < 10 || !TryParseFrameAddress(fields[1], fields[2], out var identifier, out var isExtended))
            return false;
        if (!int.TryParse(fields[3], out var startByte) || !int.TryParse(fields[4], out var length) ||
            startByte < 0 || length is < 1 or > 8 || startByte + length > 8)
            return false;
        if (!Enum.TryParse<CanSignalValueKind>(fields[5], true, out var valueKind) ||
            !Enum.TryParse<CanSignalByteOrder>(fields[6] + "Endian", true, out var byteOrder) ||
            !double.TryParse(fields[7], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var scale))
            return false;
        var name = fields[9].Trim();
        if (name.Length == 0)
            return false;
        var unit = fields[8].Trim();
        signal = new CanSignalDefinition(startByte, length, name, valueKind, byteOrder, scale, unit);
        messageKey = MakeKey(identifier, isExtended);
        return true;
    }

    private static bool TryParseFrameAddress(string type, string value, out uint identifier, out bool isExtended)
    {
        identifier = 0;
        isExtended = false;
        if (!type.Trim().Equals("standard", StringComparison.OrdinalIgnoreCase) &&
            !type.Trim().Equals("extended", StringComparison.OrdinalIgnoreCase))
            return false;
        var cleanValue = value.Trim();
        if (cleanValue.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            cleanValue = cleanValue[2..];
        if (!uint.TryParse(cleanValue, System.Globalization.NumberStyles.HexNumber, null, out identifier))
            return false;
        isExtended = type.Trim().Equals("extended", StringComparison.OrdinalIgnoreCase);
        return true;
    }

    private static string EscapeField(string value)
        => value.Replace("\t", " ").Replace("\r", " ").Replace("\n", " ");

    private static IReadOnlyList<CanMessageDefinition> CreateDefaultSre7bMessages()
        =>
        [
            new(0x500, false, "TPS 0", [
                new(0, 1, "Throttle percent", Scale: 100d / 255, Unit: "%"),
                new(1, 1, "TPS0 percent", Scale: 100d / 255, Unit: "%"),
                new(2, 2, "TPS0 sensor value"),
                new(4, 2, "TPS0 calibration minimum"),
                new(6, 2, "TPS0 calibration maximum")]),
            new(0x501, false, "TPS 1", [
                new(0, 1, "Throttle percent", Scale: 100d / 255, Unit: "%"),
                new(1, 1, "TPS1 percent", Scale: 100d / 255, Unit: "%"),
                new(2, 2, "TPS1 sensor value"),
                new(4, 2, "TPS1 calibration minimum"),
                new(6, 2, "TPS1 calibration maximum")]),
            new(0x502, false, "BPS 0", [
                new(0, 1, "Brake percent", Scale: 100d / 255, Unit: "%"),
                new(1, 1, "Eco button sensor value"),
                new(2, 2, "BPS0 sensor value"),
                new(4, 2, "BPS0 calibration minimum"),
                new(6, 2, "BPS0 calibration maximum")]),
            new(0x503, false, "SDiff", [
                new(0, 1, "SDiff toggle", CanSignalValueKind.Boolean),
                new(1, 2, "SDiff shared multiplier", Scale: 0.001, Unit: "x"),
                new(3, 2, "SDiff left multiplier", Scale: 0.001, Unit: "x"),
                new(5, 2, "SDiff right multiplier", Scale: 0.001, Unit: "x")]),
            new(0x504, false, "SAS", [
                new(0, 2, "SAS sensor value"),
                new(2, 2, "Steering angle", CanSignalValueKind.Signed, Unit: "deg")]),
            new(0x505, false, "Safety Checker", [
                new(0, 4, "Safety faults"),
                new(4, 2, "Safety warnings"),
                new(6, 2, "Safety notices")]),
            new(0x506, false, "Reserved / unused", []),
            new(0x507, false, "MCM RTD Status", [
                new(0, 2, "HVIL termination sense")]),
            new(0x508, false, "BMS Loopback Test", [
                new(0, 1, "BMS fault flags"),
                new(1, 1, "BMS warning flags"),
                new(2, 1, "BMS relay state"),
                new(3, 2, "Highest cell temperature", Scale: 0.1, Unit: "°C"),
                new(5, 2, "BMS pack current", CanSignalValueKind.Signed, Scale: 100, Unit: "mA")]),
            new(0x509, false, "Powertrain power summary", [
                new(0, 2, "Average voltage", CanSignalValueKind.Signed, Scale: 0.1, Unit: "V"),
                new(2, 2, "Total DC current", CanSignalValueKind.Signed, Scale: 0.1, Unit: "A"),
                new(4, 2, "Power", CanSignalValueKind.Signed, Scale: 10, Unit: "W"),
                new(6, 2, "Driver request", CanSignalValueKind.Signed, Scale: 100, Unit: "mA")]),
            new(0x100, true, "RL motor command current", [
                new(0, 4, "RL command current", CanSignalValueKind.Signed, CanSignalByteOrder.BigEndian, Unit: "mA")]),
            new(0x101, true, "RR motor command current", [
                new(0, 4, "RR command current", CanSignalValueKind.Signed, CanSignalByteOrder.BigEndian, Unit: "mA")]),
            new(0x102, true, "FL motor command current", [
                new(0, 4, "FL command current", CanSignalValueKind.Signed, CanSignalByteOrder.BigEndian, Unit: "mA")]),
            new(0x103, true, "FR motor command current", [
                new(0, 4, "FR command current", CanSignalValueKind.Signed, CanSignalByteOrder.BigEndian, Unit: "mA")]),
            new(0x605, false, "BMS precharge request", [
                new(0, 1, "Precharge request", CanSignalValueKind.Boolean)])
        ];

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
