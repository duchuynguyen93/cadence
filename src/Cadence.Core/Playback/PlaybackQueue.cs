using Cadence.Core.Models;

namespace Cadence.Core.Playback;

/// <summary>
/// Hàng đợi phát nhạc: thứ tự, shuffle, repeat.
///
/// Thiết kế: danh sách bài (<c>_tracks</c>) và thứ tự phát (<c>_order</c>) tách rời nhau.
/// <c>_order</c> chứa các chỉ số trỏ vào <c>_tracks</c>. Bật shuffle chỉ hoán vị <c>_order</c>,
/// không đụng tới <c>_tracks</c> — nhờ vậy tắt shuffle là quay lại đúng thứ tự gốc,
/// và UI vẫn hiển thị queue theo thứ tự người dùng thấy quen.
/// </summary>
public sealed class PlaybackQueue
{
    private readonly List<Track> _tracks = [];
    private readonly List<int> _order = [];
    private readonly Random _random;

    public PlaybackQueue() : this(Random.Shared) { }

    /// <param name="random">
    /// Nguồn ngẫu nhiên cho shuffle. Nhận từ ngoài để test dựng lại được đúng
    /// một thứ tự đã cho — nếu không, mọi khẳng định về shuffle chỉ kiểm được
    /// tính chất chung, không kiểm được thứ tự cụ thể.
    /// </param>
    public PlaybackQueue(Random random)
    {
        ArgumentNullException.ThrowIfNull(random);
        _random = random;
    }

    /// <summary>Vị trí trong <c>_order</c>, KHÔNG phải trong <c>_tracks</c>. -1 = chưa phát gì.</summary>
    private int _cursor = -1;

    private bool _shuffle;

    public IReadOnlyList<Track> Tracks => _tracks;
    public RepeatMode RepeatMode { get; set; } = RepeatMode.Off;
    public int Count => _tracks.Count;

    public Track? Current => _cursor >= 0 && _cursor < _order.Count ? _tracks[_order[_cursor]] : null;

    /// <summary>Vị trí bài hiện tại trong <c>_tracks</c> — dùng để highlight trong UI.</summary>
    public int CurrentIndex => _cursor >= 0 && _cursor < _order.Count ? _order[_cursor] : -1;

    public event EventHandler? CurrentChanged;
    public event EventHandler? QueueChanged;

    public bool Shuffle
    {
        get => _shuffle;
        set
        {
            if (_shuffle == value) return;
            _shuffle = value;
            RebuildOrder(keepCurrent: true);
            QueueChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Nạp danh sách mới và bắt đầu từ <paramref name="startIndex"/> (chỉ số trong danh sách truyền vào).</summary>
    public void Load(IEnumerable<Track> tracks, int startIndex = 0)
    {
        _tracks.Clear();
        _tracks.AddRange(tracks);

        RebuildOrder(keepCurrent: false);

        if (_tracks.Count == 0)
        {
            _cursor = -1;
        }
        else
        {
            startIndex = Math.Clamp(startIndex, 0, _tracks.Count - 1);

            // Với shuffle, bài user bấm vào phải là bài phát đầu tiên — nên đưa nó
            // lên đầu _order thay vì để random quyết định.
            var positionInOrder = _order.IndexOf(startIndex);
            if (_shuffle && positionInOrder > 0)
            {
                _order.RemoveAt(positionInOrder);
                _order.Insert(0, startIndex);
                positionInOrder = 0;
            }

            _cursor = positionInOrder;
        }

        QueueChanged?.Invoke(this, EventArgs.Empty);
        CurrentChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Bài kế tiếp mà KHÔNG di chuyển con trỏ. Dùng cho gapless preload.
    /// Trả về null nếu đã hết queue.
    /// </summary>
    /// <remarks>
    /// Cố tình bỏ qua <see cref="RepeatMode.One"/>: lặp một bài thì engine seek về 0
    /// chứ không nạp lại file, nên không cần preload.
    /// </remarks>
    public Track? PeekNext()
    {
        if (_order.Count == 0) return null;

        if (_cursor + 1 < _order.Count) return _tracks[_order[_cursor + 1]];
        if (RepeatMode == RepeatMode.All) return _tracks[_order[0]];
        return null;
    }

    /// <summary>
    /// Chuyển sang bài kế. Trả về false nếu hết queue (caller nên dừng phát).
    /// </summary>
    /// <param name="userInitiated">
    /// True khi user bấm nút Next. Khác biệt quan trọng với RepeatMode.One:
    /// hết bài tự nhiên thì lặp lại, nhưng user bấm Next là muốn đi tiếp thật.
    /// </param>
    public bool MoveNext(bool userInitiated)
    {
        if (_order.Count == 0) return false;

        if (RepeatMode == RepeatMode.One && !userInitiated)
        {
            CurrentChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }

        if (_cursor + 1 < _order.Count)
        {
            _cursor++;
        }
        else if (RepeatMode == RepeatMode.All)
        {
            // Mỗi vòng lặp shuffle lại để nghe lần 2 không trùng thứ tự lần 1.
            if (_shuffle) RebuildOrder(keepCurrent: false);
            _cursor = 0;
        }
        else
        {
            return false;
        }

        CurrentChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>Lùi một bài. Ở đầu queue thì đứng yên (không wrap) — giống hành vi mặc định của mọi player.</summary>
    public bool MovePrevious()
    {
        if (_order.Count == 0) return false;

        if (_cursor > 0)
        {
            _cursor--;
        }
        else if (RepeatMode == RepeatMode.All)
        {
            _cursor = _order.Count - 1;
        }
        else
        {
            return false;
        }

        CurrentChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>Nhảy tới một bài cụ thể theo chỉ số trong <see cref="Tracks"/>.</summary>
    public bool JumpTo(int trackIndex)
    {
        if (trackIndex < 0 || trackIndex >= _tracks.Count) return false;

        var positionInOrder = _order.IndexOf(trackIndex);
        if (positionInOrder < 0) return false;

        _cursor = positionInOrder;
        CurrentChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public void Clear()
    {
        _tracks.Clear();
        _order.Clear();
        _cursor = -1;
        QueueChanged?.Invoke(this, EventArgs.Empty);
        CurrentChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Dựng lại <c>_order</c>. Với shuffle thì Fisher–Yates trên phần còn lại.
    /// </summary>
    /// <param name="keepCurrent">
    /// Giữ bài đang phát ở nguyên vị trí con trỏ. Cần thiết khi user bật/tắt shuffle
    /// giữa chừng — nhạc đang phát không được nhảy sang bài khác.
    /// </param>
    private void RebuildOrder(bool keepCurrent)
    {
        var currentTrackIndex = keepCurrent ? CurrentIndex : -1;

        _order.Clear();
        for (var i = 0; i < _tracks.Count; i++) _order.Add(i);

        if (_shuffle)
        {
            for (var i = _order.Count - 1; i > 0; i--)
            {
                var j = _random.Next(i + 1);
                (_order[i], _order[j]) = (_order[j], _order[i]);
            }
        }

        if (currentTrackIndex >= 0)
        {
            var position = _order.IndexOf(currentTrackIndex);
            if (position >= 0)
            {
                // Đưa bài đang phát về đúng chỗ con trỏ đang trỏ tới, thay vì
                // dịch con trỏ — như vậy các bài đã nghe vẫn nằm phía sau.
                var target = Math.Clamp(_cursor, 0, _order.Count - 1);
                (_order[position], _order[target]) = (_order[target], _order[position]);
                _cursor = target;
            }
        }
    }
}
