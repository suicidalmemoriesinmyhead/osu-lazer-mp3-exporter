using System.Text;
using OsuLazerAudioExporter.Models;
using OsuLazerAudioExporter.Services;
using Xunit;

namespace OsuLazerAudioExporter.Tests;

public sealed class OsuBeatmapParserTests
{
    [Fact]
    public void ParseStream_ValidOsuFile_ExtractsAllMetadataAndCover()
    {
        const string sampleOsu = """
            osu file format v14

            [General]
            AudioFilename: audio.ogg
            AudioLeadIn: 0
            PreviewTime: 45120

            [Metadata]
            Title:FREEDOM DiVE
            TitleUnicode:FREEDOM DiVE
            Artist:xi
            ArtistUnicode:xi
            Creator:Nakagawa-Kanon
            Version:FOUR DIMENSIONS
            BeatmapID:129891
            BeatmapSetID:39804

            [Events]
            //Background and Video events
            0,0,"freedom_dive_bg.jpg",0,0
            """;

        using var ms = new MemoryStream(Encoding.UTF8.GetBytes(sampleOsu));
        var result = OsuBeatmapParser.ParseStream(ms);

        Assert.NotNull(result);
        Assert.Equal("FREEDOM DiVE", result.Title);
        Assert.Equal("xi", result.Artist);
        Assert.Equal("Nakagawa-Kanon", result.Creator);
        Assert.Equal("FOUR DIMENSIONS", result.Version);
        Assert.Equal("audio.ogg", result.AudioFilename);
        Assert.Equal(129891, result.BeatmapId);
        Assert.Equal(39804, result.BeatmapSetId);
        Assert.Equal("freedom_dive_bg.jpg", result.BackgroundFilename);
    }

    [Fact]
    public void ParseStream_CorruptedOrEmptyStream_ReturnsNull()
    {
        using var ms = new MemoryStream("not an osu file"u8.ToArray());
        var result = OsuBeatmapParser.ParseStream(ms);

        Assert.Null(result);
    }

    [Theory]
    [InlineData(new byte[] { 0x4F, 0x67, 0x67, 0x53 }, "OGG")]
    [InlineData(new byte[] { 0x49, 0x44, 0x33, 0x04 }, "MP3")]
    [InlineData(new byte[] { 0xFF, 0xFB, 0x90, 0x64 }, "MP3")]
    public void DetectAudioFormat_RecognizesMagicBytes(byte[] header, string expectedFormat)
    {
        var detected = OsuBeatmapParser.DetectAudioFormat(header);
        Assert.Equal(expectedFormat, detected);
    }

    [Fact]
    public void BuildDestinationFilePath_SanitizesIllegalCharacters()
    {
        var entry = new BeatmapEntry
        {
            Artist = "Camellia:Feat*Nanahira",
            Title = "Bassdrop?Freaks!",
            BeatmapSetId = 999111
        };

        var path = AudioExportService.BuildDestinationFilePath(
            "/exports",
            OutputStructure.ArtistTitleFolder,
            entry);

        Assert.DoesNotContain(":", Path.GetFileName(path));
        Assert.DoesNotContain("*", Path.GetFileName(path));
        Assert.DoesNotContain("?", Path.GetFileName(path));
        Assert.EndsWith(".mp3", path);
    }
}