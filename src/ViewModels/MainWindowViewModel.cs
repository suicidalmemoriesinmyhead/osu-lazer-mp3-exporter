using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OsuLazerAudioExporter.Models;
using OsuLazerAudioExporter.Services;

namespace OsuLazerAudioExporter.ViewModels;

public partial class MainWindowViewModel : ViewModelBase, IDisposable
{
    private readonly AppLogger _logger;
    private readonly ConfigService _configService;
    private readonly SqliteCacheService _cacheService;
    private readonly LazerLibraryScanner _scanner;
    private readonly FfmpegService _ffmpegService;
    private readonly AudioExportService _exportService;
    private readonly LibraryWatcherService _watcherService;

    private List<BeatmapEntry> _allBeatmaps = [];
    private CancellationTokenSource? _operationCts;

    public ObservableCollection<BeatmapEntry> FilteredBeatmaps { get; } = [];
    public ObservableCollection<string> LogEntries { get; } = [];

    public IReadOnlyList<int> AvailableBitrates { get; } = [128, 192, 320];

    [ObservableProperty]
    private string _lazerLibraryPath = string.Empty;

    [ObservableProperty]
    private string _outputDirectory = string.Empty;

    [ObservableProperty]
    private int _selectedBitrate = 320;

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _isWatchModeEnabled;

    [ObservableProperty]
    private double _overallProgressPercent;

    [ObservableProperty]
    private string _statusMessage = "Нажмите «1. Сканировать библиотеку», чтобы загрузить треки из osu!lazer";

    [ObservableProperty]
    private bool _isFfmpegMissing;

    [ObservableProperty]
    private int _totalTracksCount;

    [ObservableProperty]
    private int _selectedTracksCount;

    [ObservableProperty]
    private int _sessionExportedCount;

    [ObservableProperty]
    private string _lastCreatedExportFolder = string.Empty;

    public bool IsLibraryValid => ConfigService.IsValidLazerLibrary(LazerLibraryPath);
    public string LibraryStatusBadge => IsLibraryValid ? "✅ Папка files/ найдена" : "❌ Папка files/ не найдена";
    public string WatchModeButtonText => IsWatchModeEnabled ? "🟢 Авто-режим: ВКЛ" : "⚪ Авто-режим: ВЫКЛ";

