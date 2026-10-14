namespace OsuLazerAudioExporter.Models;

public enum OutputStructure
{
    ArtistTitleFolder = 0, // Artist - Title/Artist - Title.mp3
    BeatmapSetIdFolder = 1, // BeatmapSetID/Artist - Title.mp3
    FlatFolder = 2          // Artist - Title.mp3 (всё в одну папку)
}