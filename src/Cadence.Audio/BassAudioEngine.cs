using Cadence.Core.Abstractions;
using Cadence.Core.Models;
using ManagedBass;
using ManagedBass.Mix;
using CorePlaybackState = Cadence.Core.Models.PlaybackState;

namespace Cadence.Audio;

/// <summary>
/// Engine phát nhạc dựa trên BASS (un4seen).
///
/// KIẾN TRÚC GAPLESS — đây là lý do code phức tạp hơn "mở file rồi play":
///
///   file .flac ──> decode stream (BassFlags.Decode, không tự phát ra loa)
///                        │
///                        ├──> mixer stream (đây mới là cái phát ra loa)
///                        │
///   file kế ────> decode stream đã preload, chờ sẵn
///
/// Khi decode stream hiện tại hết dữ liệu, BASS bắn sync ở chế độ "mixtime" —
/// tức là callback chạy TRONG lúc mixer đang trộn, chứ không phải sau đó. Ta cắm
/// stream kế vào mixer ngay tại thời điểm ấy, nên không có một sample im lặng nào
/// ở giữa. Đó là khác biệt giữa gapless thật và "chuyển bài nhanh".
///
/// GIỚI HẠN ĐÃ BIẾT:
/// - Mixer có sample rate cố định. Nếu bài kế khác sample rate (44.1k → 96k), ta buộc
///   phải dựng lại mixer, và lần chuyển bài đó KHÔNG gapless. Không tránh được —
///   sound card cũng phải đổi chế độ.
/// - <see cref="OutputMode.Exclusive"/> chưa làm (cần BASSWASAPI, là đường xuất khác hẳn).
/// </summary>
public sealed class BassAudioEngine : IAudioEngine
{
    private readonly Lock _gate = new();

    /// <summary>Stream phát ra loa. 0 = chưa tạo.</summary>
    private int _mixer;

    /// <summary>Decode stream của bài đang phát. 0 = chưa có.</summary>
    private int _current;

    /// <summary>Decode stream đã nạp sẵn cho bài kế, chưa cắm vào mixer. 0 = chưa có.</summary>
    private int _preloaded;

    private int _mixerRate;
    private int _mixerChannels;

    private CorePlaybackState _state = CorePlaybackState.Empty;
    private float _volume = 1.0f;
    private OutputMode _outputMode = OutputMode.Shared;
    private bool _disposed;

    /// <summary>
    /// PHẢI giữ tham chiếu tới delegate này ở field.
    ///
    /// BASS lưu con trỏ hàm ở tầng native — GC của .NET không nhìn thấy tham chiếu đó.
    /// Nếu chỉ truyền lambda vào ChannelSetSync mà không giữ lại, GC sẽ thu hồi delegate
    /// và app crash ngẫu nhiên khi BASS gọi vào vùng nhớ đã chết. Đây là bug kinh điển
    /// khi dùng BASS từ .NET và cực khó debug vì nó xảy ra không đều.
    /// </summary>
    private readonly SyncProcedure _endSyncProcedure;

    /// <summary>Buffer FFT dùng lại giữa các lần gọi — tránh cấp phát 60 lần/giây.</summary>
    private readonly float[] _fftBuffer = new float[1024];

    public BassAudioEngine()
    {
        BassNativeLoader.EnsureLoaded();
        _endSyncProcedure = OnStreamEnded;
    }

    public CorePlaybackState State
    {
        get { lock (_gate) return _state; }
    }

    public AudioFormat? CurrentFormat { get; private set; }

    public event EventHandler<CorePlaybackState>? StateChanged;
    public event EventHandler? PlaybackEnded;
    public event EventHandler? AdvancedToPreloaded;
    public event EventHandler<AudioEngineException>? ErrorOccurred;

    public TimeSpan Position
    {
        get
        {
            lock (_gate)
            {
                if (_current == 0) return TimeSpan.Zero;

                // BassMix.ChannelGetPosition chứ KHÔNG phải Bass.ChannelGetPosition:
                // decode stream luôn chạy trước tiếng ra loa một đoạn bằng buffer của
                // mixer. Dùng hàm của Bass sẽ khiến thanh progress chạy sớm ~1 giây.
                var bytes = BassMix.ChannelGetPosition(_current, PositionFlags.Bytes);
                if (bytes < 0) return TimeSpan.Zero;

                var seconds = Bass.ChannelBytes2Seconds(_current, bytes);
                return seconds < 0 ? TimeSpan.Zero : TimeSpan.FromSeconds(seconds);
            }
        }
    }

