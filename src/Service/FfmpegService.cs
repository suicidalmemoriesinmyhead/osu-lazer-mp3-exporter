using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;

namespace OsuLazerAudioExporter.Services;

public sealed class FfmpegService
{
    private readonly AppLogger _logger;
    private string? _resolvedFfmpegPath;

    public FfmpegService(AppLogger logger)
    {
        _logger = logger;
    }

    public string? ResolveFfmpegExecutable(string? customPath = null)
    {
        if (!string.IsNullOrWhiteSpace(customPath) && File.Exists(customPath))
            return _resolvedFfmpegPath = customPath;

        var exeName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "ffmpeg.exe" : "ffmpeg";

        // 1. Проверяем локальную папку tools/ рядом с приложением
        var localToolsPath = Path.Combine(AppContext.BaseDirectory, "tools", exeName);
        if (File.Exists(localToolsPath))
            return _resolvedFfmpegPath = localToolsPath;

        // 2. Проверяем системный PATH
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var dir in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(dir, exeName);
            if (File.Exists(candidate))
                return _resolvedFfmpegPath = candidate;
        }

        return null;
    }

    /// <summary>
    /// Автоматическое скачивание статической сборки FFmpeg в папку tools/, если он не установлен в системе.
    /// </summary>
    public async Task<string?> DownloadFfmpegAsync(IProgress<double>? downloadProgress = null, CancellationToken ct = default)
    {
        var toolsDir = Path.Combine(AppContext.BaseDirectory, "tools");
        Directory.CreateDirectory(toolsDir);

        string downloadUrl;
        bool isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        if (isWindows)
        {
            downloadUrl = "https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip";
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            downloadUrl = "https://evermeet.cx/ffmpeg/getrelease/zip";
        }
        else
        {
            _logger.Warn("Для Linux рекомендуется установить ffmpeg через пакетный менеджер (sudo apt install ffmpeg / pacman -S ffmpeg).");
            return null;
        }

        var archivePath = Path.Combine(toolsDir, "ffmpeg_download.zip");
        _logger.Info($"Скачивание FFmpeg из {downloadUrl}...");

        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
            using var response = await http.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? -1L;
            await using (var contentStream = await response.Content.ReadAsStreamAsync(ct))
            await using (var fileStream = new FileStream(archivePath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
            {
                var buffer = new byte[81920];
                long totalRead = 0;
                int read;
                while ((read = await contentStream.ReadAsync(buffer, ct)) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, read), ct);
                    totalRead += read;
                    if (totalBytes > 0)
                    {
                        downloadProgress?.Report((double)totalRead / totalBytes * 100.0);
                    }
                }
            }

            var exeName = isWindows ? "ffmpeg.exe" : "ffmpeg";
            var targetExePath = Path.Combine(toolsDir, exeName);

            using (var zip = ZipFile.OpenRead(archivePath))
            {
                var entry = zip.Entries.FirstOrDefault(e => e.Name.Equals(exeName, StringComparison.OrdinalIgnoreCase));
                if (entry is null)
                    throw new FileNotFoundException("В скачанном архиве не найден исполняемый файл ffmpeg.");

                entry.ExtractToFile(targetExePath, overwrite: true);
            }

            File.Delete(archivePath);

            if (!isWindows)
            {
                Process.Start("chmod", $"+x \"{targetExePath}\"")?.WaitForExit();
            }

            _logger.Info($"FFmpeg успешно установлен: {targetExePath}");
            return _resolvedFfmpegPath = targetExePath;
        }
        catch (Exception ex)
        {
            _logger.Error("Не удалось автоматически скачать FFmpeg", ex);
            return null;
        }
    }

    public async Task ConvertToMp3Async(
        string inputPath,
        string outputPath,
        int bitrateKbps,
        CancellationToken ct = default)
    {
        var ffmpegExe = _resolvedFfmpegPath ?? ResolveFfmpegExecutable();
        if (string.IsNullOrEmpty(ffmpegExe))
            throw new InvalidOperationException("FFmpeg не найден. Укажите путь к FFmpeg или используйте автозагрузку.");

        var psi = new ProcessStartInfo
        {
            FileName = ffmpegExe,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };

        psi.ArgumentList.Add("-y");
        psi.ArgumentList.Add("-i");
        psi.ArgumentList.Add(inputPath);
        psi.ArgumentList.Add("-vn");
        psi.ArgumentList.Add("-acodec");
        psi.ArgumentList.Add("libmp3lame");
        psi.ArgumentList.Add("-b:a");
        psi.ArgumentList.Add($"{bitrateKbps}k");
        psi.ArgumentList.Add(outputPath);

        using var process = new Process { StartInfo = psi };
        process.Start();

        var stdErrTask = process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);

        if (process.ExitCode != 0)
        {
            var err = await stdErrTask;
            throw new InvalidOperationException($"FFmpeg завершился с кодом {process.ExitCode}: {err}");
        }
    }
}