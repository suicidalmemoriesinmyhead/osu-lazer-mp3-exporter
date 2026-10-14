using CommunityToolkit.Mvvm.ComponentModel;

namespace OsuLazerAudioExporter.Models;

public partial class BeatmapEntry : ObservableObject
{
    [ObservableProperty]
    private bool _isSelected = true;

    [ObservableProperty]
    private string _status = "Готов";

    public int BeatmapId { get; init; }
    public int BeatmapSetId { get; init; }
    public string Title { get; init; } = "Unknown Title";
    public string Artist { get; init; } = "Unknown Artist";
    public string Creator { get; init; } = "Unknown Creator";
    public string Version { get; init; } = "Normal";
    public string AudioFilename { get; init; } = string.Empty;
    public string? BackgroundFilename { get; init; }

    /// <summary>
    /// SHA-256 хэш аудиофайла в хранилище osu!lazer (files/x/xy/xyz...)
    /// </summary>
    public string AudioHash { get; init; } = string.Empty;

    /// <summary>
    /// Полный путь к физическому аудиофайлу в папке files/
    /// </summary>
    public string AudioPhysicalPath { get; init; } = string.Empty;

    /// <summary>
    /// Полный путь к физическому файлу обложки в папке files/ (если найден)
    /// </summary>
    public string? CoverPhysicalPath { get; init; }

    public string Format { get; init; } = "AUDIO";
    public TimeSpan Duration { get; set; } = TimeSpan.Zero;

    public string DurationFormatted =>
        Duration == TimeSpan.Zero
            ? "--:--"
            : $"{(int)Duration.TotalMinutes:D2}:{Duration.Seconds:D2}";
}