    public TimeSpan Duration
    {
        get
        {
            lock (_gate)
            {
                if (_current == 0) return TimeSpan.Zero;

                var bytes = Bass.ChannelGetLength(_current, PositionFlags.Bytes);
                if (bytes < 0) return TimeSpan.Zero;

                var seconds = Bass.ChannelBytes2Seconds(_current, bytes);
                return seconds < 0 ? TimeSpan.Zero : TimeSpan.FromSeconds(seconds);
            }
        }
    }

    public float Volume
    {
        get => _volume;
        set
        {
            _volume = Math.Clamp(value, 0f, 1f);
            lock (_gate)
            {
                if (_mixer != 0) ApplyVolume();
            }
        }
    }

    public OutputMode OutputMode
    {
        get => _outputMode;
        set
        {
            if (_outputMode == value) return;

            if (value == OutputMode.Exclusive)
            {
                throw new NotSupportedException(
                    "WASAPI exclusive mode chưa được hiện thực. Cần chuyển đường xuất " +
                    "sang BASSWASAPI thay vì Bass.Init — xem README, mục lộ trình.");
            }

            lock (_gate)
            {
                if (_state is CorePlaybackState.Playing or CorePlaybackState.Paused)
                    throw new InvalidOperationException("Không đổi được output mode khi đang phát.");

                _outputMode = value;
            }
        }
    }

    public void Open(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ObjectDisposedException.ThrowIf(_disposed, this);

        lock (_gate)
        {
            SetState(CorePlaybackState.Loading);

            // Bài mới do người dùng chọn ⇒ preload cũ chắc chắn sai, bỏ đi.
            FreePreloadedLocked();
            FreeCurrentLocked();

            var stream = CreateDecodeStream(filePath);
            AttachToMixerLocked(stream);

            _current = stream;
            CurrentFormat = DescribeStream(stream);

            SetState(CorePlaybackState.Stopped);
        }
    }

    public void Play()
    {
        lock (_gate)
        {
            if (_current == 0 || _mixer == 0) return;

            // Tham số thứ hai = false: tiếp tục từ vị trí hiện tại thay vì tua về đầu,
            // nhờ vậy Play() sau Pause() nghe tiếp đúng chỗ đang dở.
            if (!Bass.ChannelPlay(_mixer, false))
            {
                RaiseError($"Không phát được (lỗi BASS: {Bass.LastError}).");
                return;
            }

            SetState(CorePlaybackState.Playing);
        }
    }

    public void Pause()
    {
        lock (_gate)
        {
            if (_mixer == 0 || _state != CorePlaybackState.Playing) return;

            Bass.ChannelPause(_mixer);
            SetState(CorePlaybackState.Paused);
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            if (_mixer == 0) return;

            Bass.ChannelStop(_mixer);

            // Tua source về 0 để lần Play() sau bắt đầu lại từ đầu bài,
            // đúng ngữ nghĩa Stop (khác Pause).
            if (_current != 0) BassMix.ChannelSetPosition(_current, 0, PositionFlags.Bytes);

            SetState(CorePlaybackState.Stopped);
        }
    }

    public void Seek(TimeSpan position)
    {
        lock (_gate)
        {
            if (_current == 0) return;

            var bytes = Bass.ChannelSeconds2Bytes(_current, position.TotalSeconds);
            if (bytes < 0) return;

            BassMix.ChannelSetPosition(_current, bytes, PositionFlags.Bytes);

            // Tua source thôi là CHƯA ĐỦ. Mixer vẫn đang giữ vài trăm ms audio đã trộn
            // từ trước lúc seek, nên người dùng sẽ nghe tiếp đoạn cũ rồi mới nhảy —
            // và thanh progress cũng báo sai chừng đó thời gian.
            //
            // Set position 0 trên chính mixer là cách BASS quy định để xả buffer đó.
            // Mixer là stream "non-stop" không có độ dài thật nên thao tác này không
            // tua nhạc về đầu, nó chỉ vứt phần đã trộn sẵn đi.
            Bass.ChannelSetPosition(_mixer, 0, PositionFlags.Bytes);
        }
    }

    public void PreloadNext(string? filePath)
    {
        lock (_gate)
        {
            FreePreloadedLocked();

            if (string.IsNullOrWhiteSpace(filePath)) return;

            try
            {
                var stream = CreateDecodeStream(filePath);
                var info = Bass.ChannelGetInfo(stream);

                // Khác sample rate/số kênh thì mixer hiện tại không nhận được.
                // Bỏ preload — lúc chuyển bài sẽ đi đường Open() bình thường (có gap).
                if (info.Frequency != _mixerRate || info.Channels != _mixerChannels)
                {
                    Bass.StreamFree(stream);
                    return;
                }

                _preloaded = stream;
            }
            catch (AudioEngineException)
            {
                // Preload chỉ là tối ưu. File kế hỏng thì im lặng bỏ qua ở đây —
                // lỗi sẽ được báo tử tế lúc thật sự tới lượt nó phát.
            }
        }
    }

