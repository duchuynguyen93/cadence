using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Cadence.App.Controls;
using Cadence.App.Services;
using Cadence.App.ViewModels;
using Cadence.App.Views;
using Cadence.Audio;
using Cadence.Core.Library;
using Cadence.Core.Playback;

namespace Cadence.App;

public partial class App : Application
{
    private PlaybackService? _playback;
    private LibraryDatabase? _database;
    private MainWindowViewModel? _viewModel;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            AppPaths.EnsureCreated();

            var settings = AppSettings.Load();
            _database = new LibraryDatabase(AppPaths.DatabaseFile);

            var metadataReader = new TrackMetadataReader(AppPaths.ArtworkCache);
            var scanner = new LibraryScanner(_database, metadataReader);

            // Static chứ không inject: ArtworkImage được tạo bên trong DataTemplate của
            // ListBox, nơi không có đường nào truyền dependency vào. Gán ở đây, trước khi
            // MainWindow được dựng nên chắc chắn có sẵn khi control đầu tiên xuất hiện.
            ArtworkImage.Service = new ArtworkService(AppPaths.ArtworkCache);

            // PlaybackService bắt SynchronizationContext ngay tại constructor để biết
            // đường đẩy event của BASS về UI thread — nên PHẢI khởi tạo ở đây,
            // trên UI thread, chứ không phải trong một Task nền.
            _playback = new PlaybackService(new BassAudioEngine());

            _viewModel = new MainWindowViewModel(_playback, _database, scanner, settings);

            desktop.MainWindow = new MainWindow { DataContext = _viewModel };
            desktop.ShutdownRequested += OnShutdownRequested;
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void OnShutdownRequested(object? sender, ShutdownRequestedEventArgs e)
    {
        // Thứ tự dọn dẹp có ý nghĩa: dừng timer của ViewModel trước, rồi mới hạ engine.
        // Ngược lại thì timer có thể đọc vào engine đã dispose và ném exception lúc thoát.
        _viewModel?.Dispose();
        _playback?.Dispose();
        _database?.Dispose();
        BassNativeLoader.Unload();
    }
}
