namespace OsuLazerAudioExporter.Models;

public sealed record ExportProgress(
    int CompletedCount,
    int TotalCount,
    string CurrentTrackName,
    double CurrentFileProgressPercent,
    int ExportedCount,
    int SkippedCount,
    int FailedCount);