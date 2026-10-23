using System.Text.Json;

namespace TokenMojo.Core;

public sealed class SettingsException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>User settings in %APPDATA%\TokenMojo\settings.json. A file that cannot be read is an error, never silently replaced by defaults.</summary>
public sealed record Settings(int LongTurnSeconds = 30)
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };

    public TimeSpan LongTurn => TimeSpan.FromSeconds(LongTurnSeconds);

    public static Settings Load(string path)
    {
        if (!File.Exists(path))
        {
            Settings defaults = new();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(defaults, Options));
            return defaults;
        }
        try
        {
            Settings settings = JsonSerializer.Deserialize<Settings>(File.ReadAllText(path), Options)
                ?? throw new JsonException("The file is JSON null");
            if (settings.LongTurnSeconds < 0) throw new JsonException("longTurnSeconds must not be negative");
            return settings;
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            throw new SettingsException($"Cannot read settings file {path}: {e.Message}", e);
        }
    }
}
