using Cadence.Core.Abstractions;
using Cadence.Core.Models;

namespace Cadence.Core.Playback;

/// <summary>
/// Nối <see cref="IAudioEngine"/> với <see cref="PlaybackQueue"/>. Đây là API mà UI dùng —
/// ViewModel không bao giờ chạm trực tiếp vào engine.
///
/// VẤN ĐỀ THREAD (quan trọng): engine bắn event từ thread nội bộ của BASS, trong đó
/// <see cref="IAudioEngine.AdvancedToPreloaded"/> đến từ mixtime callback — thread đang
/// trộn audio thời gian thực. Làm bất cứ việc gì nặng ở đó (mở file để preload bài kế
/// chính là I/O đĩa) sẽ gây giật tiếng.
///
/// Nên service bắt SynchronizationContext lúc khởi tạo (là UI thread) và đẩy mọi xử lý
/// về đó. Audio thread chỉ việc post rồi quay lại trộn nhạc ngay.
/// </summary>
public sealed class PlaybackService : IDisposable
{
    private readonly IAudioEngine _engine;
    private readonly SynchronizationContext? _context;
    private bool _disposed;

    public PlaybackService(IAudioEngine engine)
    {
        _engine = engine;
        _context = SynchronizationContext.Current;

        Queue.CurrentChanged += OnQueueCurrentChanged;

        _engine.StateChanged += (_, state) => Post(() => StateChanged?.Invoke(this, state));
        _engine.ErrorOccurred += (_, error) => Post(() => ErrorOccurred?.Invoke(this, error));
        _engine.PlaybackEnded += (_, _) => Post(OnPlaybackEnded);
        _engine.AdvancedToPreloaded += (_, _) => Post(OnAdvancedToPreloaded);
    }

    public PlaybackQueue Queue { get; } = new();

    public Track? CurrentTrack => Queue.Current;
    public PlaybackState State => _engine.State;
    public TimeSpan Position => _engine.Position;
    public TimeSpan Duration => _engine.Duration;
    public AudioFormat? CurrentFormat => _engine.CurrentFormat;

    public float Volume
    {
        get => _engine.Volume;
        set => _engine.Volume = value;
    }

    public bool Shuffle
    {
        get => Queue.Shuffle;
        set
        {
            Queue.Shuffle = value;
            // Đổi shuffle làm bài kế thay đổi ⇒ preload cũ thành vô nghĩa.
            RefreshPreload();
        }
    }

    public RepeatMode RepeatMode
    {
        get => Queue.RepeatMode;
        set
        {
            Queue.RepeatMode = value;
            RefreshPreload();
        }
    }

    public event EventHandler<PlaybackState>? StateChanged;
    public event EventHandler<Track?>? CurrentTrackChanged;
    public event EventHandler<AudioEngineException>? ErrorOccurred;

    /// <summary>Nạp danh sách và phát ngay từ <paramref name="startIndex"/>.</summary>
    public void PlayList(IEnumerable<Track> tracks, int startIndex = 0)
    {
        Queue.Load(tracks, startIndex);
        PlayCurrent();
    }

    /// <summary>
    /// Nối thêm bài vào hàng đợi, không đụng tới bài đang phát.
    /// </summary>
    /// <remarks>
    /// Nếu chưa phát gì thì bài đầu tiên trong danh sách được nạp và phát luôn —
    /// nếu không, "nối vào hàng đợi rỗng" sẽ im lặng chẳng làm gì.
    /// </remarks>
    public void Append(IEnumerable<Track> tracks)
    {
        var wasEmpty = Queue.Count == 0;
        Queue.Append(tracks);

        if (wasEmpty && Queue.Current is not null) PlayCurrent();
    }

    public void TogglePlayPause()
    {
        switch (_engine.State)
        {
            case PlaybackState.Playing:
                _engine.Pause();
                break;

            case PlaybackState.Paused:
            case PlaybackState.Stopped:
                _engine.Play();
                break;

            case PlaybackState.Empty:
                // Chưa nạp gì mà user bấm play: nếu queue có sẵn nội dung thì bắt đầu.
                if (Queue.Current is not null) PlayCurrent();
                break;
        }
    }

    public void Next()
    {
        if (Queue.MoveNext(userInitiated: true)) PlayCurrent();
        else _engine.Stop();
    }

    /// <summary>
    /// Nút Previous. Đang phát quá 3 giây thì tua về đầu bài thay vì lùi bài —
    /// đây là hành vi chuẩn mà mọi player đều làm, và user cũng kỳ vọng như vậy.
    /// </summary>
    public void Previous()
    {
        if (_engine.Position > TimeSpan.FromSeconds(3))
        {
            _engine.Seek(TimeSpan.Zero);
            return;
        }

        if (Queue.MovePrevious()) PlayCurrent();
        else _engine.Seek(TimeSpan.Zero);
    }

    public void JumpTo(int trackIndex)
    {
        if (Queue.JumpTo(trackIndex)) PlayCurrent();
    }

    public void Seek(TimeSpan position) => _engine.Seek(position);

    public void Stop() => _engine.Stop();

    public int ReadSpectrum(Span<float> destination) => _engine.ReadSpectrum(destination);

    // ── Nội bộ ────────────────────────────────────────────────────────────────

    private void PlayCurrent()
    {
        var track = Queue.Current;
        if (track is null)
        {
            _engine.Stop();
            return;
        }

        try
        {
            _engine.Open(track.FilePath);
            _engine.Play();
            RefreshPreload();
        }
        catch (AudioEngineException error)
        {
            ErrorOccurred?.Invoke(this, error);

            // File hỏng không được làm treo cả queue. Nhảy sang bài kế —
            // MoveNext trả false khi hết queue nên vòng này không lặp vô hạn.
            if (Queue.MoveNext(userInitiated: true)) PlayCurrent();
        }
    }

    /// <summary>
    /// Engine tự chuyển sang bài preload rồi — nhạc mới ĐANG phát.
    /// Chỉ dịch con trỏ queue, tuyệt đối không gọi Open (sẽ cắt ngang bài vừa bắt đầu).
    /// </summary>
    private void OnAdvancedToPreloaded()
    {
        Queue.MoveNext(userInitiated: false);
        RefreshPreload();
    }

    private void OnPlaybackEnded()
    {
        // Hết bài mà không có preload nào tiếp quản. Hai khả năng:
        // hết queue thật, hoặc bài kế khác sample rate nên preload đã bị từ chối.
        if (Queue.MoveNext(userInitiated: false)) PlayCurrent();
    }

    private void OnQueueCurrentChanged(object? sender, EventArgs e) =>
        CurrentTrackChanged?.Invoke(this, Queue.Current);

    private void RefreshPreload() => _engine.PreloadNext(Queue.PeekNext()?.FilePath);

    /// <summary>Đẩy công việc về UI thread. Gọi được từ bất kỳ thread nào của BASS.</summary>
    private void Post(Action action)
    {
        if (_disposed) return;

        if (_context is null) ThreadPool.QueueUserWorkItem(_ => action());
        else _context.Post(_ => action(), null);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        Queue.CurrentChanged -= OnQueueCurrentChanged;
        _engine.Dispose();
    }
}
