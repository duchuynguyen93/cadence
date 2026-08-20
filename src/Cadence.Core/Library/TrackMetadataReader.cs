using System.Security.Cryptography;
using Cadence.Core.Models;

namespace Cadence.Core.Library;

/// <summary>
/// Đọc tag từ file nhạc bằng TagLib# và trích ảnh bìa ra cache trên đĩa.
///
/// Ảnh bìa được lưu ra file thay vì giữ trong RAM vì một thư viện 10.000 bài
/// với ảnh 500KB mỗi bài là 5GB — không thể giữ trong bộ nhớ. Nhiều track cùng
/// album cho ra cùng một hash nên chỉ tốn một file ảnh duy nhất.
/// </summary>
public sealed class TrackMetadataReader
{
    /// <summary>
    /// Phần mở rộng file được coi là nhạc. Đây là danh sách BASS (kèm plugin FLAC) đọc được.
    /// Lưu ý: .opus/.ape/.wv/.dsf cần plugin BASS riêng chưa được bundle — hiện tại
    /// scanner vẫn index chúng nhưng phát sẽ lỗi. Xem README phần "Codec chưa hỗ trợ".
    /// </summary>
    public static readonly string[] SupportedExtensions =
    [
        ".mp3", ".flac", ".m4a", ".mp4", ".aac", ".ogg", ".oga",
        ".wav", ".aiff", ".aif", ".wma",
    ];

    private static readonly HashSet<string> ExtensionLookup =
        new(SupportedExtensions, StringComparer.OrdinalIgnoreCase);

    private readonly string _artworkCacheDir;

    public TrackMetadataReader(string artworkCacheDir)
    {
        _artworkCacheDir = artworkCacheDir;
        Directory.CreateDirectory(_artworkCacheDir);
    }

    public static bool IsSupportedFile(string path) =>
        ExtensionLookup.Contains(Path.GetExtension(path));

    /// <summary>
    /// Đọc metadata của một file. Trả về null nếu file hỏng hoặc không đọc được tag —
    /// scanner sẽ bỏ qua chứ không làm sập cả lượt quét.
    /// </summary>
    public Track? Read(string filePath)
    {
        try
        {
            var fileInfo = new FileInfo(filePath);
            if (!fileInfo.Exists) return null;

            using var tagFile = TagLib.File.Create(filePath);
            var tag = tagFile.Tag;
            var props = tagFile.Properties;

            // Title rỗng là chuyện thường với file rip ẩu — fallback về tên file
            // để user vẫn thấy được thứ gì đó có nghĩa.
            var title = string.IsNullOrWhiteSpace(tag.Title)
                ? Path.GetFileNameWithoutExtension(filePath)
                : tag.Title.Trim();

            return new Track
            {
                FilePath = filePath,
                Title = title,
                Artist = FirstNonEmpty(tag.Performers),
                AlbumArtist = FirstNonEmpty(tag.AlbumArtists),
                Album = NullIfBlank(tag.Album),
                TrackNumber = tag.Track,
                DiscNumber = tag.Disc,
                Year = tag.Year,
                Genre = FirstNonEmpty(tag.Genres),
                Duration = props?.Duration ?? TimeSpan.Zero,
                Bitrate = props?.AudioBitrate ?? 0,
                SampleRate = props?.AudioSampleRate ?? 0,
                Channels = props?.AudioChannels ?? 0,
                Codec = DescribeCodec(props),
                FileSize = fileInfo.Length,
                FileModifiedUtc = fileInfo.LastWriteTimeUtc,
                DateAddedUtc = DateTime.UtcNow,
                // Bọc riêng: ảnh bìa là thứ trang trí. Không lấy được ảnh thì bài hát
                // vẫn phải vào thư viện. Trước đây lỗi ở đây rơi xuống catch bên dưới
                // và làm MẤT LUÔN bài hát — xem chú thích trong TryExtractArtwork.
                ArtworkHash = TryExtractArtwork(tagFile, filePath),
            };
        }
        catch (Exception)
        {
            // TagLib ném đủ loại exception với file hỏng (CorruptFileException,
            // UnsupportedFormatException, IOException...). Với scanner thì phản ứng
            // đúng luôn là bỏ qua file đó và đi tiếp.
            return null;
        }
    }

    /// <summary>
    /// Tên file ảnh bìa hay gặp khi rip đĩa hoặc tải album về.
    /// Xếp theo thứ tự ưu tiên: "cover" là quy ước phổ biến nhất, "folder" là của
    /// Windows Media Player, "front" hay thấy trong bản rip từ đĩa vật lý.
    /// </summary>
    private static readonly string[] CoverFileNames =
        ["cover", "folder", "front", "album", "albumart", "artwork"];

    private static readonly string[] CoverExtensions = [".jpg", ".jpeg", ".png", ".webp"];

