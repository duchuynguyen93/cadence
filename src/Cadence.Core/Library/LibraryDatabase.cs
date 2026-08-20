using Cadence.Core.Models;
using Microsoft.Data.Sqlite;

namespace Cadence.Core.Library;

/// <summary>
/// Chỉ mục thư viện nhạc trên SQLite.
///
/// Vì sao cần DB thay vì quét thư mục mỗi lần mở app: đọc tag của 10.000 file mất
/// hàng chục giây và quay đĩa liên tục. Có index thì khởi động là đọc một lượt DB
/// (dưới một giây), rồi quét lại ở nền để bắt thay đổi.
/// </summary>
public sealed class LibraryDatabase : IDisposable
{
    private readonly SqliteConnection _connection;

    public LibraryDatabase(string databasePath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);

        _connection = new SqliteConnection($"Data Source={databasePath}");
        _connection.Open();

        // WAL cho phép đọc (UI) song song với ghi (scanner chạy nền) mà không chặn nhau.
        // Thiếu nó thì mỗi lần scan sẽ làm UI đứng hình khi cuộn danh sách.
        Execute("PRAGMA journal_mode=WAL;");
        Execute("PRAGMA synchronous=NORMAL;");

        CreateSchema();
    }

    private void CreateSchema()
    {
        Execute("""
            CREATE TABLE IF NOT EXISTS tracks (
                id                INTEGER PRIMARY KEY AUTOINCREMENT,
                file_path         TEXT    NOT NULL UNIQUE,
                title             TEXT    NOT NULL,
                artist            TEXT,
                album_artist      TEXT,
                album             TEXT,
                track_number      INTEGER NOT NULL DEFAULT 0,
                disc_number       INTEGER NOT NULL DEFAULT 0,
                year              INTEGER NOT NULL DEFAULT 0,
                genre             TEXT,
                duration_ticks    INTEGER NOT NULL DEFAULT 0,
                bitrate           INTEGER NOT NULL DEFAULT 0,
                sample_rate       INTEGER NOT NULL DEFAULT 0,
                channels          INTEGER NOT NULL DEFAULT 0,
                codec             TEXT,
                file_size         INTEGER NOT NULL DEFAULT 0,
                file_modified_utc INTEGER NOT NULL DEFAULT 0,
                date_added_utc    INTEGER NOT NULL DEFAULT 0,
                artwork_hash      TEXT
            );
            """);

        // Index phục vụ hai truy vấn nóng nhất: gom theo album, và lọc theo nghệ sĩ.
        Execute("CREATE INDEX IF NOT EXISTS idx_tracks_album ON tracks(album_artist, album, disc_number, track_number);");
        Execute("CREATE INDEX IF NOT EXISTS idx_tracks_artist ON tracks(artist);");
    }

    /// <summary>
    /// Chèn mới hoặc cập nhật theo <c>file_path</c>.
    /// Giữ nguyên <c>date_added_utc</c> của bản ghi cũ — user sắp xếp theo "mới thêm"
    /// thì một lần rescan không được đẩy cả thư viện lên đầu danh sách.
    /// </summary>
    public void Upsert(Track track)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = """
            INSERT INTO tracks (
                file_path, title, artist, album_artist, album, track_number, disc_number,
                year, genre, duration_ticks, bitrate, sample_rate, channels, codec,
                file_size, file_modified_utc, date_added_utc, artwork_hash
            ) VALUES (
                $path, $title, $artist, $albumArtist, $album, $trackNo, $discNo,
                $year, $genre, $duration, $bitrate, $sampleRate, $channels, $codec,
                $size, $modified, $added, $artwork
            )
            ON CONFLICT(file_path) DO UPDATE SET
                title = excluded.title,
                artist = excluded.artist,
                album_artist = excluded.album_artist,
                album = excluded.album,
                track_number = excluded.track_number,
                disc_number = excluded.disc_number,
                year = excluded.year,
                genre = excluded.genre,
                duration_ticks = excluded.duration_ticks,
                bitrate = excluded.bitrate,
                sample_rate = excluded.sample_rate,
                channels = excluded.channels,
                codec = excluded.codec,
                file_size = excluded.file_size,
                file_modified_utc = excluded.file_modified_utc,
                artwork_hash = excluded.artwork_hash;
            """;

        Bind(command, track);
        command.ExecuteNonQuery();
    }

    /// <summary>
    /// Ghi nhiều track trong một transaction.
    /// SQLite mặc định commit từng câu lệnh — 10.000 insert lẻ mất vài phút,
    /// gộp vào một transaction còn vài giây. Khác biệt cỡ hai bậc độ lớn.
    /// </summary>
    public void UpsertBatch(IEnumerable<Track> tracks)
    {
        using var transaction = _connection.BeginTransaction();
        foreach (var track in tracks) Upsert(track);
        transaction.Commit();
    }

    public IReadOnlyList<Track> GetAll()
    {
        using var command = _connection.CreateCommand();
        command.CommandText = """
            SELECT id, file_path, title, artist, album_artist, album, track_number,
                   disc_number, year, genre, duration_ticks, bitrate, sample_rate,
                   channels, codec, file_size, file_modified_utc, date_added_utc, artwork_hash
            FROM tracks
            ORDER BY album_artist, album, disc_number, track_number, title;
            """;

        var results = new List<Track>();
        using var reader = command.ExecuteReader();
        while (reader.Read()) results.Add(Read(reader));
        return results;
    }

    /// <summary>
    /// Map đường dẫn -> thời điểm sửa file, để scanner biết file nào không đổi mà bỏ qua.
    /// Chỉ lấy 2 cột nên rẻ hơn nhiều so với nạp cả Track.
    /// </summary>
    public Dictionary<string, DateTime> GetPathTimestamps()
    {
        using var command = _connection.CreateCommand();
        command.CommandText = "SELECT file_path, file_modified_utc FROM tracks;";

        var map = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        using var reader = command.ExecuteReader();
        while (reader.Read())
            map[reader.GetString(0)] = new DateTime(reader.GetInt64(1), DateTimeKind.Utc);

        return map;
    }

    /// <summary>Xoá các bản ghi có đường dẫn không còn nằm trong <paramref name="existingPaths"/>.</summary>
    public int RemoveMissing(IReadOnlyCollection<string> existingPaths)
    {
        var keep = new HashSet<string>(existingPaths, StringComparer.OrdinalIgnoreCase);
        var doomed = GetPathTimestamps().Keys.Where(path => !keep.Contains(path)).ToList();
        if (doomed.Count == 0) return 0;

        using var transaction = _connection.BeginTransaction();
        foreach (var path in doomed)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = "DELETE FROM tracks WHERE file_path = $path;";
            command.Parameters.AddWithValue("$path", path);
            command.ExecuteNonQuery();
        }
        transaction.Commit();

        return doomed.Count;
    }

    public void Clear() => Execute("DELETE FROM tracks;");

    private static void Bind(SqliteCommand command, Track track)
    {
        command.Parameters.AddWithValue("$path", track.FilePath);
        command.Parameters.AddWithValue("$title", track.Title);
        command.Parameters.AddWithValue("$artist", (object?)track.Artist ?? DBNull.Value);
        command.Parameters.AddWithValue("$albumArtist", (object?)track.AlbumArtist ?? DBNull.Value);
        command.Parameters.AddWithValue("$album", (object?)track.Album ?? DBNull.Value);
        command.Parameters.AddWithValue("$trackNo", track.TrackNumber);
        command.Parameters.AddWithValue("$discNo", track.DiscNumber);
        command.Parameters.AddWithValue("$year", track.Year);
        command.Parameters.AddWithValue("$genre", (object?)track.Genre ?? DBNull.Value);
        command.Parameters.AddWithValue("$duration", track.Duration.Ticks);
        command.Parameters.AddWithValue("$bitrate", track.Bitrate);
        command.Parameters.AddWithValue("$sampleRate", track.SampleRate);
        command.Parameters.AddWithValue("$channels", track.Channels);
        command.Parameters.AddWithValue("$codec", (object?)track.Codec ?? DBNull.Value);
        command.Parameters.AddWithValue("$size", track.FileSize);
        command.Parameters.AddWithValue("$modified", track.FileModifiedUtc.Ticks);
        command.Parameters.AddWithValue("$added", track.DateAddedUtc.Ticks);
        command.Parameters.AddWithValue("$artwork", (object?)track.ArtworkHash ?? DBNull.Value);
    }

    private static Track Read(SqliteDataReader reader) => new()
    {
        Id = reader.GetInt64(0),
        FilePath = reader.GetString(1),
        Title = reader.GetString(2),
        Artist = reader.IsDBNull(3) ? null : reader.GetString(3),
        AlbumArtist = reader.IsDBNull(4) ? null : reader.GetString(4),
        Album = reader.IsDBNull(5) ? null : reader.GetString(5),
        TrackNumber = (uint)reader.GetInt64(6),
        DiscNumber = (uint)reader.GetInt64(7),
        Year = (uint)reader.GetInt64(8),
        Genre = reader.IsDBNull(9) ? null : reader.GetString(9),
        Duration = TimeSpan.FromTicks(reader.GetInt64(10)),
        Bitrate = reader.GetInt32(11),
        SampleRate = reader.GetInt32(12),
        Channels = reader.GetInt32(13),
        Codec = reader.IsDBNull(14) ? null : reader.GetString(14),
        FileSize = reader.GetInt64(15),
        FileModifiedUtc = new DateTime(reader.GetInt64(16), DateTimeKind.Utc),
        DateAddedUtc = new DateTime(reader.GetInt64(17), DateTimeKind.Utc),
        ArtworkHash = reader.IsDBNull(18) ? null : reader.GetString(18),
    };

    private void Execute(string sql)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    public void Dispose()
    {
        _connection.Close();
        _connection.Dispose();
    }
}
