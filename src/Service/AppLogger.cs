using System.Collections.Concurrent;
using System.Text;

namespace OsuLazerAudioExporter.Services;

public sealed class AppLogger : IDisposable
{
    private readonly string _logFilePath;
    private readonly BlockingCollection<string> _logQueue = new(new ConcurrentQueue<string>());
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _writerTask;

    public event Action<string>? OnLogMessage;

    public AppLogger(string? logFilePath = null)
    {
        // Принудительно настраиваем консоль на UTF-8, чтобы русские буквы в логах не ломались
        try { Console.OutputEncoding = Encoding.UTF8; } catch { }

        _logFilePath = logFilePath ?? Path.Combine(AppContext.BaseDirectory, "osu-lazer-exporter.log");
        _writerTask = Task.Run(ProcessLogQueueAsync);
    }

    public void Info(string message) => Enqueue("INFO", message);
    public void Warn(string message) => Enqueue("WARN", message);
    public void Error(string message, Exception? ex = null) =>
        Enqueue("ERROR", ex is null ? message : $"{message} | {ex.GetType().Name}: {ex.Message}");

    private void Enqueue(string level, string message)
    {
        var formatted = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{level}] {message}";
        OnLogMessage?.Invoke(formatted);
        if (!_cts.IsCancellationRequested)
        {
            _logQueue.TryAdd(formatted);
        }
    }

    private async Task ProcessLogQueueAsync()
    {
        try
        {
            await using var stream = new FileStream(
                _logFilePath,
                FileMode.Append,
                FileAccess.Write,
                FileShare.ReadWrite,
                4096,
                useAsync: true);
            await using var writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };

            foreach (var line in _logQueue.GetConsumingEnumerable(_cts.Token))
            {
                await writer.WriteLineAsync(line);
            }
        }
        catch (OperationCanceledException)
        {
            // Штатное завершение
        }
        catch
        {
            // Игнорируем сбои файловой системы
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _logQueue.CompleteAdding();
        try
        {
            _writerTask.Wait(TimeSpan.FromSeconds(2));
        }
        catch
        {
            // Ignore
        }
        _cts.Dispose();
        _logQueue.Dispose();
    }
}