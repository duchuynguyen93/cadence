using Cadence.Core.Library;
using Cadence.Core.Models;
using Xunit;

namespace Cadence.Core.Tests;

/// <summary>
/// Chạy trên SQLite thật trong thư mục tạm, không mock.
///
/// Mock một database chỉ kiểm được rằng code gọi đúng method mình tự bịa ra.
/// Thứ đáng kiểm ở đây là hành vi của chính SQL: ON CONFLICT có cập nhật đúng
/// không, date_added_utc có được giữ lại không, so sánh đường dẫn có phân biệt
/// hoa thường không. Không cái nào trong số đó sống sót qua một mock.
/// </summary>
public sealed class LibraryDatabaseTests : IDisposable
{
    private readonly string _directory;
    private readonly LibraryDatabase _database;

    public LibraryDatabaseTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "cadence-tests", Guid.NewGuid().ToString("N"));
        _database = new LibraryDatabase(Path.Combine(_directory, "library.db"));
    }

    public void Dispose()
    {
        _database.Dispose();
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // Windows đôi khi còn giữ handle của file WAL một nhịp sau Dispose.
            // Rác trong thư mục tạm không đáng để làm đỏ một lượt test.
        }
    }

    private static Track WithPath(string path, string title = "Bai hat") => new()
    {
        FilePath = path,
        Title = title,
        Artist = "Nghe si",
        Album = "Album",
        Duration = TimeSpan.FromMinutes(3),
        FileModifiedUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        DateAddedUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
    };

    [Fact]
    public void Ghi_roi_doc_lai_giu_nguyen_moi_truong()
    {
        var track = WithPath("/music/a.flac") with
        {
            AlbumArtist = "Nghe si chinh",
            TrackNumber = 4,
            DiscNumber = 2,
            Year = 1997,
            Genre = "Rock",
            Bitrate = 1_024,
            SampleRate = 96_000,
            Channels = 2,
            Codec = "FLAC",
            FileSize = 42_000_000,
            ArtworkHash = "abc123",
        };

        _database.Upsert(track);
        var loaded = _database.GetAll().Single();

        Assert.Equal(track.FilePath, loaded.FilePath);
        Assert.Equal(track.AlbumArtist, loaded.AlbumArtist);
        Assert.Equal(track.TrackNumber, loaded.TrackNumber);
        Assert.Equal(track.DiscNumber, loaded.DiscNumber);
        Assert.Equal(track.Year, loaded.Year);
        Assert.Equal(track.Genre, loaded.Genre);
        Assert.Equal(track.Duration, loaded.Duration);
        Assert.Equal(track.SampleRate, loaded.SampleRate);
        Assert.Equal(track.Codec, loaded.Codec);
        Assert.Equal(track.FileSize, loaded.FileSize);
        Assert.Equal(track.ArtworkHash, loaded.ArtworkHash);
        Assert.True(loaded.Id > 0);
    }

    [Fact]
    public void Truong_null_van_di_ve_nguyen_ven()
    {
        _database.Upsert(new Track { FilePath = "/music/b.mp3", Title = "Khong tag" });
        var loaded = _database.GetAll().Single();

        Assert.Null(loaded.Artist);
        Assert.Null(loaded.Album);
        Assert.Null(loaded.Genre);
        Assert.Null(loaded.ArtworkHash);
    }

    [Fact]
    public void Upsert_cung_duong_dan_thi_cap_nhat_chu_khong_tao_ban_ghi_thu_hai()
    {
        _database.Upsert(WithPath("/music/a.flac", "Ten cu"));
        _database.Upsert(WithPath("/music/a.flac", "Ten moi"));

        var all = _database.GetAll();
        Assert.Single(all);
        Assert.Equal("Ten moi", all[0].Title);
    }

    [Fact]
    public void Quet_lai_khong_lam_moi_ngay_them_bai()
    {
        // User sắp xếp theo "mới thêm" thì một lần rescan không được đẩy cả thư
        // viện lên đầu danh sách.
        var original = WithPath("/music/a.flac");
        _database.Upsert(original);

        _database.Upsert(original with
        {
            Title = "Sua tag",
            DateAddedUtc = new DateTime(2026, 8, 25, 0, 0, 0, DateTimeKind.Utc),
        });

        Assert.Equal(original.DateAddedUtc, _database.GetAll().Single().DateAddedUtc);
    }

    [Fact]
    public void GetAll_sap_xep_theo_thu_tu_dia_va_so_bai()
    {
        _database.UpsertBatch(
        [
            WithPath("/music/3.flac", "Ba") with { AlbumArtist = "X", DiscNumber = 1, TrackNumber = 3 },
            WithPath("/music/1.flac", "Mot") with { AlbumArtist = "X", DiscNumber = 1, TrackNumber = 1 },
            WithPath("/music/4.flac", "Bon") with { AlbumArtist = "X", DiscNumber = 2, TrackNumber = 1 },
            WithPath("/music/2.flac", "Hai") with { AlbumArtist = "X", DiscNumber = 1, TrackNumber = 2 },
        ]);

        Assert.Equal(["Mot", "Hai", "Ba", "Bon"], _database.GetAll().Select(t => t.Title));
    }

    [Fact]
    public void GetPathTimestamps_tra_ve_dung_moc_thoi_gian_da_ghi()
    {
        var modified = new DateTime(2026, 3, 14, 15, 9, 26, DateTimeKind.Utc);
        _database.Upsert(WithPath("/music/a.flac") with { FileModifiedUtc = modified });

        var map = _database.GetPathTimestamps();

        Assert.Equal(modified, map["/music/a.flac"]);
        Assert.Equal(DateTimeKind.Utc, map["/music/a.flac"].Kind);
    }

    [Fact]
    public void GetPathTimestamps_khong_phan_biet_hoa_thuong_vi_duong_dan_Windows_khong_phan_biet()
    {
        _database.Upsert(WithPath(@"C:\Music\A.flac"));

        Assert.True(_database.GetPathTimestamps().ContainsKey(@"c:\music\a.flac"));
    }

    [Fact]
    public void RemoveMissing_chi_xoa_thu_khong_con_tren_dia()
    {
        _database.UpsertBatch([WithPath("/music/a.flac"), WithPath("/music/b.flac"), WithPath("/music/c.flac")]);

        var removed = _database.RemoveMissing(["/music/a.flac", "/music/c.flac"]);

        Assert.Equal(1, removed);
        Assert.Equal(["/music/a.flac", "/music/c.flac"], _database.GetAll().Select(t => t.FilePath).Order());
    }

    [Fact]
    public void RemoveMissing_khong_xoa_gi_khi_moi_file_van_con()
    {
        _database.UpsertBatch([WithPath("/music/a.flac"), WithPath("/music/b.flac")]);

        Assert.Equal(0, _database.RemoveMissing(["/music/a.flac", "/music/b.flac"]));
        Assert.Equal(2, _database.GetAll().Count);
    }

    [Fact]
    public void RemoveMissing_voi_danh_sach_rong_thi_don_sach_thu_vien()
    {
        // Đây là lý do cờ removeMissing tồn tại: quét một thư mục lẻ mà bật cờ
        // này sẽ xoá sạch phần còn lại. Test ghim lại hành vi đó cho rõ ràng.
        _database.UpsertBatch([WithPath("/music/a.flac"), WithPath("/music/b.flac")]);

        Assert.Equal(2, _database.RemoveMissing([]));
        Assert.Empty(_database.GetAll());
    }

    [Fact]
    public void Clear_xoa_het_nhung_giu_lai_schema()
    {
        _database.UpsertBatch([WithPath("/music/a.flac"), WithPath("/music/b.flac")]);
        _database.Clear();

        Assert.Empty(_database.GetAll());

        _database.Upsert(WithPath("/music/c.flac"));
        Assert.Single(_database.GetAll());
    }

    [Fact]
    public void UpsertBatch_ghi_het_trong_mot_transaction()
    {
        _database.UpsertBatch(TrackFactory.Sequence(50).Select(t => WithPath(t.FilePath, t.Title)));

        Assert.Equal(50, _database.GetAll().Count);
    }

    [Fact]
    public void Du_lieu_song_sot_qua_mot_lan_mo_lai()
    {
        var path = Path.Combine(_directory, "reopen.db");
        using (var first = new LibraryDatabase(path))
        {
            first.Upsert(WithPath("/music/a.flac"));
        }

        using var second = new LibraryDatabase(path);
        Assert.Single(second.GetAll());
    }
}
