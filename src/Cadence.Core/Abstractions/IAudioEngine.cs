using Cadence.Core.Models;

namespace Cadence.Core.Abstractions;

/// <summary>
/// Trừu tượng hoá engine phát nhạc.
///
/// ĐÂY LÀ RANH GIỚI QUAN TRỌNG NHẤT CỦA CODEBASE. Toàn bộ phần còn lại của app
/// chỉ biết interface này, không biết BASS tồn tại. Lý do rất thực tế:
/// BASS miễn phí cho app free nhưng PHẢI mua license (~€125+) nếu sau này bán app.
/// Giữ ranh giới này thì việc đổi sang LibVLCSharp hay NAudio chỉ là viết một
/// implementation mới, không phải đập lại cả app.
///
/// Quy ước threading: mọi method phải gọi từ UI thread. Các event thì được raise
/// từ thread nội bộ của engine — subscriber tự lo marshal về UI thread.
/// </summary>
public interface IAudioEngine : IDisposable
{
    PlaybackState State { get; }

    /// <summary>Vị trí phát hiện tại. Đọc trực tiếp từ engine, không phải giá trị cache theo timer.</summary>
    TimeSpan Position { get; }

    TimeSpan Duration { get; }

    /// <summary>Âm lượng tuyến tính 0.0–1.0. Implementation tự lo chuyển sang thang log.</summary>
    float Volume { get; set; }

    AudioFormat? CurrentFormat { get; }

    /// <summary>Chỉ đổi được khi đang Stopped/Empty — đổi lúc đang phát sẽ ném exception.</summary>
    OutputMode OutputMode { get; set; }

    event EventHandler<PlaybackState>? StateChanged;

    /// <summary>
    /// Bắn khi hết bài mà KHÔNG có bài preload nào tiếp quản — tức là hết nhạc thật sự.
    /// </summary>
    event EventHandler? PlaybackEnded;

    /// <summary>
    /// Bắn khi engine đã tự chuyển sang bài được <see cref="PreloadNext"/> nạp sẵn.
    ///
    /// Đây là hệ quả của gapless: lúc event này bắn thì bài mới ĐÃ đang phát rồi.
    /// Queue chỉ được dịch con trỏ, TUYỆT ĐỐI không gọi <see cref="Open"/> —
    /// làm vậy sẽ cắt ngang chính bài vừa bắt đầu.
    /// </summary>
    event EventHandler? AdvancedToPreloaded;

    /// <summary>Lỗi bất đồng bộ từ engine (mất thiết bị, file hỏng giữa chừng...).</summary>
    event EventHandler<AudioEngineException>? ErrorOccurred;

    /// <summary>Nạp file và chuyển sang Stopped. Không tự phát.</summary>
    /// <exception cref="AudioEngineException">File không đọc được hoặc codec không hỗ trợ.</exception>
    void Open(string filePath);

    void Play();
    void Pause();
    void Stop();
    void Seek(TimeSpan position);

    /// <summary>
    /// Báo trước bài kế tiếp để engine nạp sẵn, phục vụ gapless playback.
    /// Truyền null để huỷ preload (ví dụ user vừa đổi queue).
    /// Đây là gợi ý, không phải lệnh — engine được phép bỏ qua.
    /// </summary>
    void PreloadNext(string? filePath);

    /// <summary>
    /// Đọc dữ liệu FFT cho visualizer. Ghi vào <paramref name="destination"/> các giá trị
    /// đã chuẩn hoá 0.0–1.0 và trả về số phần tử đã ghi.
    ///
    /// Dùng Span để không cấp phát gì cả — hàm này bị gọi 60 lần/giây, mọi allocation
    /// đều thành rác cho GC.
    /// </summary>
    int ReadSpectrum(Span<float> destination);

    IReadOnlyList<AudioDevice> GetOutputDevices();
}

/// <param name="Id">Định danh nội bộ của engine, không bền vững giữa các lần chạy.</param>
public sealed record AudioDevice(int Id, string Name, bool IsDefault, bool IsEnabled);

public sealed class AudioEngineException : Exception
{
    public AudioEngineException(string message) : base(message) { }
    public AudioEngineException(string message, Exception inner) : base(message, inner) { }
}
