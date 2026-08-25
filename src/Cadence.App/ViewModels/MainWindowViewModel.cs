using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using Cadence.App.Services;
using Cadence.Core.Library;
using Cadence.Core.Models;
using Cadence.Core.Playback;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Cadence.App.ViewModels;

public partial class MainWindowViewModel : ViewModelBase, IDisposable
{
    private readonly PlaybackService _playback;
    private readonly LibraryDatabase _database;
    private readonly LibraryScanner _scanner;
    private readonly AppSettings _settings;
    private readonly DispatcherTimer _tick;

    /// <summary>Toàn bộ thư viện. Danh sách hiển thị là bản đã lọc từ đây.</summary>
    private IReadOnlyList<Track> _allTracks = [];

    private CancellationTokenSource? _scanCancellation;

    /// <summary>
    /// True khi thanh tua đang được cập nhật theo tiến độ phát, để phân biệt với
    /// việc người dùng tự kéo. Thiếu cờ này thì mỗi lần timer cập nhật sẽ bị hiểu
    /// nhầm là seek, và nhạc sẽ giật liên tục.
    /// </summary>
    private bool _updatingPositionFromEngine;

    public MainWindowViewModel(
        PlaybackService playback,
        LibraryDatabase database,
        LibraryScanner scanner,
        AppSettings settings)
    {
        _playback = playback;
        _database = database;
        _scanner = scanner;
        _settings = settings;

        _volume = settings.Volume;
        _shuffle = settings.Shuffle;
        _playback.Volume = settings.Volume;
        _playback.Shuffle = settings.Shuffle;
        if (Enum.TryParse<RepeatMode>(settings.RepeatMode, out var repeat))
        {
            _repeatMode = repeat;
            _playback.RepeatMode = repeat;
        }

        _playback.CurrentTrackChanged += (_, track) => HandleCurrentTrackChanged(track);
        _playback.StateChanged += (_, state) => HandlePlaybackStateChanged(state);
        _playback.ErrorOccurred += (_, error) => StatusMessage = error.Message;

        // 100ms là điểm cân bằng: thanh tua trông vẫn mượt với mắt người, mà không
        // đánh thức CPU 60 lần/giây chỉ để dịch vài pixel. Visualizer sau này cần
        // 60fps thì phải dùng render loop riêng, không dùng timer này.
        _tick = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _tick.Tick += (_, _) => UpdatePlaybackProgress();
        _tick.Start();

        LoadLibraryFromDatabase();

        // Quét lại ở nền ngay khi mở app để bắt các file được thêm/xoá/sửa tag từ lần
        // chạy trước. Rẻ thôi: file không đổi mtime sẽ bị bỏ qua mà không cần mở ra đọc,
        // nên lần quét thứ hai trở đi gần như chỉ tốn công liệt kê thư mục.
        //
        // Cố ý không await — constructor phải trả về ngay để cửa sổ hiện lên. Danh sách
        // bài đã nạp từ DB rồi, quét xong sẽ tự làm mới.
        if (settings.MusicFolders.Count > 0) _ = RescanAsync();
    }

    // ── Trạng thái hiển thị ───────────────────────────────────────────────

    public ObservableCollection<Track> VisibleTracks { get; } = [];

    [ObservableProperty] private Track? _currentTrack;
    [ObservableProperty] private bool _isPlaying;
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private bool _isScanning;
    [ObservableProperty] private double _scanProgress;

    [ObservableProperty] private double _positionSeconds;
    [ObservableProperty] private double _durationSeconds;
    [ObservableProperty] private string _positionText = "0:00";
    [ObservableProperty] private string _durationText = "0:00";

    [ObservableProperty] private float _volume = 0.7f;
    [ObservableProperty] private bool _shuffle;
    [ObservableProperty] private RepeatMode _repeatMode = RepeatMode.Off;
    [ObservableProperty] private string _searchText = string.Empty;

    /// <summary>Bài đang được chọn trong danh sách (một cú click), khác với bài đang phát.</summary>
    [ObservableProperty] private Track? _selectedTrack;

