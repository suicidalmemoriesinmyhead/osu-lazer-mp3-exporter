using OsuLazerAudioExporter.Models;
using Realms;
using TagLib;
using File = System.IO.File;

namespace OsuLazerAudioExporter.Services;

public sealed class LazerLibraryScanner
{
    private readonly AppLogger _logger;
    private readonly SqliteCacheService _cacheService;

    public LazerLibraryScanner(AppLogger logger, SqliteCacheService cacheService)
    {
        _logger = logger;
        _cacheService = cacheService;
    }

    public static string GetHashedFilePath(string filesRoot, string sha256Hash)
    {
        if (string.IsNullOrWhiteSpace(sha256Hash) || sha256Hash.Length < 3)
            return string.Empty;

        var lower = sha256Hash.ToLowerInvariant();
        return Path.Combine(filesRoot, lower[..1], lower[..2], lower);
    }

    /// <summary>
    /// Ð¡ÐºÐ°Ð½Ð¸Ñ€ÑƒÐµÑ‚ Ð±Ð¸Ð±Ð»Ð¸Ð¾Ñ‚ÐµÐºÑƒ osu!lazer:
    /// 1) ÐŸÑ‹Ñ‚Ð°ÐµÑ‚ÑÑ Ð¿Ñ€Ð¾Ñ‡Ð¸Ñ‚Ð°Ñ‚ÑŒ ÑÐ²ÑÐ·Ð¸ BeatmapSet â†” Files Ð¸Ð· ÑÐ½Ð¸Ð¼ÐºÐ° client.realm Ð¸ Ð¿Ð°Ñ€ÑÐ¸Ñ‚ .osu Ñ„Ð°Ð¹Ð»Ñ‹ Ð² files/.
    /// 2) ÐŸÑ€Ð¸ Ð½ÐµÐ´Ð¾ÑÑ‚ÑƒÐ¿Ð½Ð¾ÑÑ‚Ð¸ Ð¸Ð»Ð¸ ÑÐ¼ÐµÐ½Ðµ ÑÑ…ÐµÐ¼Ñ‹ client.realm Ð¸ÑÐ¿Ð¾Ð»ÑŒÐ·ÑƒÐµÑ‚ Ð¿Ñ€ÑÐ¼Ð¾Ðµ ÑÐºÐ°Ð½Ð¸Ñ€Ð¾Ð²Ð°Ð½Ð¸Ðµ Ñ…Ñ€Ð°Ð½Ð¸Ð»Ð¸Ñ‰Ð° files/.
    /// Ð”ÐµÐ´ÑƒÐ¿Ð»Ð¸Ñ†Ð¸Ñ€ÑƒÐµÑ‚ Ñ‚Ñ€ÐµÐºÐ¸ Ð¿Ð¾ SHA-256 Ñ…ÑÑˆÑƒ Ð°ÑƒÐ´Ð¸Ð¾Ñ„Ð°Ð¹Ð»Ð°.
    /// </summary>
    public async Task<List<BeatmapEntry>> ScanLibraryAsync(
        string lazerRootPath,
        CancellationToken ct = default)
    {
        if (!ConfigService.IsValidLazerLibrary(lazerRootPath))
            throw new DirectoryNotFoundException($"ÐŸÐ°Ð¿ÐºÐ° Ð±Ð¸Ð±Ð»Ð¸Ð¾Ñ‚ÐµÐºÐ¸ osu!lazer Ð½Ðµ Ð½Ð°Ð¹Ð´ÐµÐ½Ð° Ð¸Ð»Ð¸ Ð½Ðµ ÑÐ¾Ð´ÐµÑ€Ð¶Ð¸Ñ‚ ÐºÐ°Ñ‚Ð°Ð»Ð¾Ð³ 'files': {lazerRootPath}");

        var filesRoot = Path.Combine(lazerRootPath, "files");
        var realmPath = Path.Combine(lazerRootPath, "client.realm");

        _logger.Info($"ÐÐ°Ñ‡Ð°Ñ‚Ð¾ ÑÐºÐ°Ð½Ð¸Ñ€Ð¾Ð²Ð°Ð½Ð¸Ðµ Ð±Ð¸Ð±Ð»Ð¸Ð¾Ñ‚ÐµÐºÐ¸: {lazerRootPath}");

        return await Task.Run(() =>
        {
            var cachedByOsuHash = _cacheService.LoadCachedBeatmaps(filesRoot);
            var uniqueAudioEntries = new Dictionary<string, BeatmapEntry>(StringComparer.OrdinalIgnoreCase);

            // Ð¨Ð°Ð³ 1: ÐŸÐ¾Ð¿Ñ‹Ñ‚ÐºÐ° Ñ‡Ñ‚ÐµÐ½Ð¸Ñ ÑÐ²ÑÐ·ÐµÐ¹ Ð¸Ð· client.realm (Ñ‡ÐµÑ€ÐµÐ· Ð²Ñ€ÐµÐ¼ÐµÐ½Ð½ÑƒÑŽ ÐºÐ¾Ð¿Ð¸ÑŽ, Ñ‡Ñ‚Ð¾Ð±Ñ‹ Ð½Ðµ ÐºÐ¾Ð½Ñ„Ð»Ð¸ÐºÑ‚Ð¾Ð²Ð°Ñ‚ÑŒ Ñ Ð·Ð°Ð¿ÑƒÑ‰ÐµÐ½Ð½Ñ‹Ð¼ lazer)
            bool realmLoaded = TryScanViaRealmSnapshot(realmPath, filesRoot, cachedByOsuHash, uniqueAudioEntries, ct);

            // Ð¨Ð°Ð³ 2: Ð•ÑÐ»Ð¸ client.realm Ð¾Ñ‚ÑÑƒÑ‚ÑÑ‚Ð²ÑƒÐµÑ‚ Ð¸Ð»Ð¸ Ð¸Ð·Ð¼ÐµÐ½Ð¸Ð» Ð²Ð½ÑƒÑ‚Ñ€ÐµÐ½Ð½ÑŽÑŽ ÑÑ…ÐµÐ¼Ñƒ â€” Ð·Ð°Ð¿ÑƒÑÐºÐ°ÐµÐ¼ Ð¿Ñ€ÑÐ¼Ð¾Ð¹ Ð°Ð½Ð°Ð»Ð¸Ð·Ð°Ñ‚Ð¾Ñ€ Ð¿Ð°Ð¿ÐºÐ¸ files/
            if (!realmLoaded || uniqueAudioEntries.Count == 0)
            {
                _logger.Info("Ð˜ÑÐ¿Ð¾Ð»ÑŒÐ·ÑƒÐµÑ‚ÑÑ Ð¿Ñ€ÑÐ¼Ð¾Ðµ Ð¸Ð½Ð´ÐµÐºÑÐ¸Ñ€Ð¾Ð²Ð°Ð½Ð¸Ðµ Ñ…Ñ€Ð°Ð½Ð¸Ð»Ð¸Ñ‰Ð° files/ Ð¸ Ð·Ð°Ð³Ð¾Ð»Ð¾Ð²ÐºÐ¾Ð² .osu...");
                ScanFilesDirectoryDirectly(filesRoot, cachedByOsuHash, uniqueAudioEntries, ct);
            }

            var list = uniqueAudioEntries.Values
                .OrderBy(x => x.Artist, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.Title, StringComparer.OrdinalIgnoreCase)
                .ToList();

            _logger.Info($"Ð¡ÐºÐ°Ð½Ð¸Ñ€Ð¾Ð²Ð°Ð½Ð¸Ðµ Ð·Ð°Ð²ÐµÑ€ÑˆÐµÐ½Ð¾. ÐÐ°Ð¹Ð´ÐµÐ½Ð¾ ÑƒÐ½Ð¸ÐºÐ°Ð»ÑŒÐ½Ñ‹Ñ… Ð°ÑƒÐ´Ð¸Ð¾Ñ‚Ñ€ÐµÐºÐ¾Ð²: {list.Count}");
            return list;
        }, ct);
    }

