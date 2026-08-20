using System.Collections.Concurrent;
using Cadence.Core.Models;

namespace Cadence.Core.Library;

public sealed record ScanProgress(int FilesSeen, int TracksImported, int Skipped, string? CurrentFile);

public sealed record ScanResult(int Imported, int Unchanged, int Failed, int Removed, TimeSpan Elapsed);

/// <summary>
/// Quét thư mục nhạc và đồng bộ vào <see cref="LibraryDatabase"/>.
///
/// Quét lại lần hai rất nhanh vì file có mtime trùng với bản ghi trong DB sẽ bị bỏ qua
/// hoàn toàn — không mở file, không đọc tag. Thư viện 10.000 bài mà chỉ đổi vài file
/// thì lần quét sau chỉ tốn công liệt kê thư mục.
/// </summary>
public sealed class LibraryScanner(LibraryDatabase database, TrackMetadataReader reader)
{
    /// <summary>
    /// Quét các thư mục và cập nhật DB.
    /// </summary>
    /// <param name="removeMissing">
    /// Xoá khỏi DB những bài không còn thấy trên đĩa. Chỉ bật khi quét TOÀN BỘ thư mục
    /// đã cấu hình — quét một thư mục lẻ mà bật cờ này sẽ xoá sạch phần còn lại của thư viện.
    /// </param>
    public async Task<ScanResult> ScanAsync(
        IReadOnlyList<string> folders,
        bool removeMissing = true,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var startedAt = DateTime.UtcNow;

        var known = database.GetPathTimestamps();
        var files = EnumerateAudioFiles(folders, cancellationToken);

        var imported = new ConcurrentBag<Track>();
        var seen = 0;
        var unchanged = 0;
        var failed = 0;

        // Đọc tag là I/O-bound và TagLib an toàn khi dùng song song trên các file khác nhau,
        // nên chạy nhiều luồng để tận dụng SSD. Giới hạn bằng số nhân để không làm
        // treo máy khi thư viện nằm trên ổ mạng.
        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = Environment.ProcessorCount,
            CancellationToken = cancellationToken,
        };

        await Task.Run(() => Parallel.ForEach(files, options, file =>
        {
            var count = Interlocked.Increment(ref seen);

            try
            {
                var lastWrite = File.GetLastWriteTimeUtc(file);

                // Bỏ qua file không đổi kể từ lần quét trước. So sánh tới giây là đủ:
                // một số hệ thống file làm tròn mtime nên so bằng tick sẽ luôn lệch.
                if (known.TryGetValue(file, out var storedTime) &&
                    Math.Abs((storedTime - lastWrite).TotalSeconds) < 1)
                {
                    Interlocked.Increment(ref unchanged);
                    return;
                }

                var track = reader.Read(file);
                if (track is null)
                {
                    Interlocked.Increment(ref failed);
                    return;
                }

                imported.Add(track);
            }
            catch (Exception)
            {
                // File bị khoá, mất quyền đọc, ổ mạng rớt... — bỏ qua từng file,
                // không để một file làm hỏng cả lượt quét.
                Interlocked.Increment(ref failed);
            }
            finally
            {
                if (count % 25 == 0)
                    progress?.Report(new ScanProgress(count, imported.Count, unchanged, file));
            }
        }), cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();

        // Ghi một lần ở cuối trong một transaction. Ghi rải rác trong vòng lặp song song
        // vừa chậm vừa phải khoá connection giữa các thread.
        if (!imported.IsEmpty) database.UpsertBatch(imported);

        var removed = removeMissing ? database.RemoveMissing(files) : 0;

        // Cố ý KHÔNG báo progress lần cuối ở đây. Progress<T> đẩy callback về UI thread
        // theo kiểu bất đồng bộ, nên bản tin cuối cùng thường tới SAU khi caller đã
        // hiển thị kết quả tổng kết — và ghi đè mất nó. ScanResult trả về đã đủ số liệu.
        return new ScanResult(imported.Count, unchanged, failed, removed, DateTime.UtcNow - startedAt);
    }

    private static List<string> EnumerateAudioFiles(
        IReadOnlyList<string> folders, CancellationToken cancellationToken)
    {
        var files = new List<string>();

        foreach (var folder in folders)
        {
            if (!Directory.Exists(folder)) continue;

            // IgnoreInaccessible: gặp thư mục không có quyền đọc thì bỏ qua thay vì
            // ném UnauthorizedAccessException và giết cả lượt liệt kê.
            var options = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.Hidden | FileAttributes.System,
            };

            foreach (var file in Directory.EnumerateFiles(folder, "*", options))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (TrackMetadataReader.IsSupportedFile(file)) files.Add(file);
            }
        }

        return files;
    }
}