    /// <summary>
    /// Chế độ thu gọn: chỉ còn ảnh bìa, tên bài và cụm nút phát.
    ///
    /// Cố tình KHÔNG lưu vào settings. Mở app lần sau mà hiện ra một cửa sổ tí
    /// hon không có danh sách nhạc thì người dùng sẽ tưởng app hỏng — trong khi
    /// cái giá của việc không nhớ chỉ là một cú bấm.
    /// </summary>
    [ObservableProperty] private bool _isMiniMode;

    public bool HasLibrary => _allTracks.Count > 0;
    public string FormatText => _playback.CurrentFormat?.ToString() ?? string.Empty;

    /// <summary>
    /// Giá trị Maximum cho thanh tua.
    ///
    /// Không bind thẳng DurationSeconds vì lúc chưa phát gì nó bằng 0, mà Slider với
    /// Minimum == Maximum sẽ vẽ núm ở tận cùng bên phải — trông như bài đã phát xong.
    /// Trả về 1 trong trường hợp đó để núm nằm đúng bên trái.
    /// </summary>
    public double ScrubMaximum => DurationSeconds > 0 ? DurationSeconds : 1;

    /// <summary>Chỉ cho kéo thanh tua khi thật sự có bài đang nạp.</summary>
    public bool CanScrub => DurationSeconds > 0;

    // ── Phản ứng khi thuộc tính đổi (source generator gọi tự động) ─────────

    partial void OnVolumeChanged(float value)
    {
        _playback.Volume = value;
        _settings.Volume = value;
        _settings.Save();
    }

    partial void OnShuffleChanged(bool value)
    {
        _playback.Shuffle = value;
        _settings.Shuffle = value;
        _settings.Save();
    }

    partial void OnRepeatModeChanged(RepeatMode value)
    {
        _playback.RepeatMode = value;
        _settings.RepeatMode = value.ToString();
        _settings.Save();
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    partial void OnDurationSecondsChanged(double value)
    {
        OnPropertyChanged(nameof(ScrubMaximum));
        OnPropertyChanged(nameof(CanScrub));
    }

    partial void OnPositionSecondsChanged(double value)
    {
        // Chỉ seek khi thay đổi đến TỪ người dùng. Xem chú thích ở _updatingPositionFromEngine.
        if (_updatingPositionFromEngine) return;
        _playback.Seek(TimeSpan.FromSeconds(value));
    }

    // ── Lệnh ──────────────────────────────────────────────────────────────

    [RelayCommand]
    private void TogglePlayPause()
    {
        // Mở app lên thì queue rỗng, nên bấm Play sẽ không có gì xảy ra — người dùng
        // tưởng app hỏng. Ở trạng thái đó, hiểu Play là "phát bài đang chọn", và nếu
        // chưa chọn gì thì phát từ đầu danh sách.
        if (_playback.CurrentTrack is null)
        {
            var track = SelectedTrack ?? VisibleTracks.FirstOrDefault();
            if (track is null) return;

            PlayTrack(track);
            return;
        }

        _playback.TogglePlayPause();
    }

    [RelayCommand]
    private void Next() => _playback.Next();

    [RelayCommand]
    private void Previous() => _playback.Previous();

    [RelayCommand]
    private void ToggleShuffle() => Shuffle = !Shuffle;

    [RelayCommand]
    private void ToggleMiniMode() => IsMiniMode = !IsMiniMode;

    [RelayCommand]
    private void CycleRepeat() => RepeatMode = RepeatMode switch
    {
        RepeatMode.Off => RepeatMode.All,
        RepeatMode.All => RepeatMode.One,
        _ => RepeatMode.Off,
    };

    /// <summary>Phát bài được double-click, và nạp cả danh sách đang hiển thị làm queue.</summary>
    [RelayCommand]
    private void PlayTrack(Track? track)
    {
        if (track is null) return;

        var index = VisibleTracks.IndexOf(track);
        if (index < 0) return;

        _playback.PlayList(VisibleTracks.ToList(), index);
    }

    /// <summary>Thêm thư mục nhạc rồi quét. Đường dẫn do View chọn qua hộp thoại hệ thống.</summary>
    public async Task AddFolderAsync(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder)) return;

