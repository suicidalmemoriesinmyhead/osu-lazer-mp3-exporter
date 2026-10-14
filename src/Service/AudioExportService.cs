using System.Collections.Concurrent;
using OsuLazerAudioExporter.Models;
using TagLib;
using File = System.IO.File;

namespace OsuLazerAudioExporter.Services;

public sealed class AudioExportService
{
    private readonly FfmpegService _ffmpegService;
    private readonly SqliteCacheService _cacheService;
    private readonly AppLogger _logger;

    public AudioExportService(
        FfmpegService ffmpegService,
        SqliteCacheService cacheService,
        AppLogger logger)
    {
        _ffmpegService = ffmpegService;
        _cacheService = cacheService;
        _logger = logger;
    }

    public static string BuildDestinationFilePath(
        string baseOutputDir,
        OutputStructure structure,
        BeatmapEntry entry)
    {
        var safeArtist = SanitizeFileName(entry.Artist);
        var safeTitle = SanitizeFileName(entry.Title);
        var baseName = $"{safeArtist} - {safeTitle}";

        return structure switch
        {
            OutputStructure.ArtistTitleFolder =>
                Path.Combine(baseOutputDir, baseName, $"{baseName}.mp3"),

            OutputStructure.BeatmapSetIdFolder =>
                Path.Combine(
                    baseOutputDir,
                    entry.BeatmapSetId > 0 ? entry.BeatmapSetId.ToString() : $"local_{entry.AudioHash[..Math.Min(8, entry.AudioHash.Length)]}",
                    $"{baseName}.mp3"),

            OutputStructure.FlatFolder =>
                Path.Combine(baseOutputDir, $"{baseName}.mp3"),

            _ => Path.Combine(baseOutputDir, $"{baseName}.mp3")
        };
    }

    public static string SanitizeFileName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "Unknown";

        var invalidChars = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Select(ch => invalidChars.Contains(ch) ? '_' : ch).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(cleaned) ? "Unknown" : cleaned.TrimEnd('.');
    }

    /// <summary>
    /// Создаёт папку с текущей датой и временем и экспортирует туда все выбранные MP3.
    /// Возвращает полный путь к созданной папке.
    /// </summary>
    public async Task<string> ExportBatchAsync(
        IReadOnlyList<BeatmapEntry> entries,
        AppConfig config,
        IProgress<ExportProgress>? progress = null,
        CancellationToken ct = default)
    {
        Directory.CreateDirectory(config.OutputDirectory);

        var timestampFolder = $"osu_mp3_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}";
        var sessionOutputDir = Path.Combine(config.OutputDirectory, timestampFolder);
        Directory.CreateDirectory(sessionOutputDir);

        int total = entries.Count;
        int completed = 0;
        int exported = 0;
        int skipped = 0;
        int failed = 0;

        var reservedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pathLock = new object();
        var processedHashesInBatch = new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);

        int maxConcurrency = Math.Clamp(Environment.ProcessorCount, 2, 8);
        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = maxConcurrency,
            CancellationToken = ct
        };

        _logger.Info($"Создана папка экспорта: {sessionOutputDir}");
        _logger.Info($"Запуск экспорта {total} треков в {maxConcurrency} потоков ({config.Mp3BitrateKbps} kbps)...");

        await Parallel.ForEachAsync(entries, parallelOptions, async (entry, token) =>
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(entry.AudioHash) &&
                    !processedHashesInBatch.TryAdd(entry.AudioHash, 0))
                {
                    entry.Status = "Дубликат в списке";
                    Interlocked.Increment(ref skipped);
                    return;
                }

                string targetPath;
                lock (pathLock)
                {
                    var candidatePath = BuildDestinationFilePath(sessionOutputDir, OutputStructure.FlatFolder, entry);
                    if (!reservedPaths.Add(candidatePath))
                    {
                        var fileNoExt = Path.GetFileNameWithoutExtension(candidatePath);
                        var suffix = entry.BeatmapSetId > 0
                            ? entry.BeatmapSetId.ToString()
                            : (entry.AudioHash.Length >= 6 ? entry.AudioHash[..6] : Guid.NewGuid().ToString("N")[..6]);
                        candidatePath = Path.Combine(sessionOutputDir, $"{fileNoExt} [{suffix}].mp3");
                        reservedPaths.Add(candidatePath);
                    }
                    targetPath = candidatePath;
                }

                entry.Status = "⏳ Конвертация...";
                await _ffmpegService.ConvertToMp3Async(
                    entry.AudioPhysicalPath,
                    targetPath,
                    config.Mp3BitrateKbps,
                    token);

                ApplyId3Tags(targetPath, entry);
                _cacheService.MarkAudioExported(entry.AudioHash, targetPath, config.Mp3BitrateKbps);

                entry.Status = "✅ Готово (MP3)";
                Interlocked.Increment(ref exported);
                _logger.Info($"[MP3] {Path.GetFileName(targetPath)}");
            }
            catch (OperationCanceledException)
            {
                entry.Status = "⏹ Отменён";
                throw;
            }
            catch (UnauthorizedAccessException ex)
            {
                entry.Status = "❌ Нет прав доступа";
                Interlocked.Increment(ref failed);
                _logger.Error($"Нет прав доступа при экспорте '{entry.Title}'", ex);
            }
            catch (Exception ex)
            {
                entry.Status = "❌ Ошибка";
                Interlocked.Increment(ref failed);
                _logger.Error($"Ошибка экспорта '{entry.Artist} - {entry.Title}'", ex);
            }
            finally
            {
                var done = Interlocked.Increment(ref completed);
                progress?.Report(new ExportProgress(
                    CompletedCount: done,
                    TotalCount: total,
                    CurrentTrackName: $"{entry.Artist} - {entry.Title}",
                    CurrentFileProgressPercent: 100.0,
                    ExportedCount: exported,
                    SkippedCount: skipped,
                    FailedCount: failed));
            }
        });

        _logger.Info($"Готово! Успешно: {exported}, пропущено: {skipped}, ошибок: {failed}. Папка: {sessionOutputDir}");
        return sessionOutputDir;
    }

    private void ApplyId3Tags(string mp3Path, BeatmapEntry entry)
    {
        try
        {
            using var tagFile = TagLib.File.Create(mp3Path);
            tagFile.Tag.Title = entry.Title;
            tagFile.Tag.Performers = [entry.Artist];
            tagFile.Tag.Album = entry.BeatmapSetId > 0
                ? $"osu!lazer BeatmapSet #{entry.BeatmapSetId}"
                : "osu!lazer Library";
            tagFile.Tag.Comment = $"Mapper: {entry.Creator} | BeatmapID: {entry.BeatmapId}";

            if (!string.IsNullOrWhiteSpace(entry.CoverPhysicalPath) && File.Exists(entry.CoverPhysicalPath))
            {
                var coverBytes = File.ReadAllBytes(entry.CoverPhysicalPath);
                var picture = new Picture(new ByteVector(coverBytes))
                {
                    Type = PictureType.FrontCover,
                    Description = "Beatmap Background",
                    MimeType = DetectImageMimeType(coverBytes)
                };
                tagFile.Tag.Pictures = [picture];
            }

            tagFile.Save();
        }
        catch (Exception ex)
        {
            _logger.Warn($"Не удалось записать ID3-теги для '{mp3Path}': {ex.Message}");
        }
    }

    private static string DetectImageMimeType(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 4 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47)
            return "image/png";
        return "image/jpeg";
    }
}