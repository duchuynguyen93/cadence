# Cadence

Trình phát nhạc local cho Windows, giao diện dựng theo ngôn ngữ thiết kế macOS.
Chỉ nhạc — không video.

**Stack:** .NET 10 · Avalonia 12 · BASS (un4seen) · SQLite

---

## Vì sao chọn stack này

| Quyết định | Lý do |
|---|---|
| **Avalonia** thay vì WinUI/WPF | Mục tiêu là giao diện *macOS* chạy trên Windows, tức là chủ động từ chối ngôn ngữ thiết kế Fluent. Avalonia tự vẽ toàn bộ bằng Skia nên toàn quyền kiểm soát pixel. |
| **Avalonia** thay vì Tauri/Electron | Không phụ thuộc WebView2, không có ranh giới IPC giữa audio thread và UI (quan trọng cho visualizer 60fps), RAM thấp hơn. |
| Render bằng Skia | App chạy trên macOS và Windows ra **pixel giống hệt nhau** — dev trên Mac không còn rủi ro "trên máy tao chạy ngon". |
| **BASS** thay vì NAudio thuần | NAudio miễn phí nhưng thiếu decoder cho ALAC/AAC. BASS phủ đủ codec và có gapless playback đúng nghĩa. |

## Cấu trúc

```
src/
├── Cadence.Core/     Không phụ thuộc UI lẫn BASS
│   ├── Models/       Track, PlaybackState, AudioFormat
│   ├── Abstractions/ IAudioEngine  ← ranh giới quan trọng nhất, xem bên dưới
│   ├── Library/      Quét thư mục, đọc tag (TagLib), index SQLite
│   └── Playback/     PlaybackQueue (shuffle/repeat), PlaybackService (điều phối)
├── Cadence.Audio/    Hiện thực IAudioEngine bằng BASS
└── Cadence.App/      Avalonia — Views, ViewModels, Styles
```

### `IAudioEngine` — ranh giới cần giữ

Toàn bộ app chỉ biết interface này, **không chỗ nào ngoài `Cadence.Audio` được tham
chiếu tới BASS**. Đây không phải trừu tượng hoá cho vui: BASS miễn phí với app free
nhưng phải mua license nếu bán. Giữ được ranh giới thì đổi sang LibVLCSharp hoặc
NAudio chỉ là viết một implementation mới.

### Gapless hoạt động thế nào

```
file hiện tại ──> decode stream ──┐
                                  ├──> mixer stream ──> loa
file kế (preload) ──> decode stream ┘   (cắm vào đúng lúc stream cũ hết)
```

BASS bắn sync ở chế độ **mixtime** khi decode stream hết dữ liệu — callback chạy
*trong* lúc mixer đang trộn, nên stream kế được nối vào mà không hụt một sample nào.
Chi tiết trong `BassAudioEngine.OnStreamEnded`.

**Giới hạn:** đổi giữa hai bài khác sample rate (44.1k → 96k) buộc phải dựng lại
mixer, lần chuyển đó không gapless. Không tránh được — sound card cũng phải đổi chế độ.

---

## Chạy lần đầu

```bash
# 1. Tải native lib của BASS (không có trong git, xem phần Giấy phép)
./scripts/fetch-bass.sh

# 2. Chạy
dotnet run --project src/Cadence.App
```

Yêu cầu .NET 10 SDK. Nếu chưa có:
```bash
curl -fsSL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel 10.0
export DOTNET_ROOT="$HOME/.dotnet" && export PATH="$DOTNET_ROOT:$PATH"
```

## Build cho Windows

```bash
dotnet publish src/Cadence.App -c Release -r win-x64 --self-contained false -o dist/
```

Chạy được cả từ macOS. Bản `--self-contained false` cần máy đích có .NET 10 Runtime;
thêm `--self-contained true` để đóng gói kèm runtime (nặng hơn ~70MB nhưng chạy ngay).

> **Đã dính bug này một lần:** phần copy native lib phải nằm trong `Cadence.App.csproj`,
> không phải `Cadence.Audio.csproj` — `RuntimeIdentifier` không lan xuống class library,
> nên đặt sai chỗ sẽ đóng gói `.dylib` của macOS vào bản Windows và app crash lúc mở.
> Target `VerifyBassNatives` trong csproj giờ chặn trường hợp này ngay lúc build.

