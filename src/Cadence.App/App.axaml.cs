using System;
using System.Collections.Generic;
using System.Linq;
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

            _viewModel = new MainWindowViewModel(_playback, _database, scanner, metadataReader, settings);

            desktop.MainWindow = new MainWindow { DataContext = _viewModel };
            desktop.ShutdownRequested += OnShutdownRequested;

            // File truyền qua dòng lệnh: "Open with", kéo thả lên icon, hoặc
            // double-click một đuôi file đã liên kết. Bộ cài đăng ký lệnh mở là
            // `Cadence.exe "%1"`, nên nếu không đọc chỗ này thì app mở lên rồi
            // ngồi im — đúng như báo cáo từ máy thật.
            //
            // Không await: OnFrameworkInitializationCompleted phải trả về để cửa
            // sổ hiện lên. Đọc tag chạy ở nền, phát ngay khi xong.
            var startupFiles = ParseStartupFiles(desktop.Args);
            if (startupFiles.Count > 0) _ = _viewModel.OpenPathsAsync(startupFiles);
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// Lọc ra các đường dẫn file từ tham số dòng lệnh.
    /// </summary>
    /// <remarks>
    /// Avalonia đã cắt bỏ argv[0] trước khi đưa vào <c>desktop.Args</c>, nên ở
    /// đây không phải lo chuyện đường dẫn của chính file exe bị hiểu nhầm thành
    /// file nhạc. Các tham số bắt đầu bằng '-' hoặc '/' bị bỏ qua để sau này
    /// thêm cờ dòng lệnh không phá chỗ này.
    /// </remarks>
    private static IReadOnlyList<string> ParseStartupFiles(string[]? args)
    {
        if (args is null || args.Length == 0) return [];

        return [.. args.Where(a =>
            !string.IsNullOrWhiteSpace(a) &&
            !a.StartsWith('-') &&
            !a.StartsWith('/'))];
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
