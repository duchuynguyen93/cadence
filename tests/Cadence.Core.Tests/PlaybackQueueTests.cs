using Cadence.Core.Models;
using Cadence.Core.Playback;
using Xunit;

namespace Cadence.Core.Tests;

public sealed class PlaybackQueueTests
{
    private static PlaybackQueue Loaded(int count = 3, int startIndex = 0, int seed = 1234)
    {
        var queue = new PlaybackQueue(new Random(seed));
        queue.Load(TrackFactory.Sequence(count), startIndex);
        return queue;
    }

    private static List<string> PlayThrough(PlaybackQueue queue, int max = 50)
    {
        var visited = new List<string>();
        if (queue.Current is { } first) visited.Add(first.Title);
        while (visited.Count < max && queue.MoveNext(userInitiated: false))
        {
            visited.Add(queue.Current!.Title);
        }

        return visited;
    }

    // ── Nạp danh sách ───────────────────────────────────────────────────────

    [Fact]
    public void Nap_xong_thi_bai_dau_tien_la_bai_duoc_chi_dinh()
    {
        var queue = Loaded(startIndex: 1);

        Assert.Equal("2", queue.Current!.Title);
        Assert.Equal(1, queue.CurrentIndex);
    }

    [Fact]
    public void StartIndex_ngoai_pham_vi_bi_kep_lai_chu_khong_nem_loi()
    {
        // Mở một thư mục rồi file bị xoá mất trước khi bấm phát là chuyện thường.
        Assert.Equal("3", Loaded(startIndex: 99).Current!.Title);
        Assert.Equal("1", Loaded(startIndex: -5).Current!.Title);
    }

    [Fact]
    public void Queue_rong_thi_moi_thao_tac_deu_tra_ve_false()
    {
        var queue = new PlaybackQueue(new Random(1));
        queue.Load([]);

        Assert.Null(queue.Current);
        Assert.Equal(-1, queue.CurrentIndex);
        Assert.False(queue.MoveNext(userInitiated: true));
        Assert.False(queue.MovePrevious());
        Assert.Null(queue.PeekNext());
    }

    [Fact]
    public void Nap_lan_hai_thay_the_hoan_toan_danh_sach_cu()
    {
        var queue = Loaded(count: 3);
        queue.Load([TrackFactory.Make("khac")], 0);

        Assert.Equal(1, queue.Count);
        Assert.Equal("khac", queue.Current!.Title);
    }

    // ── Repeat ──────────────────────────────────────────────────────────────

    [Fact]
    public void Repeat_off_thi_dung_lai_o_cuoi_queue()
    {
        var queue = Loaded(startIndex: 2);

        Assert.False(queue.MoveNext(userInitiated: false));
        Assert.Equal("3", queue.Current!.Title);
    }

    [Fact]
    public void Repeat_all_thi_vong_lai_ca_hai_chieu()
    {
        var queue = Loaded(startIndex: 2);
        queue.RepeatMode = RepeatMode.All;

        Assert.True(queue.MoveNext(userInitiated: false));
        Assert.Equal("1", queue.Current!.Title);

        Assert.True(queue.MovePrevious());
        Assert.Equal("3", queue.Current!.Title);
    }

    [Fact]
    public void Repeat_one_lap_lai_khi_het_bai_nhung_van_di_tiep_khi_bam_Next()
    {
        var queue = Loaded();
        queue.RepeatMode = RepeatMode.One;

        // Hết bài tự nhiên: giữ nguyên bài.
        Assert.True(queue.MoveNext(userInitiated: false));
        Assert.Equal("1", queue.Current!.Title);

        // User bấm Next: phải đi tiếp thật, nếu không nút Next trông như hỏng.
        Assert.True(queue.MoveNext(userInitiated: true));
        Assert.Equal("2", queue.Current!.Title);
    }

    [Fact]
    public void Repeat_off_o_dau_queue_thi_Previous_dung_yen()
    {
        var queue = Loaded();

        Assert.False(queue.MovePrevious());
        Assert.Equal("1", queue.Current!.Title);
    }

    // ── PeekNext, phục vụ gapless preload ───────────────────────────────────

    [Fact]
    public void PeekNext_khong_lam_dich_con_tro()
    {
        var queue = Loaded();

        Assert.Equal("2", queue.PeekNext()!.Title);
        Assert.Equal("1", queue.Current!.Title);
        Assert.Equal(0, queue.CurrentIndex);
    }

    [Fact]
    public void PeekNext_o_cuoi_queue_tra_ve_null_khi_repeat_off()
    {
        Assert.Null(Loaded(startIndex: 2).PeekNext());
    }

    [Fact]
    public void PeekNext_o_cuoi_queue_tro_ve_dau_khi_repeat_all()
    {
        var queue = Loaded(startIndex: 2);
        queue.RepeatMode = RepeatMode.All;

        Assert.Equal("1", queue.PeekNext()!.Title);
    }

