using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;

namespace Cadence.App.Services;

/// <summary>
/// Nạp ảnh bìa từ cache trên đĩa thành <see cref="Bitmap"/> để hiển thị.
///
/// Hai thứ ở đây quyết định app có dùng được với thư viện lớn hay không:
///
/// 1. GIẢI MÃ THEO ĐÚNG CỠ CẦN DÙNG. Ảnh bìa ngày nay thường 1500×1500 hoặc hơn.
///    Giải mã nguyên cỡ để vẽ ô 28px là tốn 9MB RAM cho mỗi bài. Một danh sách
///    500 bài nhìn thấy trên màn hình sẽ ngốn vài GB rồi chết. DecodeToWidth cho
///    Skia hạ mẫu ngay lúc giải mã nên chỉ tốn vài KB mỗi ảnh.
///
/// 2. CACHE CÓ GIỚI HẠN (LRU). Không có nó thì cuộn hết thư viện một lượt là giữ lại
///    toàn bộ ảnh trong RAM. LRU giữ phần đang nhìn thấy và bỏ phần đã cuộn qua.
/// </summary>
public sealed class ArtworkService
{
    private sealed record CacheKey(string Hash, int Width);

    private readonly string _cacheDirectory;
    private readonly int _maxEntries;

    private readonly Lock _gate = new();
    private readonly Dictionary<CacheKey, LinkedListNode<(CacheKey Key, Bitmap Image)>> _index = [];
    private readonly LinkedList<(CacheKey Key, Bitmap Image)> _recency = new();

    /// <summary>
    /// Các yêu cầu đang chạy dở. Cuộn nhanh sẽ hỏi cùng một ảnh nhiều lần trước khi
    /// lần đọc đầu tiên xong; gộp lại để không đọc đĩa và giải mã trùng lặp.
    /// </summary>
    private readonly Dictionary<CacheKey, Task<Bitmap?>> _inFlight = [];

    public ArtworkService(string cacheDirectory, int maxEntries = 300)
    {
        _cacheDirectory = cacheDirectory;
        _maxEntries = maxEntries;
    }

    /// <summary>
    /// Lấy ảnh bìa theo hash, đã hạ mẫu về <paramref name="pixelWidth"/>.
    /// Trả về null nếu track không có ảnh hoặc file cache đã mất.
    /// </summary>
    public Task<Bitmap?> GetAsync(string? hash, int pixelWidth)
    {
        if (string.IsNullOrEmpty(hash)) return Task.FromResult<Bitmap?>(null);

        var key = new CacheKey(hash, pixelWidth);

        lock (_gate)
        {
            if (_index.TryGetValue(key, out var node))
            {
                Touch(node);
                return Task.FromResult<Bitmap?>(node.Value.Image);
            }

            if (_inFlight.TryGetValue(key, out var pending)) return pending;

            var task = LoadAsync(key);
            _inFlight[key] = task;
            return task;
        }
    }

    private async Task<Bitmap?> LoadAsync(CacheKey key)
    {
        try
        {
            var path = Path.Combine(_cacheDirectory, key.Hash + ".img");

            // Task.Run để đọc đĩa và giải mã chạy ngoài UI thread. Giải mã JPEG lớn
            // mất vài chục ms — làm trên UI thread là danh sách giật thấy rõ khi cuộn.
            var bitmap = await Task.Run(() =>
            {
                try
                {
                    if (!File.Exists(path)) return null;

                    using var stream = File.OpenRead(path);
                    return Bitmap.DecodeToWidth(stream, key.Width);
                }
                catch (Exception)
                {
                    // File ảnh hỏng, định dạng lạ, hoặc bị xoá giữa chừng.
                    // Không có bìa thì hiện ô trống — không đáng để ném lỗi lên UI.
                    return null;
                }
            });

            if (bitmap is not null) Store(key, bitmap);
            return bitmap;
        }
        finally
        {
            lock (_gate) _inFlight.Remove(key);
        }
    }

    private void Store(CacheKey key, Bitmap bitmap)
    {
        lock (_gate)
        {
            if (_index.ContainsKey(key)) return;

            var node = _recency.AddFirst((key, bitmap));
            _index[key] = node;

            while (_recency.Count > _maxEntries)
            {
                var oldest = _recency.Last!;
                _recency.RemoveLast();
                _index.Remove(oldest.Value.Key);

                // CỐ Ý KHÔNG gọi Dispose() ở đây.
                //
                // Bitmap bị đẩy ra khỏi cache vẫn có thể đang được một Image trên màn
                // hình tham chiếu (ví dụ ảnh của bài đang phát ở thanh dưới, nằm yên
                // trong khi người dùng cuộn qua hàng trăm hàng khác). Dispose lúc đó
                // sẽ giải phóng bộ nhớ Skia đang được vẽ — ô ảnh biến thành trống
                // hoặc app crash. Để GC thu hồi khi thật sự không còn ai giữ.
            }
        }
    }

    private void Touch(LinkedListNode<(CacheKey Key, Bitmap Image)> node)
    {
        _recency.Remove(node);
        _recency.AddFirst(node);
    }
}