    public int ReadSpectrum(Span<float> destination)
    {
        lock (_gate)
        {
            if (_mixer == 0 || _state != CorePlaybackState.Playing) return 0;

            // Lấy FFT từ mixer chứ không từ source: mixer là tín hiệu thật sự ra loa,
            // đã gồm cả volume và mọi thứ trộn vào.
            var read = Bass.ChannelGetData(_mixer, _fftBuffer, (int)DataFlags.FFT2048);
            if (read <= 0) return 0;

            // FFT2048 trả về 1024 bin biên độ.
            var count = Math.Min(destination.Length, 1024);
            for (var i = 0; i < count; i++) destination[i] = Math.Clamp(_fftBuffer[i], 0f, 1f);

            return count;
        }
    }

    public IReadOnlyList<AudioDevice> GetOutputDevices()
    {
        var devices = new List<AudioDevice>();

        // Device 0 là "no sound" của BASS, bỏ qua. Bắt đầu từ 1.
        for (var i = 1; ; i++)
        {
            if (!Bass.GetDeviceInfo(i, out var info)) break;
            devices.Add(new AudioDevice(i, info.Name, info.IsDefault, info.IsEnabled));
        }

        return devices;
    }

    // ── Nội bộ ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Tạo decode stream. Decode nghĩa là BASS giải mã theo yêu cầu chứ không tự đẩy ra loa —
    /// bắt buộc phải vậy thì mới cắm được vào mixer.
    /// </summary>
    private static int CreateDecodeStream(string filePath)
    {
        if (!File.Exists(filePath))
            throw new AudioEngineException($"Không tìm thấy file: {filePath}");

        // Prescan quét trước toàn file để lấy độ dài chính xác với MP3 VBR.
        // Không có nó thì thanh progress của file VBR sẽ sai lệch thấy rõ.
        const BassFlags flags = BassFlags.Decode | BassFlags.Float | BassFlags.Prescan;

        var stream = Bass.CreateStream(filePath, 0, 0, flags);
        if (stream == 0)
        {
            var error = Bass.LastError;
            var hint = error == Errors.FileFormat
                ? " Codec này có thể cần plugin BASS chưa được bundle (.opus, .ape, .wv...)."
                : string.Empty;

            throw new AudioEngineException(
                $"Không mở được '{Path.GetFileName(filePath)}' (lỗi BASS: {error}).{hint}");
        }

        return stream;
    }

    /// <summary>Dựng mixer nếu chưa có (hoặc format đã đổi) rồi cắm stream vào.</summary>
    private void AttachToMixerLocked(int stream)
    {
        var info = Bass.ChannelGetInfo(stream);

        if (_mixer == 0 || info.Frequency != _mixerRate || info.Channels != _mixerChannels)
        {
            RecreateMixerLocked(info.Frequency, info.Channels);
        }

        // MixerChanNoRampin: mặc định BASSmix fade-in ~mấy chục ms mỗi khi có channel mới.
        // Với gapless thì cái fade đó chính là thứ ta đang cố loại bỏ.
        if (!BassMix.MixerAddChannel(_mixer, stream, BassFlags.MixerChanNoRampin))
        {
            Bass.StreamFree(stream);
            throw new AudioEngineException($"Không cắm được stream vào mixer (lỗi BASS: {Bass.LastError}).");
        }

        // SyncFlags.Mixtime là mấu chốt: callback chạy ngay trong lúc mixer trộn tới
        // điểm kết thúc, chứ không phải sau khi tiếng đã ra loa. Nhờ vậy stream kế
        // được cắm vào đúng sample tiếp theo.
        BassMix.ChannelSetSync(stream, SyncFlags.End | SyncFlags.Mixtime, 0, _endSyncProcedure, IntPtr.Zero);
    }

    private void RecreateMixerLocked(int frequency, int channels)
    {
        if (_mixer != 0)
        {
            Bass.StreamFree(_mixer);
            _mixer = 0;
        }

        // MixerNonStop: mixer tiếp tục sinh dữ liệu (im lặng) khi không còn source nào.
        // Thiếu cờ này thì mixer tự dừng lúc hết bài, và stream preload cắm vào sau đó
        // sẽ không phát — gapless hỏng.
        var mixer = BassMix.CreateMixerStream(
            frequency, channels, BassFlags.MixerNonStop | BassFlags.Float);

        if (mixer == 0)
            throw new AudioEngineException($"Không tạo được mixer (lỗi BASS: {Bass.LastError}).");

        _mixer = mixer;
        _mixerRate = frequency;
        _mixerChannels = channels;
        ApplyVolume();
    }

