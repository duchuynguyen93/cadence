using Cadence.Core.Models;

namespace Cadence.Core.Tests;

/// <summary>
/// Dựng <see cref="Track"/> cho test.
///
/// Track có hai trường <c>required</c>, nên viết tay từng cái trong mỗi test sẽ
/// che mất thứ mà test đó thực sự quan tâm. Ở đây chỉ đặt phần tối thiểu, và
/// mỗi test tự ghi đè đúng trường mình đang kiểm.
/// </summary>
internal static class TrackFactory
{
    internal static Track Make(string name, string? artist = null, string? album = null) => new()
    {
        FilePath = $"/music/{name}.flac",
        Title = name,
        Artist = artist,
        Album = album,
    };

    /// <summary>Tạo <paramref name="count"/> bài tên "1", "2", … theo thứ tự.</summary>
    internal static Track[] Sequence(int count) =>
        [.. Enumerable.Range(1, count).Select(i => Make(i.ToString()))];
}
