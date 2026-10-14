using System.Runtime.InteropServices;
using System.Text.Json;
using OsuLazerAudioExporter.Models;

namespace OsuLazerAudioExporter.Services;

public sealed class ConfigService
{
    private readonly string _configPath;
    private readonly AppLogger _logger;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public ConfigService(AppLogger logger)
    {
        _logger = logger;
        _configPath = Path.Combine(AppContext.BaseDirectory, "config.json");
    }

    public AppConfig Load()
    {
        try
        {
            if (File.Exists(_configPath))
            {
                var json = File.ReadAllText(_configPath);
                var config = JsonSerializer.Deserialize<AppConfig>(json, JsonOptions);
                if (config is not null)
                {
                    if (string.IsNullOrWhiteSpace(config.LazerLibraryPath))
                    {
                        config.LazerLibraryPath = DetectDefaultLazerPath();
                    }
                    return config;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Error("Не удалось загрузить config.json, используются настройки по умолчанию", ex);
        }

        var defaultConfig = new AppConfig
        {
            LazerLibraryPath = DetectDefaultLazerPath()
        };
        Save(defaultConfig);
        return defaultConfig;
    }

    public void Save(AppConfig config)
    {
        try
        {
            var json = JsonSerializer.Serialize(config, JsonOptions);
            File.WriteAllText(_configPath, json);
        }
        catch (Exception ex)
        {
            _logger.Error("Ошибка сохранения config.json", ex);
        }
    }

    public static string DetectDefaultLazerPath()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appData, "osu");
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, "Library", "Application Support", "osu");
        }

        // Linux: ~/.local/share/osu
        var linuxHome = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(linuxHome, ".local", "share", "osu");
    }

    public static bool IsValidLazerLibrary(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            return false;

        var filesDir = Path.Combine(path, "files");
        return Directory.Exists(filesDir);
    }
}