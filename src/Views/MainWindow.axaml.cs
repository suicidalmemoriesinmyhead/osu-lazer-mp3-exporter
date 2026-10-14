using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using OsuLazerAudioExporter.ViewModels;

namespace OsuLazerAudioExporter.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private async void OnSelectLazerFolderClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;

        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Выберите корневую папку osu!lazer",
            AllowMultiple = false
        });

        if (folders.Count > 0)
        {
            vm.LazerLibraryPath = folders[0].Path.LocalPath;
        }
    }

    private async void OnSelectOutputFolderClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;

        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Выберите папку назначения для MP3",
            AllowMultiple = false
        });

        if (folders.Count > 0)
        {
            vm.OutputDirectory = folders[0].Path.LocalPath;
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        if (DataContext is IDisposable disposable)
        {
            disposable.Dispose();
        }
        base.OnClosed(e);
    }
}