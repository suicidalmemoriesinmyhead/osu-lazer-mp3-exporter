namespace OsuLazerAudioExporter.Services;

public sealed class LibraryWatcherService : IDisposable
{
    private readonly AppLogger _logger;
    private FileSystemWatcher? _watcher;
    private CancellationTokenSource? _debounceCts;
    private readonly object _sync = new();

    public event Func<Task>? OnLibraryChangedAsync;
    public bool IsWatching => _watcher?.EnableRaisingEvents == true;

    public LibraryWatcherService(AppLogger logger)
    {
        _logger = logger;
    }

    public void Start(string lazerRootPath)
    {
        Stop();

        var filesDir = Path.Combine(lazerRootPath, "files");
        if (!Directory.Exists(filesDir))
        {
            _logger.Warn($"Невозможно запустить авто-режим: папка не существует ({filesDir})");
            return;
        }

        _watcher = new FileSystemWatcher(filesDir)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.CreationTime,
            EnableRaisingEvents = true
        };

        _watcher.Created += OnFileEvent;
        _watcher.Changed += OnFileEvent;
        _watcher.Renamed += OnFileEvent;

        _logger.Info($"Авто-режим (FileSystemWatcher) запущен для: {filesDir}");
    }

    public void Stop()
    {
        if (_watcher is not null)
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Created -= OnFileEvent;
            _watcher.Changed -= OnFileEvent;
            _watcher.Renamed -= OnFileEvent;
            _watcher.Dispose();
            _watcher = null;
            _logger.Info("Авто-режим остановлен.");
        }
    }

    private void OnFileEvent(object sender, FileSystemEventArgs e)
    {
        // Дебаунсинг 3 секунды, чтобы дождаться полной распаковки новой карты в osu!lazer
        CancellationTokenSource cts;
        lock (_sync)
        {
            _debounceCts?.Cancel();
            _debounceCts?.Dispose();
            _debounceCts = new CancellationTokenSource();
            cts = _debounceCts;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(3), cts.Token);
                if (OnLibraryChangedAsync is not null)
                {
                    _logger.Info("Обнаружены новые файлы в библиотеке osu!lazer. Запуск фонового экспорта...");
                    await OnLibraryChangedAsync.Invoke();
                }
            }
            catch (OperationCanceledException)
            {
                // Сброс таймера дебаунса при поступлении следующего файла
            }
            catch (Exception ex)
            {
                _logger.Error("Ошибка в обработчике авто-режима", ex);
            }
        });
    }

    public void Dispose()
    {
        Stop();
        _debounceCts?.Cancel();
        _debounceCts?.Dispose();
    }
}