    private bool TryScanViaRealmSnapshot(
        string realmPath,
        string filesRoot,
        Dictionary<string, BeatmapEntry> cachedByOsuHash,
        Dictionary<string, BeatmapEntry> uniqueAudioEntries,
        CancellationToken ct)
    {
        if (!File.Exists(realmPath))
            return false;

        var tempRealmPath = Path.Combine(Path.GetTempPath(), $"osu_lazer_snapshot_{Guid.NewGuid():N}.realm");
        try
        {
            File.Copy(realmPath, tempRealmPath, overwrite: true);

            var config = new RealmConfiguration(tempRealmPath)
            {
                IsDynamic = true,
                IsReadOnly = true
            };

            using var realm = Realm.GetInstance(config);
            var beatmapSets = realm.DynamicApi.All("BeatmapSet");

            foreach (dynamic set in beatmapSets)
            {
                ct.ThrowIfCancellationRequested();

                // Ð¡Ð¾Ð±Ð¸Ñ€Ð°ÐµÐ¼ ÐºÐ°Ñ€Ñ‚Ñƒ Ð²Ð¸Ñ€Ñ‚ÑƒÐ°Ð»ÑŒÐ½Ñ‹Ñ… Ð¸Ð¼Ñ‘Ð½ Ñ„Ð°Ð¹Ð»Ð¾Ð² Ð²Ð½ÑƒÑ‚Ñ€Ð¸ ÑÐµÑ‚Ð° -> SHA-256 Ñ…ÑÑˆ Ð² Ð¿Ð°Ð¿ÐºÐµ files/
                var fileMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (dynamic namedFile in set.Files)
                {
                    string? filename = namedFile.Filename;
                    string? hash = namedFile.File?.Hash;
                    if (!string.IsNullOrWhiteSpace(filename) && !string.IsNullOrWhiteSpace(hash))
                    {
                        fileMap[filename] = hash;
                    }
                }

                // ÐÐ°Ñ…Ð¾Ð´Ð¸Ð¼ Ð¿ÐµÑ€Ð²Ñ‹Ð¹ Ð²Ð°Ð»Ð¸Ð´Ð½Ñ‹Ð¹ .osu Ñ„Ð°Ð¹Ð» Ð² ÑÐµÑ‚Ðµ
                var osuPair = fileMap.FirstOrDefault(kv => kv.Key.EndsWith(".osu", StringComparison.OrdinalIgnoreCase));
                if (string.IsNullOrEmpty(osuPair.Value))
                    continue;

                var osuHash = osuPair.Value;
                if (cachedByOsuHash.TryGetValue(osuHash, out var cachedEntry))
                {
                    uniqueAudioEntries.TryAdd(cachedEntry.AudioHash, cachedEntry);
                    continue;
                }

                var osuPhysicalPath = GetHashedFilePath(filesRoot, osuHash);
                if (!File.Exists(osuPhysicalPath))
                    continue;

                ParsedOsuMetadata? parsed;
                try
                {
                    parsed = OsuBeatmapParser.ParseFile(osuPhysicalPath);
                }
                catch (Exception ex)
                {
                    _logger.Warn($"ÐŸÑ€Ð¾Ð¿ÑƒÑ‰ÐµÐ½ Ð¿Ð¾Ð²Ñ€ÐµÐ¶Ð´Ñ‘Ð½Ð½Ñ‹Ð¹ .osu Ñ„Ð°Ð¹Ð» ({osuHash}): {ex.Message}");
                    continue;
                }

                if (parsed is null || !fileMap.TryGetValue(parsed.AudioFilename, out var audioHash))
                    continue;

                if (uniqueAudioEntries.ContainsKey(audioHash))
                    continue;

                var audioPhysicalPath = GetHashedFilePath(filesRoot, audioHash);
                if (!File.Exists(audioPhysicalPath))
                    continue;

                string? coverHash = null;
                string? coverPhysicalPath = null;
                if (!string.IsNullOrWhiteSpace(parsed.BackgroundFilename) &&
                    fileMap.TryGetValue(parsed.BackgroundFilename, out coverHash))
                {
                    var candidateCover = GetHashedFilePath(filesRoot, coverHash);
                    if (File.Exists(candidateCover))
                        coverPhysicalPath = candidateCover;
                }

                var (format, duration) = ProbeAudioFileInfo(audioPhysicalPath, parsed.AudioFilename);

                var entry = new BeatmapEntry
                {
                    BeatmapId = parsed.BeatmapId,
                    BeatmapSetId = parsed.BeatmapSetId,
                    Title = parsed.Title,
                    Artist = parsed.Artist,
                    Creator = parsed.Creator,
                    Version = parsed.Version,
                    AudioFilename = parsed.AudioFilename,
                    BackgroundFilename = parsed.BackgroundFilename,
                    AudioHash = audioHash,
                    AudioPhysicalPath = audioPhysicalPath,
                    CoverPhysicalPath = coverPhysicalPath,
                    Format = format,
                    Duration = duration
                };

                uniqueAudioEntries[audioHash] = entry;
                _cacheService.SaveBeatmapToCache(osuHash, entry, coverHash);
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.Warn($"Ð§Ñ‚ÐµÐ½Ð¸Ðµ client.realm Ñ‡ÐµÑ€ÐµÐ· Dynamic API Ð½Ðµ ÑƒÐ´Ð°Ð»Ð¾ÑÑŒ ({ex.Message}), Ð¿ÐµÑ€ÐµÐºÐ»ÑŽÑ‡Ð°ÐµÐ¼ÑÑ Ð½Ð° Ð¿Ñ€ÑÐ¼Ð¾Ðµ ÑÐºÐ°Ð½Ð¸Ñ€Ð¾Ð²Ð°Ð½Ð¸Ðµ files/.");
            return false;
        }
        finally
        {
            try
            {
                if (File.Exists(tempRealmPath))
                    File.Delete(tempRealmPath);
            }
            catch
            {
                // Ignore temp cleanup error
            }
        }
    }

    private void ScanFilesDirectoryDirectly(
        string filesRoot,
        Dictionary<string, BeatmapEntry> cachedByOsuHash,
        Dictionary<string, BeatmapEntry> uniqueAudioEntries,
        CancellationToken ct)
    {
        Span<byte> headerBuffer = stackalloc byte[64];

        var allFiles = Directory.EnumerateFiles(filesRoot, "*", SearchOption.AllDirectories);
        var discoveredAudioFiles = new List<(string Hash, string Path, string Format)>();
        var discoveredOsuMetadata = new List<(string OsuHash, ParsedOsuMetadata Meta)>();

        foreach (var filePath in allFiles)
        {
            ct.ThrowIfCancellationRequested();
            var hash = Path.GetFileName(filePath);

            if (cachedByOsuHash.TryGetValue(hash, out var cached))
            {
                uniqueAudioEntries.TryAdd(cached.AudioHash, cached);
                continue;
            }

            try
            {
                using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                if (fs.Length < 16)
                    continue;

                var bytesRead = fs.Read(headerBuffer);
                var slice = headerBuffer[..bytesRead];

                if (OsuBeatmapParser.IsOsuFileHeader(slice))
                {
                    fs.Position = 0;
                    var meta = OsuBeatmapParser.ParseStream(fs);
                    if (meta is not null)
                    {
                        discoveredOsuMetadata.Add((hash, meta));
                    }
                }
                else
                {
                    var audioFormat = OsuBeatmapParser.DetectAudioFormat(slice);
                    if (audioFormat is not null && fs.Length > 64 * 1024) // ÐŸÑ€Ð¾Ð¿ÑƒÑÐºÐ°ÐµÐ¼ ÐºÐ¾Ñ€Ð¾Ñ‚ÐºÐ¸Ðµ Ð·Ð²ÑƒÐºÐ¸ Ð¿Ð¾Ð¿Ð°Ð´Ð°Ð½Ð¸Ð¹ (hitsounds < 64 KB)
                    {
                        discoveredAudioFiles.Add((hash, filePath, audioFormat));
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Warn($"ÐžÑˆÐ¸Ð±ÐºÐ° Ñ‡Ñ‚ÐµÐ½Ð¸Ñ Ñ„Ð°Ð¹Ð»Ð° {hash}: {ex.Message}");
            }
        }

        // Ð˜Ð½Ð´ÐµÐºÑÐ¸Ñ€ÑƒÐµÐ¼ Ð½Ð°Ð¹Ð´ÐµÐ½Ð½Ñ‹Ðµ Ð°ÑƒÐ´Ð¸Ð¾Ñ„Ð°Ð¹Ð»Ñ‹ (Ñ Ñ‡Ñ‚ÐµÐ½Ð¸ÐµÐ¼ Ð²ÑÑ‚Ñ€Ð¾ÐµÐ½Ð½Ñ‹Ñ… Ñ‚ÐµÐ³Ð¾Ð² Ð¸Ð»Ð¸ ÑÐ¾Ð¿Ð¾ÑÑ‚Ð°Ð²Ð»ÐµÐ½Ð¸ÐµÐ¼ Ñ Ð½Ð°Ð¹Ð´ÐµÐ½Ð½Ñ‹Ð¼Ð¸ .osu)
        var metaByTitle = discoveredOsuMetadata
            .GroupBy(m => m.Meta.Title, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        int index = 0;
        foreach (var audio in discoveredAudioFiles)
        {
            ct.ThrowIfCancellationRequested();
            if (uniqueAudioEntries.ContainsKey(audio.Hash))
                continue;

            var (format, duration) = ProbeAudioFileInfo(audio.Path, $"track.{audio.Format.ToLowerInvariant()}");

            string title = $"Track #{++index}";
            string artist = "osu!lazer Library";
            string creator = "Unknown";
            int setId = 0;
            int mapId = 0;

            try
            {
                var abstraction = new HashedFileAbstraction($"track.{audio.Format.ToLowerInvariant()}", audio.Path);
                using var tagFile = TagLib.File.Create(abstraction);
                if (!string.IsNullOrWhiteSpace(tagFile.Tag.Title))
                    title = tagFile.Tag.Title;
                if (!string.IsNullOrWhiteSpace(tagFile.Tag.FirstPerformer))
                    artist = tagFile.Tag.FirstPerformer;
            }
            catch
            {
                // Ð¢ÐµÐ³Ð¸ Ð² Ð¸ÑÑ…Ð¾Ð´Ð½Ð¸ÐºÐµ Ð¼Ð¾Ð³ÑƒÑ‚ Ð¾Ñ‚ÑÑƒÑ‚ÑÑ‚Ð²Ð¾Ð²Ð°Ñ‚ÑŒ
            }

            if (metaByTitle.TryGetValue(title, out var matchedOsu))
            {
                title = matchedOsu.Meta.Title;
                artist = matchedOsu.Meta.Artist;
                creator = matchedOsu.Meta.Creator;
                setId = matchedOsu.Meta.BeatmapSetId;
                mapId = matchedOsu.Meta.BeatmapId;
            }

            var entry = new BeatmapEntry
            {
                BeatmapId = mapId,
                BeatmapSetId = setId,
                Title = title,
                Artist = artist,
                Creator = creator,
                AudioFilename = $"{audio.Hash}.{audio.Format.ToLowerInvariant()}",
                AudioHash = audio.Hash,
                AudioPhysicalPath = audio.Path,
                Format = format,
                Duration = duration
            };

            uniqueAudioEntries[audio.Hash] = entry;
        }
    }

    private static (string Format, TimeSpan Duration) ProbeAudioFileInfo(string physicalPath, string virtualFilename)
    {
        Span<byte> header = stackalloc byte[16];
        string format = Path.GetExtension(virtualFilename).TrimStart('.').ToUpperInvariant();

        try
        {
            using var fs = new FileStream(physicalPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            int read = fs.Read(header);
            var detected = OsuBeatmapParser.DetectAudioFormat(header[..read]);
            if (!string.IsNullOrEmpty(detected))
                format = detected;
        }
        catch
        {
            // Ignore
        }

        if (string.IsNullOrWhiteSpace(format))
            format = "AUDIO";

        TimeSpan duration = TimeSpan.Zero;
        try
        {
            var ext = format.ToLowerInvariant();
            var abstraction = new HashedFileAbstraction($"probe.{ext}", physicalPath);
            using var tagFile = TagLib.File.Create(abstraction);
            duration = tagFile.Properties.Duration;
        }
        catch
        {
            // Ð•ÑÐ»Ð¸ TagLib Ð½Ðµ ÑÐ¼Ð¾Ð³ Ð¾Ð¿Ñ€ÐµÐ´ÐµÐ»Ð¸Ñ‚ÑŒ Ð´Ð»Ð¸Ñ‚ÐµÐ»ÑŒÐ½Ð¾ÑÑ‚ÑŒ Ð±ÐµÐ· Ñ€Ð°ÑÑˆÐ¸Ñ€ÐµÐ½Ð¸Ñ
        }

        return (format, duration);
    }

    private sealed class HashedFileAbstraction : TagLib.File.IFileAbstraction
    {
        private readonly string _physicalPath;
        public string Name { get; }
        public Stream ReadStream => new FileStream(_physicalPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        public Stream WriteStream => new FileStream(_physicalPath, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);

        public HashedFileAbstraction(string virtualName, string physicalPath)
        {
            Name = virtualName;
            _physicalPath = physicalPath;
        }

        public void CloseStream(Stream stream) => stream.Dispose();
    }
}
