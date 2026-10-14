<div align="center">

# osu!lazer MP3 Exporter

**A lightning-fast, multi-threaded desktop utility to analyze local osu!lazer storage and batch-export audio files (.ogg / .wav) to MP3 with preserved ID3v2 metadata and background covers.**

[![Build & Release](https://github.com/suicidalmemoriesinmyhead/osu-lazer-mp3-exporter/actions/workflows/build.yml/badge.svg)](https://github.com/suicidalmemoriesinmyhead/osu-lazer-mp3-exporter/actions/workflows/build.yml)
[![.NET 8.0](https://img.shields.io/badge/.NET-8.0-7C3AED.svg?style=flat&logo=dotnet)](https://dotnet.microsoft.com/)
[![Avalonia UI](https://img.shields.io/badge/UI-Avalonia-3B82F6.svg?style=flat)](https://avaloniaui.net/)
[![License: MIT](https://img.shields.io/badge/license-MIT-059669.svg?style=flat)](LICENSE)
[![Platform](https://img.shields.io/badge/platform-Windows%20%7C%20Linux%20%7C%20macOS-informational.svg?style=flat)]()

[Download Latest Release](https://github.com/suicidalmemoriesinmyhead/osu-lazer-mp3-exporter/releases) · [Report Bug](https://github.com/suicidalmemoriesinmyhead/osu-lazer-mp3-exporter/issues)

</div>

---

> **Note**
> This application works **strictly offline** with your local `osu!lazer` library folder (`files/` and `client.realm`). It does not download any copyrighted music from the internet — it only indexes and converts your local resource cache to MP3.

---

## 💡 What it does

If you play `osu!lazer`, you know that its internal storage uses content-addressable hashed file names (e.g., `files/a/ab/ab1234...`) rather than clean artist names. 

**osu!lazer MP3 Exporter** scans your local library, parses `.osu` beatmap metadata, reads `client.realm` relationships, extracts `.ogg`/`.wav` tracks, decodes them via **FFmpeg**, and exports them into clean, organized folders (`Artist - Title.mp3`) with embedded ID3v2 tags and background artwork.

---

## ✨ Features

* **⚡ Smart Library Scanner:** Automatically detects default `osu!lazer` paths across Windows, Linux, and macOS. Parses `.osu` format or indexes storage directly via SQLite cache.
* **🎵 Batch MP3 Conversion:** Multithreaded worker pool powered by FFmpeg for fast, parallel decoding and encoding at 128 / 192 / 320 kbps.
* **🏷️ Metadata & Cover Art:** Automatically injects ID3v2 tags (Title, Artist, Album, Mapper comment) and embeds background images as album art.
* **⏱️ Timestamped Export Sessions:** Automatically creates a clean folder for each export session (e.g., `osu_mp3_2026-10-15_12-30-00/`) to keep your music library organized.
* **👁️ Watch Mode (Auto-sync):** Real-time file system watcher (`FileSystemWatcher`) that monitors your lazer folder in the background and auto-exports newly imported beatmaps.
* **🚀 Modern UI:** Built with **Avalonia UI** following the sleek dark aesthetic of `osu!lazer`, featuring real-time search filtering, virtualized tables, and one-click folder navigation.

---

## 🛠️ Tech Stack

* **Language:** C# 12 / .NET 8
* **UI Framework:** Avalonia UI 11 (Cross-platform desktop)
* **Database & IO:** Microsoft.Data.Sqlite, Realms (for client.realm parsing)
* **Audio & Tags:** FFmpeg, TagLibSharp
* **Architecture:** MVVM (CommunityToolkit.Mvvm), asynchronous worker channels, incremental SQLite cache.

---

## 🚀 Building from Source

Make sure you have **.NET 8 SDK** installed on your system.

```bash
# Clone the repository
git clone [https://github.com/suicidalmemoriesinmyhead/osu-lazer-mp3-exporter.git](https://github.com/suicidalmemoriesinmyhead/osu-lazer-mp3-exporter.git)
cd osu-lazer-mp3-exporter

# Restore NuGet packages
dotnet restore OsuLazerAudioExporter.sln

# Run unit tests
dotnet test OsuLazerAudioExporter.sln

# Run the application
dotnet run --project src/OsuLazerAudioExporter.csproj