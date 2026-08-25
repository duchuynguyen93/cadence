using Cadence.Core.Models;
using Xunit;

namespace Cadence.Core.Tests;

public sealed class TrackTests
{
    [Fact]
    public void DisplayArtist_lan_luot_lui_ve_AlbumArtist_roi_moi_bo_cuoc()
    {
        Assert.Equal("A", TrackFactory.Make("t", artist: "A").DisplayArtist);

        var albumArtistOnly = TrackFactory.Make("t") with { AlbumArtist = "B" };
        Assert.Equal("B", albumArtistOnly.DisplayArtist);

        Assert.Equal("Unknown Artist", TrackFactory.Make("t").DisplayArtist);
    }

    [Fact]
    public void DisplayAlbumArtist_uu_tien_nguoc_lai_so_voi_DisplayArtist()
    {
        // Gom album phải bám AlbumArtist trước, nếu không album tuyển tập sẽ vỡ
        // thành nhiều mảnh theo từng nghệ sĩ khách mời.
        var track = TrackFactory.Make("t", artist: "Khach") with { AlbumArtist = "Chinh" };

        Assert.Equal("Chinh", track.DisplayAlbumArtist);
        Assert.Equal("Khach", track.DisplayArtist);
    }

    [Fact]
    public void AlbumKey_khong_phan_biet_hoa_thuong()
    {
        var lower = TrackFactory.Make("a", artist: "radiohead", album: "kid a");
        var upper = TrackFactory.Make("b", artist: "Radiohead", album: "Kid A");

        Assert.Equal(lower.AlbumKey, upper.AlbumKey);
    }

    [Fact]
    public void AlbumKey_khong_dung_do_khi_ghep_ten_nghe_si_va_album()
    {
        // Đây chính là ca mà ký tự ngăn cách U+001F sinh ra để chặn: nối thẳng
        // thì "AB" + "C" và "A" + "BC" ra cùng một khoá, hai album khác nhau bị
        // gom làm một.
        var first = TrackFactory.Make("x", artist: "AB", album: "C");
        var second = TrackFactory.Make("y", artist: "A", album: "BC");

        Assert.NotEqual(first.AlbumKey, second.AlbumKey);
    }

    [Fact]
    public void AlbumKey_gom_dung_cac_bai_cung_album()
    {
        var tracks = new[]
        {
            TrackFactory.Make("1", artist: "Nghe si", album: "Album"),
            TrackFactory.Make("2", artist: "Nghe si", album: "Album"),
            TrackFactory.Make("3", artist: "Nghe si", album: "Album khac"),
        };

        Assert.Equal(2, tracks.Select(t => t.AlbumKey).Distinct().Count());
    }

    [Fact]
    public void Track_la_record_nen_so_sanh_theo_gia_tri()
    {
        // PlaybackQueue và các ViewModel dựa vào điều này để so sánh bài đang phát.
        var a = TrackFactory.Make("bai", artist: "A");
        var b = TrackFactory.Make("bai", artist: "A");

        Assert.Equal(a, b);
    }

    [Fact]
    public void Album_khong_ten_van_gom_duoc_voi_nhau()
    {
        var first = TrackFactory.Make("1", artist: "A");
        var second = TrackFactory.Make("2", artist: "A");

        Assert.Equal("Unknown Album", first.DisplayAlbum);
        Assert.Equal(first.AlbumKey, second.AlbumKey);
    }
}

public sealed class AudioFormatTests
{
    [Theory]
    [InlineData(44_100, 16, false)]
    [InlineData(48_000, 16, false)]
    [InlineData(48_001, 16, true)]
    [InlineData(96_000, 16, true)]
    [InlineData(44_100, 24, true)]
    public void IsHiRes_dung_o_ca_hai_bien(int sampleRate, int bits, bool expected)
    {
        var format = new AudioFormat(sampleRate, 2, bits, "FLAC");

        Assert.Equal(expected, format.IsHiRes);
    }

    [Fact]
    public void ToString_ghi_stereo_thay_vi_dem_kenh()
    {
        Assert.Equal("FLAC · 44.1 kHz · 16-bit · Stereo", new AudioFormat(44_100, 2, 16, "FLAC").ToString());
        Assert.Equal("DSD · 96 kHz · 24-bit · 6ch", new AudioFormat(96_000, 6, 24, "DSD").ToString());
    }

    [Fact]
    public void ToString_bo_so_le_thua_o_sample_rate_tron()
    {
        // 48000 phải ra "48 kHz" chứ không phải "48.0 kHz".
        Assert.Contains("48 kHz", new AudioFormat(48_000, 2, 16, "AAC").ToString(), StringComparison.Ordinal);
    }
}
