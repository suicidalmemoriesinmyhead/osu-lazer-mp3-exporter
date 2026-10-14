using System.Text;

namespace OsuLazerAudioExporter.Services;

public sealed record ParsedOsuMetadata(
    string Title,
    string Artist,
    string Creator,
    string Version,
    string AudioFilename,
    int BeatmapId,
    int BeatmapSetId,
    string? BackgroundFilename);

public static class OsuBeatmapParser
{
    /// <summary>
    /// Быстрая проверка по первым байтам файла, является ли он текстовым .osu файлом.
    /// </summary>
    public static bool IsOsuFileHeader(ReadOnlySpan<byte> headerBytes)
    {
        if (headerBytes.Length < 15)
            return false;

        // Учитываем возможный UTF-8 BOM (EF BB BF)
        var slice = headerBytes;
        if (slice.Length >= 3 && slice[0] == 0xEF && slice[1] == 0xBB && slice[2] == 0xBF)
        {
            slice = slice[3..];
        }

        var text = Encoding.UTF8.GetString(slice).TrimStart();
        return text.StartsWith("osu file format v", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Определяет аудиоформат по Magic Bytes (сигнатуре заголовка файла в хранилище lazer).
    /// </summary>
    public static string? DetectAudioFormat(ReadOnlySpan<byte> header)
    {
        if (header.Length < 4)
            return null;

        // OGG: "OggS" (4F 67 67 53)
        if (header[0] == 0x4F && header[1] == 0x67 && header[2] == 0x67 && header[3] == 0x53)
            return "OGG";

        // WAV: "RIFF" (52 49 46 46) ... "WAVE"
        if (header.Length >= 12 &&
            header[0] == 0x52 && header[1] == 0x49 && header[2] == 0x46 && header[3] == 0x46 &&
            header[8] == 0x57 && header[9] == 0x41 && header[10] == 0x56 && header[11] == 0x45)
            return "WAV";

        // MP3 с ID3v2: "ID3" (49 44 33)
        if (header[0] == 0x49 && header[1] == 0x44 && header[2] == 0x33)
            return "MP3";

        // MP3 без ID3 (MPEG Audio Frame Sync: FF FB, FF F3, FF F2, FF FA)
        if (header[0] == 0xFF && (header[1] & 0xE0) == 0xE0)
            return "MP3";

        return null;
    }

    public static ParsedOsuMetadata? ParseFile(string filePath)
    {
        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        return ParseStream(stream);
    }

    public static ParsedOsuMetadata? ParseStream(Stream stream)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 4096, leaveOpen: true);

        var firstLine = reader.ReadLine()?.Trim();
        if (string.IsNullOrEmpty(firstLine) || !firstLine.StartsWith("osu file format v", StringComparison.OrdinalIgnoreCase))
            return null;

        string title = string.Empty;
        string titleUnicode = string.Empty;
        string artist = string.Empty;
        string artistUnicode = string.Empty;
        string creator = "Unknown Creator";
        string version = "Normal";
        string audioFilename = string.Empty;
        int beatmapId = 0;
        int beatmapSetId = 0;
        string? backgroundFilename = null;

        string currentSection = string.Empty;

        while (reader.ReadLine() is { } rawLine)
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith("//", StringComparison.Ordinal))
                continue;

            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                currentSection = line;
                // Если мы дошли до [HitObjects] или [TimingPoints], все нужные метаданные уже прочитаны
                if (currentSection.Equals("[HitObjects]", StringComparison.OrdinalIgnoreCase))
                    break;
                continue;
            }

            if (currentSection.Equals("[General]", StringComparison.OrdinalIgnoreCase) ||
                currentSection.Equals("[Metadata]", StringComparison.OrdinalIgnoreCase))
            {
                var colonIndex = line.IndexOf(':');
                if (colonIndex <= 0)
                    continue;

                var key = line[..colonIndex].Trim();
                var val = line[(colonIndex + 1)..].Trim();

                switch (key)
                {
                    case "AudioFilename":
                        audioFilename = val;
                        break;
                    case "Title":
                        title = val;
                        break;
                    case "TitleUnicode":
                        titleUnicode = val;
                        break;
                    case "Artist":
                        artist = val;
                        break;
                    case "ArtistUnicode":
                        artistUnicode = val;
                        break;
                    case "Creator":
                        creator = val;
                        break;
                    case "Version":
                        version = val;
                        break;
                    case "BeatmapID":
                        _ = int.TryParse(val, out beatmapId);
                        break;
                    case "BeatmapSetID":
                        _ = int.TryParse(val, out beatmapSetId);
                        break;
                }
            }
            else if (currentSection.Equals("[Events]", StringComparison.OrdinalIgnoreCase) && backgroundFilename is null)
            {
                // Формат строки фона в [Events]: 0,0,"bg.jpg",0,0
                if (line.StartsWith("0,0,", StringComparison.Ordinal))
                {
                    var firstQuote = line.IndexOf('"');
                    var lastQuote = line.LastIndexOf('"');
                    if (firstQuote >= 0 && lastQuote > firstQuote)
                    {
                        backgroundFilename = line[(firstQuote + 1)..lastQuote];
                    }
                }
            }
        }

        var finalTitle = !string.IsNullOrWhiteSpace(title) ? title : titleUnicode;
        var finalArtist = !string.IsNullOrWhiteSpace(artist) ? artist : artistUnicode;

        if (string.IsNullOrWhiteSpace(audioFilename))
            return null;

        return new ParsedOsuMetadata(
            Title: string.IsNullOrWhiteSpace(finalTitle) ? "Unknown Title" : finalTitle,
            Artist: string.IsNullOrWhiteSpace(finalArtist) ? "Unknown Artist" : finalArtist,
            Creator: string.IsNullOrWhiteSpace(creator) ? "Unknown Creator" : creator,
            Version: version,
            AudioFilename: audioFilename,
            BeatmapId: beatmapId,
            BeatmapSetId: beatmapSetId,
            BackgroundFilename: backgroundFilename);
    }
}