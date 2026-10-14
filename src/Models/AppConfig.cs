using System.Text.Json.Serialization;

namespace OsuLazerAudioExporter.Models;

public sealed class AppConfig
{
    public string LazerLibraryPath { get; set; } = string.Empty;
    public string OutputDirectory { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
        "osu-lazer-exports");

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public OutputStructure OutputFormat { get; set; } = OutputStructure.ArtistTitleFolder;

    public int Mp3BitrateKbps { get; set; } = 320; // 128, 192, 320
    public bool AutoStartWatchMode { get; set; } = false;
    public string FfmpegCustomPath { get; set; } = string.Empty;
}