        if (!_settings.MusicFolders.Contains(folder, StringComparer.OrdinalIgnoreCase))
        {
            _settings.MusicFolders.Add(folder);
            _settings.Save();
        }

        await RescanAsync();
    }

    [RelayCommand]
    private async Task RescanAsync()
    {
        if (IsScanning) return;

        if (_settings.MusicFolders.Count == 0)
        {
            StatusMessage = "Chưa có thư mục nhạc nào. Bấm “Thêm thư mục” để bắt đầu.";
            return;
        }

        IsScanning = true;
        ScanProgress = 0;
        StatusMessage = "Đang quét…";

        _scanCancellation?.Cancel();
        _scanCancellation = new CancellationTokenSource();

        try
        {
            var progress = new Progress<ScanProgress>(p =>
            {
                StatusMessage = p.CurrentFile is null
                    ? $"Đã quét {p.FilesSeen} file"
                    : $"Đang quét… {p.FilesSeen} file · {System.IO.Path.GetFileName(p.CurrentFile)}";
            });

            var result = await _scanner.ScanAsync(
                _settings.MusicFolders, removeMissing: true, progress, _scanCancellation.Token);

            LoadLibraryFromDatabase();

            StatusMessage =
                $"Xong: {result.Imported} bài mới, {result.Unchanged} không đổi, " +
                $"{result.Failed} lỗi, {result.Removed} đã gỡ · {result.Elapsed.TotalSeconds:0.0}s";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Đã huỷ quét.";
        }
        catch (Exception error)
        {
            StatusMessage = $"Quét thất bại: {error.Message}";
        }
        finally
        {
            IsScanning = false;
        }
    }

    // ── Nội bộ ────────────────────────────────────────────────────────────

    private void LoadLibraryFromDatabase()
    {
        _allTracks = _database.GetAll();
        ApplyFilter();
        OnPropertyChanged(nameof(HasLibrary));
    }

    private void ApplyFilter()
    {
        var query = SearchText?.Trim();

        var filtered = string.IsNullOrEmpty(query)
            ? _allTracks
            : _allTracks.Where(t =>
                  t.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                  (t.Artist?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false) ||
                  (t.Album?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false))
              .ToList();

        VisibleTracks.Clear();
        foreach (var track in filtered) VisibleTracks.Add(track);
    }

    private void HandleCurrentTrackChanged(Track? track)
    {
        CurrentTrack = track;
        DurationSeconds = track?.Duration.TotalSeconds ?? 0;
        DurationText = FormatTime(track?.Duration ?? TimeSpan.Zero);
        OnPropertyChanged(nameof(FormatText));
    }

    private void HandlePlaybackStateChanged(PlaybackState state)
    {
        IsPlaying = state == PlaybackState.Playing;
        OnPropertyChanged(nameof(FormatText));
    }

    private void UpdatePlaybackProgress()
    {
        if (_playback.State is PlaybackState.Empty or PlaybackState.Stopped) return;

        _updatingPositionFromEngine = true;
        try
        {
            var position = _playback.Position;
            PositionSeconds = position.TotalSeconds;
            PositionText = FormatTime(position);

            // Engine biết độ dài chính xác hơn tag (nhất là MP3 VBR), nên khi đã
            // phát thì lấy theo engine.
            var duration = _playback.Duration;
            if (duration > TimeSpan.Zero && Math.Abs(duration.TotalSeconds - DurationSeconds) > 0.5)
            {
                DurationSeconds = duration.TotalSeconds;
                DurationText = FormatTime(duration);
            }
        }
        finally
        {
            _updatingPositionFromEngine = false;
        }
    }

    private static string FormatTime(TimeSpan time) =>
        time.TotalHours >= 1
            ? $"{(int)time.TotalHours}:{time.Minutes:00}:{time.Seconds:00}"
            : $"{time.Minutes}:{time.Seconds:00}";

    public void Dispose()
    {
        _tick.Stop();
        _scanCancellation?.Cancel();
        _scanCancellation?.Dispose();
    }
}
