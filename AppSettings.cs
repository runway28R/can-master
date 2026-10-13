using System.Text;
using System.Text.Json;

namespace PcanCanMonitor;

public sealed class AppSettings
{
    public const int DefaultMaxRetainedMessages = 5000;
    public const int MinMaxRetainedMessages = 1;
    public const int MaxMaxRetainedMessages = 1_000_000;

    private readonly string _filePath;

    private AppSettings(string filePath, int maxRetainedMessages)
    {
        _filePath = filePath;
        MaxRetainedMessages = maxRetainedMessages;
    }

    public string FilePath => _filePath;

    public int MaxRetainedMessages { get; private set; }

    public static AppSettings Load()
    {
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PcanCanMonitor");
        Directory.CreateDirectory(directory);
        var filePath = Path.Combine(directory, "settings.json");
        var value = DefaultMaxRetainedMessages;

        try
        {
            if (File.Exists(filePath))
            {
                var document = JsonSerializer.Deserialize<SettingsDocument>(File.ReadAllText(filePath));
                if (document?.MaxRetainedMessages is >= MinMaxRetainedMessages and <= MaxMaxRetainedMessages)
                    value = document.MaxRetainedMessages;
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            AppLog.Error($"Could not load settings from {filePath}; using defaults.", exception);
        }

        return new AppSettings(filePath, value);
    }

    public void SetMaxRetainedMessages(int value)
    {
        if (value is < MinMaxRetainedMessages or > MaxMaxRetainedMessages)
            throw new ArgumentOutOfRangeException(nameof(value));
        MaxRetainedMessages = value;
    }

    public void Save()
    {
        var document = new SettingsDocument { MaxRetainedMessages = MaxRetainedMessages };
        var json = JsonSerializer.Serialize(document, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_filePath, json + Environment.NewLine, new UTF8Encoding(false));
    }

    private sealed class SettingsDocument
    {
        public int MaxRetainedMessages { get; set; } = DefaultMaxRetainedMessages;
    }
}
