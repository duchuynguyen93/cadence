# Cadence

Trình phát nhạc local cho Windows. Chỉ nhạc — không video.

Giao diện theo cùng ngôn ngữ thiết kế với [Nocturne](https://github.com/duchuynguyen93/Nocturne),
trình phát video cùng nhà: nền gần đen, đúng một màu nhấn hổ phách, bo góc nhẹ.
Hai app trông cùng một họ là chủ ý.

**Stack:** .NET 10 · Avalonia 12 · BASS (un4seen) · SQLite

---

## Vì sao chọn stack này

| Quyết định | Lý do |
|---|---|
| **Avalonia** thay vì WinUI/WPF | Giao diện chủ động từ chối ngôn ngữ Fluent của Windows. Avalonia tự vẽ toàn bộ bằng Skia nên toàn quyền kiểm soát pixel, và selector kiểu CSS làm việc dựng look riêng dễ hơn hẳn so với ghi đè template của WinUI. Thêm nữa: app này không có bề mặt video, nên lý do duy nhất khiến Nocturne phải dùng WinUI (`SwapChainPanel` để phủ XAML lên video) không tồn tại ở đây. |
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

tests/
└── Cadence.Core.Tests/   52 test, chạy được trên mọi nền tảng
```

## Chạy test

```bash
dotnet test tests/Cadence.Core.Tests/Cadence.Core.Tests.csproj
```

Chỉ phủ `Cadence.Core` — và đó là chủ ý, không phải thiếu sót. Core không phụ
thuộc UI lẫn BASS nên test chạy trong vài trăm mili giây ở bất kỳ đâu, không cần
thiết bị âm thanh, không cần cửa sổ.

Ranh giới của cái test được và cái không:

| Test được ở đây | Cần máy thật / tai người |
|---|---|
| Hàng đợi, shuffle, repeat, con trỏ phát | Gapless có thật sự liền mạch không |
| Gom album, xử lý tag thiếu | Giải mã đúng từng codec |
| Vòng đời SQLite: upsert, quét lại, xoá | Bố cục và bảng màu |

`PlaybackQueue` nhận `Random` từ ngoài chính là để test dựng lại được đúng một
thứ tự shuffle đã cho — nếu không, mọi khẳng định về shuffle chỉ kiểm được tính
chất chung.

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

**Cách dễ nhất: tải bản cài sẵn.** Mỗi lần đẩy lên `main`, CI dựng bộ cài và
đưa lên [release `build-latest`](../../releases/tag/build-latest) — bản
self-contained nên máy đích không cần cài .NET.

Bộ cài đưa lên release chứ không phải artifact của Actions: quota artifact dùng
chung cho cả tài khoản (500MB gói Free), một bản self-contained đủ lớn để vài
lần chạy là hết, và lúc đó bước upload đỏ kéo cả job đỏ theo dù code chẳng sai gì.

Muốn dựng tay:

```bash
dotnet publish src/Cadence.App -c Release -r win-x64 --self-contained true -o dist/
# rồi trên Windows, có Inno Setup 6:
ISCC.exe installer\Cadence.iss     # ra artifacts/installer/Cadence-*-setup.exe
```

Publish chạy được cả từ macOS; riêng bước đóng gói bộ cài cần Windows.

Bộ cài đặt theo từng người dùng, **không hỏi quyền quản trị**. Liên kết file là
tuỳ chọn và mặc định tắt — và chỉ đăng ký những đuôi thật sự phát được, vì một
đuôi file đã đăng ký mà mở không lên thì Windows đổ lỗi cho app.

> **Đã dính bug này một lần:** phần copy native lib phải nằm trong `Cadence.App.csproj`,
> không phải `Cadence.Audio.csproj` — `RuntimeIdentifier` không lan xuống class library,
> nên đặt sai chỗ sẽ đóng gói `.dylib` của macOS vào bản Windows và app crash lúc mở.
> Target `VerifyBassNatives` trong csproj giờ chặn trường hợp này ngay lúc build.

---

## Trạng thái

### Đã chạy và đã kiểm chứng
- [x] Quét thư mục, đọc tag, index SQLite (bỏ qua file không đổi mtime)
- [x] Phát / tạm dừng / tua / chuyển bài, âm lượng theo thang cảm nhận
- [x] **Gapless playback** — kiểm bằng tai trên máy thật, gồm cả lúc chuyển MP3 → FLAC. Chưa có test tự động: nó cần phát ra thiết bị âm thanh thật rồi đo lại, tức là một dàn test khác hẳn với `Cadence.Core.Tests`.
- [x] Giải mã MP3, FLAC, WAV, ALAC/AAC (FLAC qua plugin `bassflac`)
- [x] **Ảnh bìa** — ưu tiên ảnh nhúng trong file, không có thì lấy `cover.jpg`/`folder.jpg`… cùng thư mục; hiện ở danh sách và thanh phát, có ô giữ chỗ khi không có ảnh
- [x] Shuffle (Fisher–Yates, giữ nguyên bài đang phát khi bật/tắt), repeat off/all/one
- [x] Tìm kiếm theo tên bài / nghệ sĩ / album
- [x] Đèn giao thông tự vẽ, title bar tuỳ biến, sáng/tối theo hệ thống
- [x] **Chế độ thu gọn** — cửa sổ co còn 420×132, nổi trên cùng, chỉ còn ảnh bìa + tên bài + prev/play/next. Bấm nút ở góc phải title bar. Hai bố cục cùng bind một ViewModel nên chuyển qua lại không làm nhạc gián đoạn.
- [x] **52 test** cho `Cadence.Core` — hàng đợi, shuffle, repeat, gom album, vòng đời SQLite. Chạy trên mọi nền tảng, có trong CI.

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
