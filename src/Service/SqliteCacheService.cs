using Microsoft.Data.Sqlite;
using OsuLazerAudioExporter.Models;

namespace OsuLazerAudioExporter.Services;

public sealed class SqliteCacheService
{
    private readonly string _connectionString;
    private readonly AppLogger _logger;

    public SqliteCacheService(AppLogger logger)
    {
        _logger = logger;
        var dbPath = Path.Combine(AppContext.BaseDirectory, "metadata_cache.db");
        _connectionString = $"Data Source={dbPath};Cache=Shared";
        InitializeDatabase();
    }

    private void InitializeDatabase()
    {
        try
        {
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE IF NOT EXISTS BeatmapCache (
                    OsuFileHash TEXT PRIMARY KEY,
                    BeatmapId INTEGER,
                    BeatmapSetId INTEGER,
                    Title TEXT,
                    Artist TEXT,
                    Creator TEXT,
                    Version TEXT,
                    AudioFilename TEXT,
                    BackgroundFilename TEXT,
                    AudioHash TEXT,
                    CoverHash TEXT,
                    Format TEXT,
                    DurationSeconds REAL,
                    UpdatedAt INTEGER
                );

                CREATE TABLE IF NOT EXISTS ExportedAudio (
                    AudioHash TEXT PRIMARY KEY,
                    DestinationPath TEXT,
                    Bitrate INTEGER,
                    ExportedAt INTEGER
                );
                """;
            cmd.ExecuteNonQuery();
        }
        catch (Exception ex)
        {
            _logger.Error("Ошибка инициализации SQLite-кэша", ex);
        }
    }

    public bool IsAudioAlreadyExported(string audioHash, int bitrate, out string? existingPath)
    {
        existingPath = null;
        if (string.IsNullOrWhiteSpace(audioHash))
            return false;

        try
        {
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT DestinationPath
                FROM ExportedAudio
                WHERE AudioHash = $hash AND Bitrate = $bitrate
                LIMIT 1;
                """;
            cmd.Parameters.AddWithValue("$hash", audioHash);
            cmd.Parameters.AddWithValue("$bitrate", bitrate);

            var result = cmd.ExecuteScalar() as string;
            if (!string.IsNullOrWhiteSpace(result) && File.Exists(result))
            {
                existingPath = result;
                return true;
            }
        }
        catch (Exception ex)
        {
            _logger.Warn($"Не удалось проверить кэш экспорта: {ex.Message}");
        }

        return false;
    }

    public void MarkAudioExported(string audioHash, string destinationPath, int bitrate)
    {
        try
        {
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO ExportedAudio (AudioHash, DestinationPath, Bitrate, ExportedAt)
                VALUES ($hash, $path, $bitrate, $ts)
                ON CONFLICT(AudioHash) DO UPDATE SET
                    DestinationPath = excluded.DestinationPath,
                    Bitrate = excluded.Bitrate,
                    ExportedAt = excluded.ExportedAt;
                """;
            cmd.Parameters.AddWithValue("$hash", audioHash);
            cmd.Parameters.AddWithValue("$path", destinationPath);
            cmd.Parameters.AddWithValue("$bitrate", bitrate);
            cmd.Parameters.AddWithValue("$ts", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            cmd.ExecuteNonQuery();
        }
        catch (Exception ex)
        {
            _logger.Warn($"Не удалось записать статус экспорта в SQLite: {ex.Message}");
        }
    }

    public Dictionary<string, BeatmapEntry> LoadCachedBeatmaps(string lazerFilesRoot)
    {
        var result = new Dictionary<string, BeatmapEntry>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT OsuFileHash, BeatmapId, BeatmapSetId, Title, Artist, Creator,
                       Version, AudioFilename, BackgroundFilename, AudioHash, CoverHash,
                       Format, DurationSeconds
                FROM BeatmapCache;
                """;

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var osuHash = reader.GetString(0);
                var audioHash = reader.GetString(9);
                var coverHash = reader.IsDBNull(10) ? null : reader.GetString(10);

                var audioPhysical = LazerLibraryScanner.GetHashedFilePath(lazerFilesRoot, audioHash);
                if (!File.Exists(audioPhysical))
                    continue;

                var coverPhysical = !string.IsNullOrEmpty(coverHash)
                    ? LazerLibraryScanner.GetHashedFilePath(lazerFilesRoot, coverHash)
                    : null;

                result[osuHash] = new BeatmapEntry
                {
                    BeatmapId = reader.GetInt32(1),
                    BeatmapSetId = reader.GetInt32(2),
                    Title = reader.GetString(3),
                    Artist = reader.GetString(4),
                    Creator = reader.GetString(5),
                    Version = reader.GetString(6),
                    AudioFilename = reader.GetString(7),
                    BackgroundFilename = reader.IsDBNull(8) ? null : reader.GetString(8),
                    AudioHash = audioHash,
                    AudioPhysicalPath = audioPhysical,
                    CoverPhysicalPath = coverPhysical,
                    Format = reader.GetString(11),
                    Duration = TimeSpan.FromSeconds(reader.GetDouble(12))
                };
            }
        }
        catch (Exception ex)
        {
            _logger.Warn($"Ошибка чтения кэша карт: {ex.Message}");
        }
        return result;
    }

    public void SaveBeatmapToCache(string osuFileHash, BeatmapEntry entry, string? coverHash)
    {
        try
        {
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO BeatmapCache (
                    OsuFileHash, BeatmapId, BeatmapSetId, Title, Artist, Creator,
                    Version, AudioFilename, BackgroundFilename, AudioHash, CoverHash,
                    Format, DurationSeconds, UpdatedAt
                ) VALUES (
                    $osuHash, $bId, $bsId, $title, $artist, $creator,
                    $version, $audioFile, $bgFile, $audioHash, $coverHash,
                    $format, $duration, $ts
                )
                ON CONFLICT(OsuFileHash) DO UPDATE SET
                    DurationSeconds = excluded.DurationSeconds,
                    UpdatedAt = excluded.UpdatedAt;
                """;
            cmd.Parameters.AddWithValue("$osuHash", osuFileHash);
            cmd.Parameters.AddWithValue("$bId", entry.BeatmapId);
            cmd.Parameters.AddWithValue("$bsId", entry.BeatmapSetId);
            cmd.Parameters.AddWithValue("$title", entry.Title);
            cmd.Parameters.AddWithValue("$artist", entry.Artist);
            cmd.Parameters.AddWithValue("$creator", entry.Creator);
            cmd.Parameters.AddWithValue("$version", entry.Version);
            cmd.Parameters.AddWithValue("$audioFile", entry.AudioFilename);
            cmd.Parameters.AddWithValue("$bgFile", (object?)entry.BackgroundFilename ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$audioHash", entry.AudioHash);
            cmd.Parameters.AddWithValue("$coverHash", (object?)coverHash ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$format", entry.Format);
            cmd.Parameters.AddWithValue("$duration", entry.Duration.TotalSeconds);
            cmd.Parameters.AddWithValue("$ts", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            cmd.ExecuteNonQuery();
        }
        catch (Exception ex)
        {
            _logger.Warn($"Не удалось сохранить карту в кэш: {ex.Message}");
        }
    }
}