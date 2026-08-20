namespace Cadence.Core.Models;

public enum PlaybackState
{
    /// <summary>Chưa nạp file nào.</summary>
    Empty,
    Stopped,
    Playing,
    Paused,
    /// <summary>Đang nạp / buffer. UI nên hiện spinner ở nút play.</summary>
    Loading,
}

public enum RepeatMode
{
    Off,
    /// <summary>Hết queue thì quay lại đầu.</summary>
    All,
    /// <summary>Lặp lại đúng bài hiện tại.</summary>
    One,
}

/// <summary>
/// Cách đẩy audio ra thiết bị trên Windows.
/// </summary>
public enum OutputMode
{
    /// <summary>
    /// Đi qua mixer của Windows. Trộn được với âm thanh app khác, luôn hoạt động.
    /// Nhược điểm: Windows resample về sample rate chung → không bit-perfect.
    /// </summary>
    Shared,

    /// <summary>
    /// WASAPI exclusive — chiếm độc quyền sound card, bit-perfect, latency thấp.
    /// Đánh đổi: app khác mất tiếng, và chuyển bài giữa 2 sample rate khác nhau
    /// sẽ có tiếng "tách" vì phải mở lại device.
    /// </summary>
    Exclusive,
}

/// <summary>Định dạng dòng audio đang phát. Null khi chưa nạp file.</summary>
public sealed record AudioFormat(int SampleRate, int Channels, int Bits, string Codec)
{
    public bool IsHiRes => SampleRate > 48_000 || Bits > 16;

    public override string ToString() =>
        $"{Codec} · {SampleRate / 1000.0:0.#} kHz · {Bits}-bit · {(Channels == 2 ? "Stereo" : $"{Channels}ch")}";
}