    /// <summary>
    /// Chạy trên thread của BASS khi decode stream hết dữ liệu.
    ///
    /// CẢNH BÁO: đây là mixtime sync — nó chạy bên trong vòng trộn của BASS.
    /// Chỉ được làm những việc cực ngắn. Không I/O, không đụng UI, không chờ lock lâu.
    /// </summary>
    private void OnStreamEnded(int handle, int channel, int data, IntPtr user)
    {
        var advanced = false;

        lock (_gate)
        {
            if (channel != _current) return;

            var finished = _current;
            _current = 0;

            if (_preloaded != 0)
            {
                var next = _preloaded;
                _preloaded = 0;

                if (BassMix.MixerAddChannel(_mixer, next, BassFlags.MixerChanNoRampin))
                {
                    BassMix.ChannelSetSync(next, SyncFlags.End | SyncFlags.Mixtime, 0,
                        _endSyncProcedure, IntPtr.Zero);

                    _current = next;
                    CurrentFormat = DescribeStream(next);
                    advanced = true;
                }
                else
                {
                    Bass.StreamFree(next);
                }
            }

            BassMix.MixerRemoveChannel(finished);
            Bass.StreamFree(finished);

            if (!advanced)
            {
                CurrentFormat = null;
                SetState(CorePlaybackState.Stopped);
            }
        }

        // Bắn event NGOÀI lock: subscriber sẽ marshal về UI thread, mà giữ lock của
        // mixtime sync trong lúc đó thì có nguy cơ deadlock và làm nghẽn audio thread.
        if (advanced) AdvancedToPreloaded?.Invoke(this, EventArgs.Empty);
        else PlaybackEnded?.Invoke(this, EventArgs.Empty);
    }

    private static AudioFormat DescribeStream(int stream)
    {
        var info = Bass.ChannelGetInfo(stream);

        // ChannelInfo.Resolution cho biết độ sâu bit của DỮ LIỆU ĐÃ GIẢI MÃ. Ta luôn
        // yêu cầu Float nên nó sẽ là 32-bit float, không phản ánh bit depth gốc của file.
        // Bit depth thật lấy từ TagLib lúc scan; ở đây chỉ mô tả dòng đang chạy.
        var bits = info.Resolution switch
        {
            Resolution.Byte => 8,
            Resolution.Short => 16,
            _ => 32,
        };

        return new AudioFormat(info.Frequency, info.Channels, bits, info.ChannelType.ToString());
    }

    /// <summary>
    /// Đổi âm lượng tuyến tính sang thang cảm nhận.
    ///
    /// Tai người nghe theo log: kéo slider từ 1.0 xuống 0.5 mà đặt thẳng vào BASS
    /// thì nghe chỉ nhỏ đi một chút, không giống "một nửa". Luỹ thừa 3 là xấp xỉ
    /// đơn giản và đủ tự nhiên cho slider âm lượng.
    /// </summary>
    private void ApplyVolume()
    {
        var perceptual = MathF.Pow(_volume, 3f);
        Bass.ChannelSetAttribute(_mixer, ChannelAttribute.Volume, perceptual);
    }

    private void SetState(CorePlaybackState state)
    {
        if (_state == state) return;
        _state = state;
        StateChanged?.Invoke(this, state);
    }

    private void RaiseError(string message) =>
        ErrorOccurred?.Invoke(this, new AudioEngineException(message));

    private void FreeCurrentLocked()
    {
        if (_current == 0) return;

        BassMix.MixerRemoveChannel(_current);
        Bass.StreamFree(_current);
        _current = 0;
        CurrentFormat = null;
    }

    private void FreePreloadedLocked()
    {
        if (_preloaded == 0) return;

        // Chưa từng cắm vào mixer nên chỉ cần free, không cần MixerRemoveChannel.
        Bass.StreamFree(_preloaded);
        _preloaded = 0;
    }

    public void Dispose()
    {
        if (_disposed) return;

        lock (_gate)
        {
            _disposed = true;

            FreePreloadedLocked();
            FreeCurrentLocked();

            if (_mixer != 0)
            {
                Bass.ChannelStop(_mixer);
                Bass.StreamFree(_mixer);
                _mixer = 0;
            }

            _state = CorePlaybackState.Empty;
        }
    }
}