    /// <summary>
    /// Ghi ảnh bìa ra cache, trả về hash làm khoá. Null nếu không tìm được ảnh nào.
    ///
    /// Ưu tiên ảnh nhúng trong file nhạc; không có thì tìm file ảnh nằm cùng thư mục.
    /// Thứ tự này quan trọng: album tuyển tập có ảnh riêng cho từng bài, nếu lấy
    /// cover.jpg của thư mục trước thì mọi bài sẽ dùng chung một ảnh sai.
    /// </summary>
    /// <summary>
    /// Bọc <see cref="ExtractArtwork"/> để mọi lỗi ảnh bìa chỉ làm mất cái ảnh,
    /// không làm mất bài hát.
    /// </summary>
    private string? TryExtractArtwork(TagLib.File tagFile, string filePath)
    {
        try
        {
            return ExtractArtwork(tagFile, filePath);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private string? ExtractArtwork(TagLib.File tagFile, string filePath)
    {
        var data = ReadEmbeddedArtwork(tagFile) ?? ReadFolderArtwork(filePath);
        if (data is null || data.Length == 0) return null;

        var hash = Convert.ToHexStringLower(SHA256.HashData(data));
        var target = Path.Combine(_artworkCacheDir, hash + ".img");

        // Album 12 bài thì bài đầu tiên ghi file, 11 bài sau chỉ tính hash rồi thoát.
        if (File.Exists(target)) return hash;

        // Tên file tạm PHẢI duy nhất cho mỗi lần gọi.
        //
        // Scanner đọc metadata song song nhiều thread, và mọi bài trong cùng một album
        // cho ra CÙNG một hash ảnh. Dùng tên tạm cố định "{hash}.tmp" thì các thread
        // giẫm lên nhau: một thread thắng, số còn lại ném IOException. Trước khi sửa,
        // lỗi đó bị catch ở tầng trên và loại luôn bài hát khỏi thư viện — album 12 bài
        // quét xong chỉ còn đúng 1 bài, mà không có thông báo lỗi nào.
        var temp = Path.Combine(_artworkCacheDir, $"{hash}.{Guid.NewGuid():N}.tmp");

        try
        {
            File.WriteAllBytes(temp, data);

            // overwrite: false — nếu thread khác vừa ghi xong thì để nguyên bản của họ.
            // Nội dung giống hệt nhau (cùng hash) nên ai thắng cũng không quan trọng.
            File.Move(temp, target, overwrite: false);
        }
        catch (IOException)
        {
            // Thua cuộc đua: thread khác đã tạo file đích. Ảnh vẫn có, hash vẫn đúng.
        }
        finally
        {
            if (File.Exists(temp))
            {
                try { File.Delete(temp); } catch (IOException) { /* dọn dẹp, không quan trọng */ }
            }
        }

        return hash;
    }

    private static byte[]? ReadEmbeddedArtwork(TagLib.File tagFile)
    {
        var pictures = tagFile.Tag.Pictures;
        if (pictures is null || pictures.Length == 0) return null;

        // Ưu tiên ảnh được đánh dấu là bìa trước; nhiều file nhúng cả ảnh nghệ sĩ,
        // ảnh mặt sau đĩa, ảnh booklet — lấy bừa cái đầu tiên dễ ra ảnh không phải bìa.
        var cover = pictures.FirstOrDefault(p => p.Type == TagLib.PictureType.FrontCover)
                    ?? pictures[0];

        return cover.Data?.Data;
    }

    private static byte[]? ReadFolderArtwork(string filePath)
    {
        var folder = Path.GetDirectoryName(filePath);
        if (string.IsNullOrEmpty(folder)) return null;

        foreach (var name in CoverFileNames)
        {
            foreach (var extension in CoverExtensions)
            {
                var candidate = Path.Combine(folder, name + extension);
                if (!File.Exists(candidate)) continue;

                try
                {
                    // Chặn ảnh quá lớn: một số bản rip nhét ảnh scan 8000px vào thư mục.
                    // Đọc nguyên vào RAM rồi hash sẽ ngốn bộ nhớ mà chẳng để làm gì,
                    // vì UI chỉ hiển thị ở cỡ vài chục pixel.
                    var info = new FileInfo(candidate);
                    if (info.Length > 12 * 1024 * 1024) continue;

                    return File.ReadAllBytes(candidate);
                }
                catch (IOException)
                {
                    // File bị khoá hoặc ổ mạng rớt — thử ứng viên kế tiếp.
                }
            }
        }

        return null;
    }

    public string GetArtworkPath(string hash) => Path.Combine(_artworkCacheDir, hash + ".img");

    private static string? FirstNonEmpty(string[]? values) =>
        values?.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim();

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string DescribeCodec(TagLib.Properties? props)
    {
        var description = props?.Description;
        if (string.IsNullOrWhiteSpace(description)) return "Unknown";

        // TagLib trả về chuỗi kiểu "MPEG Version 1 Audio, Layer 3" — quá dài cho UI.
        // Lấy token đầu tiên là đủ nhận diện.
        return description.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
    }
}