    public MainWindowViewModel()
    {
        _logger = new AppLogger();
        _logger.OnLogMessage += msg =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                LogEntries.Add(msg);
                if (LogEntries.Count > 500)
                    LogEntries.RemoveAt(0);
            });
        };

        _configService = new ConfigService(_logger);
        _cacheService = new SqliteCacheService(_logger);
        _scanner = new LazerLibraryScanner(_logger, _cacheService);
        _ffmpegService = new FfmpegService(_logger);
        _exportService = new AudioExportService(_ffmpegService, _cacheService, _logger);
        _watcherService = new LibraryWatcherService(_logger);
        _watcherService.OnLibraryChangedAsync += HandleAutoWatchTriggerAsync;

        var cfg = _configService.Load();
        LazerLibraryPath = cfg.LazerLibraryPath;
        OutputDirectory = cfg.OutputDirectory;
        SelectedBitrate = cfg.Mp3BitrateKbps;

        CheckFfmpegStatus(cfg.FfmpegCustomPath);

        if (cfg.AutoStartWatchMode && IsLibraryValid)
        {
            ToggleWatchMode();
        }
    }

    partial void OnSearchQueryChanged(string value) => ApplyFilter();

    partial void OnLazerLibraryPathChanged(string value)
    {
        OnPropertyChanged(nameof(IsLibraryValid));
        OnPropertyChanged(nameof(LibraryStatusBadge));
        SaveCurrentConfig();
    }

    partial void OnOutputDirectoryChanged(string value) => SaveCurrentConfig();
    partial void OnSelectedBitrateChanged(int value) => SaveCurrentConfig();

    private void CheckFfmpegStatus(string? customPath = null)
    {
        var resolved = _ffmpegService.ResolveFfmpegExecutable(customPath);
        IsFfmpegMissing = string.IsNullOrEmpty(resolved);
        if (IsFfmpegMissing)
        {
            _logger.Warn("FFmpeg не найден. Нажмите кнопку «Скачать FFmpeg» вверху окна.");
        }
        else
        {
            _logger.Info($"FFmpeg готов к работе: {resolved}");
        }
    }

    private AppConfig BuildCurrentConfig() => new()
    {
        LazerLibraryPath = LazerLibraryPath,
        OutputDirectory = OutputDirectory,
        OutputFormat = OutputStructure.FlatFolder,
        Mp3BitrateKbps = SelectedBitrate,
        AutoStartWatchMode = IsWatchModeEnabled
    };

    private void SaveCurrentConfig() => _configService.Save(BuildCurrentConfig());

    [RelayCommand]
    public void ClearSearch() => SearchQuery = string.Empty;

    [RelayCommand]
    public void CancelOperation()
    {
        _operationCts?.Cancel();
        StatusMessage = "Операция останавливается...";
    }

    [RelayCommand]
    public void OpenExportFolder()
    {
        var target = !string.IsNullOrWhiteSpace(LastCreatedExportFolder) && Directory.Exists(LastCreatedExportFolder)
            ? LastCreatedExportFolder
            : OutputDirectory;

        try
        {
            Directory.CreateDirectory(target);
            Process.Start(new ProcessStartInfo
            {
                FileName = target,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            _logger.Error("Не удалось открыть папку в проводнике", ex);
        }
    }

    [RelayCommand]
    public async Task DownloadFfmpegAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        StatusMessage = "Скачивание FFmpeg... Пожалуйста, подождите.";

        try
        {
            var progress = new Progress<double>(p => OverallProgressPercent = p);
            var path = await _ffmpegService.DownloadFfmpegAsync(progress);
            IsFfmpegMissing = string.IsNullOrEmpty(path);
            StatusMessage = IsFfmpegMissing
                ? "Не удалось скачать FFmpeg автоматически"
                : "✅ FFmpeg успешно скачан и готов к конвертации!";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public async Task ScanLibraryAsync()
    {
        if (IsBusy) return;

        if (!IsLibraryValid)
        {
            StatusMessage = "❌ Укажите корректную папку osu!lazer (внутри должна быть папка files/)";
            _logger.Warn(StatusMessage);
            return;
        }

        IsBusy = true;
        OverallProgressPercent = 0;
        StatusMessage = "🔍 Сканирование библиотеки osu!lazer...";
        _operationCts = new CancellationTokenSource();

        try
        {
            foreach (var old in _allBeatmaps)
                old.PropertyChanged -= OnBeatmapPropertyChanged;

            _allBeatmaps = await _scanner.ScanLibraryAsync(LazerLibraryPath, _operationCts.Token);

            foreach (var item in _allBeatmaps)
                item.PropertyChanged += OnBeatmapPropertyChanged;

            TotalTracksCount = _allBeatmaps.Count;
            ApplyFilter();
            RecalculateSelectedCount();

            StatusMessage = $"✅ Найдено уникальных треков: {_allBeatmaps.Count}. Выберите нужные и нажмите «2. Экспортировать в MP3».";
            OverallProgressPercent = 100;
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "⏹ Сканирование отменено";
        }
        catch (Exception ex)
        {
            StatusMessage = $"❌ Ошибка сканирования: {ex.Message}";
            _logger.Error("Сбой при сканировании библиотеки", ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void OnBeatmapPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(BeatmapEntry.IsSelected))
        {
            RecalculateSelectedCount();
        }
    }

    private void RecalculateSelectedCount()
    {
        SelectedTracksCount = _allBeatmaps.Count(x => x.IsSelected);
    }

    [RelayCommand]
    public void SelectAll()
    {
        foreach (var item in FilteredBeatmaps)
            item.IsSelected = true;
        RecalculateSelectedCount();
    }

    [RelayCommand]
    public void DeselectAll()
    {
        foreach (var item in FilteredBeatmaps)
            item.IsSelected = false;
        RecalculateSelectedCount();
    }

    [RelayCommand]
    public async Task ExportSelectedAsync()
    {
        if (IsBusy) return;

        if (IsFfmpegMissing)
        {
            StatusMessage = "⚠️ Сначала нажмите «Скачать FFmpeg» в правом верхнем углу!";
            return;
        }

        var selected = FilteredBeatmaps.Where(x => x.IsSelected).ToList();
        if (selected.Count == 0)
        {
            StatusMessage = "⚠️ Не выбрано ни одного трека. Отметьте галочками нужные песни.";
            return;
        }

        IsBusy = true;
        OverallProgressPercent = 0;
        _operationCts = new CancellationTokenSource();

        var progress = new Progress<ExportProgress>(p =>
        {
            OverallProgressPercent = p.TotalCount > 0 ? (double)p.CompletedCount / p.TotalCount * 100.0 : 0;
            StatusMessage = $"🎵 Конвертация [{p.CompletedCount}/{p.TotalCount}]: {p.CurrentTrackName}";
        });

        try
        {
            var createdFolder = await _exportService.ExportBatchAsync(
                selected,
                BuildCurrentConfig(),
                progress,
                _operationCts.Token);

            LastCreatedExportFolder = createdFolder;
            SessionExportedCount += selected.Count(x => x.Status.Contains("Готово"));

            var folderName = Path.GetFileName(createdFolder);
            StatusMessage = $"🎉 Готово! Все MP3 сохранены в папку «{folderName}». Нажмите «Открыть папку с MP3»!";

            NotificationService.SendSystemNotification(
                "osu!lazer MP3 Exporter",
                $"Экспорт завершён! Файлы сохранены в {folderName}");
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "⏹ Экспорт остановлен пользователем";
        }
        catch (Exception ex)
        {
            StatusMessage = $"❌ Ошибка экспорта: {ex.Message}";
            _logger.Error("Критическая ошибка пакетного экспорта", ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public void ToggleWatchMode()
    {
        if (IsWatchModeEnabled)
        {
            _watcherService.Stop();
            IsWatchModeEnabled = false;
        }
        else
        {
            if (!IsLibraryValid)
            {
                StatusMessage = "⚠️ Сначала укажите валидную папку osu!lazer";
                return;
            }
            _watcherService.Start(LazerLibraryPath);
            IsWatchModeEnabled = true;
        }

        OnPropertyChanged(nameof(WatchModeButtonText));
        SaveCurrentConfig();
    }

    private async Task HandleAutoWatchTriggerAsync()
    {
        if (IsBusy || IsFfmpegMissing)
            return;

        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            await ScanLibraryAsync();
            await ExportSelectedAsync();
        });
    }

    private void ApplyFilter()
    {
        FilteredBeatmaps.Clear();
        var query = SearchQuery.Trim();

        IEnumerable<BeatmapEntry> source = _allBeatmaps;
        if (!string.IsNullOrEmpty(query))
        {
            source = source.Where(b =>
                b.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                b.Artist.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                b.Creator.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                b.Format.Contains(query, StringComparison.OrdinalIgnoreCase));
        }

        foreach (var item in source)
        {
            FilteredBeatmaps.Add(item);
        }
    }

    public void Dispose()
    {
        _operationCts?.Cancel();
        _operationCts?.Dispose();
        _watcherService.Dispose();
        _logger.Dispose();
    }
}