    [Fact]
    public void PeekNext_bo_qua_repeat_one_vi_lap_mot_bai_khong_can_nap_file()
    {
        var queue = Loaded();
        queue.RepeatMode = RepeatMode.One;

        // Lặp một bài thì engine seek về 0, không nạp lại file — preload bài kế
        // vẫn đúng là bài kế, không phải bài hiện tại.
        Assert.Equal("2", queue.PeekNext()!.Title);
    }

    // ── Shuffle ─────────────────────────────────────────────────────────────

    [Fact]
    public void Bat_shuffle_giua_chung_khong_lam_nhay_bai_dang_phat()
    {
        var queue = Loaded(count: 10, startIndex: 4);
        var playing = queue.Current!.Title;

        queue.Shuffle = true;

        Assert.Equal(playing, queue.Current!.Title);
    }

    [Fact]
    public void Shuffle_khong_dung_toi_thu_tu_hien_thi()
    {
        var queue = Loaded(count: 10);
        var visible = queue.Tracks.Select(t => t.Title).ToArray();

        queue.Shuffle = true;

        // UI vẫn hiển thị đúng thứ tự người dùng thấy quen; chỉ thứ tự PHÁT đổi.
        Assert.Equal(visible, queue.Tracks.Select(t => t.Title));
    }

    [Fact]
    public void Tat_shuffle_thi_tra_lai_thu_tu_phat_ban_dau()
    {
        var queue = Loaded(count: 6, startIndex: 0);
        queue.Shuffle = true;
        queue.Shuffle = false;

        Assert.Equal(0, queue.CurrentIndex);
        Assert.Equal(["1", "2", "3", "4", "5", "6"], PlayThrough(queue));
    }

    [Fact]
    public void Mot_luot_shuffle_di_qua_moi_bai_dung_mot_lan()
    {
        var queue = new PlaybackQueue(new Random(99));
        queue.Load(TrackFactory.Sequence(8), 0);
        queue.Shuffle = true;

        var visited = PlayThrough(queue);

        Assert.Equal(8, visited.Count);
        Assert.Equal(8, visited.Distinct().Count());
    }

    [Fact]
    public void Bam_vao_mot_bai_khi_dang_shuffle_thi_bai_do_phat_truoc()
    {
        var queue = new PlaybackQueue(new Random(7)) { Shuffle = true };
        queue.Load(TrackFactory.Sequence(10), startIndex: 6);

        // Không để random quyết định: user bấm bài nào thì bài đó phát.
        Assert.Equal("7", queue.Current!.Title);
        Assert.Equal(6, queue.CurrentIndex);
    }

    [Fact]
    public void Shuffle_voi_repeat_all_thi_vong_hai_tron_lai()
    {
        // Cùng seed, cùng danh sách: nếu vòng hai KHÔNG tráo lại thì hai vòng
        // sẽ trùng nhau y hệt, và đó chính là điều cần tránh.
        var queue = new PlaybackQueue(new Random(4242));
        queue.Load(TrackFactory.Sequence(8), 0);
        queue.Shuffle = true;
        queue.RepeatMode = RepeatMode.All;

        var firstPass = new List<string> { queue.Current!.Title };
        for (var i = 0; i < 7; i++)
        {
            queue.MoveNext(userInitiated: false);
            firstPass.Add(queue.Current!.Title);
        }

        var secondPass = new List<string>();
        for (var i = 0; i < 8; i++)
        {
            queue.MoveNext(userInitiated: false);
            secondPass.Add(queue.Current!.Title);
        }

        Assert.Equal(8, secondPass.Distinct().Count());
        Assert.NotEqual(firstPass, secondPass);
    }

    [Fact]
    public void Shuffle_khong_bi_dung_lai_tren_queue_rong()
    {
        var queue = new PlaybackQueue(new Random(1));
        queue.Load([]);

        queue.Shuffle = true;

        Assert.Null(queue.Current);
    }

    // ── Append: mở file bằng "Open with" rồi nối hàng xóm vào sau ───────────

    [Fact]
    public void Append_khong_lam_gian_doan_bai_dang_phat()
    {
        var queue = new PlaybackQueue(new Random(11));
        queue.Load([TrackFactory.Make("dang-phat")], 0);

        queue.Append(TrackFactory.Sequence(3));

        // Đây là toàn bộ lý do Append tồn tại: gọi Load ở bước hai sẽ cắt ngang
        // chính bài vừa bắt đầu phát.
        Assert.Equal("dang-phat", queue.Current!.Title);
        Assert.Equal(0, queue.CurrentIndex);
        Assert.Equal(4, queue.Count);
    }

