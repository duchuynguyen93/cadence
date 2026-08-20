namespace Cadence.Core.Models;

/// <summary>
/// Một bài hát trong thư viện. Đây là immutable record — mọi thay đổi tạo bản sao mới.
/// Lý do: track được share giữa nhiều ViewModel và thread (scanner chạy nền, UI đọc),
/// immutable thì không cần lock.
/// </summary>
public sealed record Track
{
    /// <summary>Khoá chính trong SQLite. 0 nghĩa là chưa được lưu.</summary>
    public long Id { get; init; }

    /// <summary>Đường dẫn tuyệt đối tới file. Đây là identity thật sự của track.</summary>
    public required string FilePath { get; init; }

    public required string Title { get; init; }
    public string? Artist { get; init; }

    /// <summary>
    /// Dùng để gom album. Với album tuyển tập (compilation) thì AlbumArtist khác Artist —
    /// nếu gom theo Artist thì album sẽ bị vỡ thành nhiều mảnh.
    /// </summary>
    public string? AlbumArtist { get; init; }

    public string? Album { get; init; }
    public uint TrackNumber { get; init; }
    public uint DiscNumber { get; init; }
    public uint Year { get; init; }
    public string? Genre { get; init; }

    public TimeSpan Duration { get; init; }

    // Thông tin kỹ thuật — hiển thị ở info panel và dùng để chọn output format cho WASAPI.
    public int Bitrate { get; init; }
    public int SampleRate { get; init; }
    public int Channels { get; init; }
    public string? Codec { get; init; }

    public long FileSize { get; init; }

    /// <summary>Mtime của file lúc scan. Dùng để phát hiện file đã đổi mà không cần đọc lại tag.</summary>
    public DateTime FileModifiedUtc { get; init; }

    public DateTime DateAddedUtc { get; init; }

    /// <summary>
    /// SHA-256 (dạng hex) của ảnh bìa nhúng trong file, hoặc null nếu không có.
    /// Ảnh thật nằm ở cache trên đĩa theo hash này — nhiều track cùng album chia sẻ một file ảnh
    /// thay vì mỗi track giữ một bản copy trong RAM.
    /// </summary>
    public string? ArtworkHash { get; init; }

    public string DisplayArtist => Artist ?? AlbumArtist ?? "Unknown Artist";
    public string DisplayAlbum => Album ?? "Unknown Album";
    public string DisplayAlbumArtist => AlbumArtist ?? Artist ?? "Unknown Artist";

    /// <summary>
    /// Ký tự ngăn cách hai thành phần của <see cref="AlbumKey"/>.
    ///
    /// Dùng U+001F (Unit Separator) vì nó không bao giờ có trong tên nghệ sĩ hay tên album
    /// thật. Nối thẳng không dấu ngăn sẽ gây đụng độ: nghệ sĩ "AB" + album "C" ra cùng khoá
    /// với nghệ sĩ "A" + album "BC", khiến hai album khác nhau bị gom làm một.
    ///
    /// Viết bằng escape \u001f chứ KHÔNG nhúng thẳng ký tự vào chuỗi: ký tự điều khiển là
    /// vô hình trong editor, và nó làm công cụ như `file` tưởng file nguồn này là nhị phân.
    /// </summary>
    private const string KeySeparator = "\u001f";

    /// <summary>Khoá gom album: album artist + tên album. Chuẩn hoá để tránh lệch hoa/thường.</summary>
    public string AlbumKey =>
        $"{DisplayAlbumArtist.ToLowerInvariant()}{KeySeparator}{DisplayAlbum.ToLowerInvariant()}";
}