---

## Trạng thái

### Đã chạy và đã kiểm chứng
- [x] Quét thư mục, đọc tag, index SQLite (bỏ qua file không đổi mtime)
- [x] Phát / tạm dừng / tua / chuyển bài, âm lượng theo thang cảm nhận
- [x] **Gapless playback** — verify bằng test đo tần số qua FFT trước/sau lúc chuyển bài, chạy được cả khi chuyển MP3 → FLAC
- [x] Giải mã MP3, FLAC, WAV, ALAC/AAC (FLAC qua plugin `bassflac`)
- [x] **Ảnh bìa** — ưu tiên ảnh nhúng trong file, không có thì lấy `cover.jpg`/`folder.jpg`… cùng thư mục; hiện ở danh sách và thanh phát, có ô giữ chỗ khi không có ảnh
- [x] Shuffle (Fisher–Yates, giữ nguyên bài đang phát khi bật/tắt), repeat off/all/one
- [x] Tìm kiếm theo tên bài / nghệ sĩ / album
- [x] Giao diện kiểu macOS: đèn giao thông tự vẽ, title bar tuỳ biến, sáng/tối theo hệ thống

### Chưa làm
- [ ] **WASAPI exclusive mode / bit-perfect** — cần đổi đường xuất sang BASSWASAPI, không dùng `Bass.Init` nữa
- [ ] Chế độ xem Album / Nghệ sĩ (sidebar đã có mục nhưng đang bị vô hiệu hoá)
- [ ] Visualizer phổ tần (`IAudioEngine.ReadSpectrum` đã chạy, cần render loop 60fps riêng)
- [ ] Playlist, phím tắt media, ReplayGain, sửa tag
- [ ] Codec cần plugin BASS chưa bundle: `.opus`, `.ape`, `.wv`, `.dsf` — scanner đang index nhưng phát sẽ lỗi

## Ảnh bìa hoạt động thế nào

Lúc quét, ảnh được ghi ra `%APPDATA%\Cadence\artwork\{sha256}.img`, và `Track` chỉ giữ
cái hash. Nhờ vậy 12 bài cùng album chia sẻ đúng **một** file ảnh thay vì 12 bản sao.

Khi vẽ, `ArtworkImage` giải mã ảnh **đúng bằng cỡ hiển thị** (`Bitmap.DecodeToWidth`) và
giữ trong cache LRU có giới hạn. Bìa album ngày nay thường 1500×1500 — giải mã nguyên cỡ
để vẽ ô 26px là 9MB RAM mỗi bài, cuộn qua vài trăm hàng là hết bộ nhớ.

> **Đã dính bug này một lần:** scanner đọc metadata song song, và mọi bài trong cùng
> album cho ra cùng một hash ảnh. Ban đầu file tạm đặt tên cố định `{hash}.tmp` nên các
> thread giẫm lên nhau; thread thua ném `IOException`, bị catch ở tầng trên và **loại
> luôn bài hát khỏi thư viện**. Album 12 bài quét xong còn đúng 1 bài, không báo lỗi gì.
> Giờ tên file tạm có GUID, và lỗi ảnh bìa không bao giờ làm mất bài hát nữa.

---

## Giấy phép

Code trong repo này là của bạn. Nhưng:

**BASS là phần mềm độc quyền của [un4seen](https://www.un4seen.com/bass.html).**
Miễn phí cho phần mềm phi thương mại / miễn phí. **Muốn bán app thì phải mua license
(~€125 trở lên).** File `.dll`/`.dylib` của BASS không được commit vào git (xem
`.gitignore`) — dùng `scripts/fetch-bass.sh` để tải.

Nếu không muốn dính ràng buộc này: viết một implementation `IAudioEngine` mới dựa trên
LibVLCSharp (LGPL, free kể cả thương mại) rồi đổi một dòng trong `App.axaml.cs`.
Toàn bộ phần còn lại của app không cần sửa gì.