    [Fact]
    public void Append_lam_bai_ke_co_tro_lai()
    {
        var queue = new PlaybackQueue(new Random(11));
        queue.Load([TrackFactory.Make("dang-phat")], 0);
        Assert.Null(queue.PeekNext());

        queue.Append(TrackFactory.Sequence(2));

        Assert.NotNull(queue.PeekNext());
        Assert.True(queue.MoveNext(userInitiated: true));
        Assert.Equal("1", queue.Current!.Title);
    }

    [Fact]
    public void Append_vao_queue_rong_thi_co_bai_hien_tai()
    {
        var queue = new PlaybackQueue(new Random(11));
        queue.Load([]);

        queue.Append(TrackFactory.Sequence(2));

        Assert.NotNull(queue.Current);
        Assert.Equal(0, queue.CurrentIndex);
    }

    [Fact]
    public void Append_danh_sach_rong_khong_lam_gi_ca()
    {
        var queue = Loaded();
        var before = queue.Current;
        var events = 0;
        queue.QueueChanged += (_, _) => events++;

        queue.Append([]);

        Assert.Equal(3, queue.Count);
        Assert.Equal(before, queue.Current);
        Assert.Equal(0, events);
    }

    [Fact]
    public void Append_giu_nguyen_thu_tu_hien_thi()
    {
        var queue = Loaded();

        queue.Append([TrackFactory.Make("moi")]);

        Assert.Equal(["1", "2", "3", "moi"], queue.Tracks.Select(t => t.Title));
    }

    [Fact]
    public void Append_khi_dang_shuffle_khong_dua_bai_da_nghe_ra_truoc()
    {
        var queue = new PlaybackQueue(new Random(5));
        queue.Load(TrackFactory.Sequence(4), 0);
        queue.Shuffle = true;

        var played = new List<string> { queue.Current!.Title };
        queue.MoveNext(userInitiated: true);
        played.Add(queue.Current!.Title);

        queue.Append([TrackFactory.Make("moi-1"), TrackFactory.Make("moi-2")]);

        // Phần chưa phát được tráo lại, nhưng bài đã nghe phải nằm yên phía sau
        // con trỏ — nếu không chúng sẽ được phát lại trong cùng một lượt.
        var remaining = new List<string>();
        while (queue.MoveNext(userInitiated: true)) remaining.Add(queue.Current!.Title);

        Assert.DoesNotContain(played[0], remaining);
        Assert.DoesNotContain(played[1], remaining);
        Assert.Equal(4, remaining.Count);
        Assert.Equal(4, remaining.Distinct().Count());
    }

    // ── JumpTo và Clear ─────────────────────────────────────────────────────

    [Fact]
    public void JumpTo_dung_chi_so_cua_danh_sach_hien_thi_ke_ca_khi_shuffle()
    {
        var queue = Loaded(count: 10, seed: 3);
        queue.Shuffle = true;

        Assert.True(queue.JumpTo(7));
        Assert.Equal("8", queue.Current!.Title);
        Assert.Equal(7, queue.CurrentIndex);
    }

    [Fact]
    public void JumpTo_ngoai_pham_vi_tra_ve_false_va_khong_doi_bai()
    {
        var queue = Loaded();

        Assert.False(queue.JumpTo(42));
        Assert.False(queue.JumpTo(-1));
        Assert.Equal("1", queue.Current!.Title);
    }

    [Fact]
    public void Clear_dua_queue_ve_trang_thai_rong()
    {
        var queue = Loaded();
        queue.Clear();

        Assert.Equal(0, queue.Count);
        Assert.Null(queue.Current);
        Assert.Equal(-1, queue.CurrentIndex);
    }

    // ── Sự kiện ─────────────────────────────────────────────────────────────

    [Fact]
    public void Doi_bai_thi_ban_CurrentChanged()
    {
        var queue = Loaded();
        var count = 0;
        queue.CurrentChanged += (_, _) => count++;

        queue.MoveNext(userInitiated: true);

        Assert.Equal(1, count);
    }

    [Fact]
    public void Repeat_one_van_ban_CurrentChanged_du_bai_khong_doi()
    {
        // Engine cần tín hiệu để seek về 0; im lặng thì bài dừng hẳn ở cuối.
        var queue = Loaded();
        queue.RepeatMode = RepeatMode.One;
        var count = 0;
        queue.CurrentChanged += (_, _) => count++;

        queue.MoveNext(userInitiated: false);

        Assert.Equal(1, count);
    }

    [Fact]
    public void Bat_shuffle_thi_ban_QueueChanged()
    {
        var queue = Loaded();
        var count = 0;
        queue.QueueChanged += (_, _) => count++;

        queue.Shuffle = true;
        queue.Shuffle = true; // gán lại cùng giá trị: không được bắn thêm

        Assert.Equal(1, count);
    }
}
