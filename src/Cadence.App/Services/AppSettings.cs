using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Cadence.App.Services;

/// <summary>
/// Cấu hình người dùng, lưu ra JSON. Cố tình KHÔNG dùng SQLite cho phần này —
/// file text sửa tay được khi cần gỡ rối, và nó nhỏ tới mức mọi thứ khác đều thừa.
/// </summary>
public sealed class AppSettings
{
    public List<string> MusicFolders { get; set; } = [];

    /// <summary>0.0–1.0.</summary>
    public float Volume { get; set; } = 0.7f;

    public bool Shuffle { get; set; }

    /// <summary>Lưu dạng chuỗi để file JSON còn đọc được bằng mắt.</summary>
    public string RepeatMode { get; set; } = "Off";

    [JsonIgnore]
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(AppPaths.SettingsFile)) return new AppSettings();

            var json = File.ReadAllText(AppPaths.SettingsFile);
            return JsonSerializer.Deserialize<AppSettings>(json, Options) ?? new AppSettings();
        }
        catch (Exception)
        {
            // File cấu hình hỏng không được ngăn app khởi động — quay về mặc định.
            // Lần Save kế tiếp sẽ ghi đè bản hỏng đó.
            return new AppSettings();
        }
    }

    public void Save()
    {
        try
        {
            AppPaths.EnsureCreated();

            // Ghi file tạm rồi move: mất điện giữa chừng vẫn còn nguyên bản cũ,
            // thay vì để lại một file JSON cụt không parse được.
            var temp = AppPaths.SettingsFile + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(this, Options));
            File.Move(temp, AppPaths.SettingsFile, overwrite: true);
        }
        catch (Exception)
        {
            // Không ghi được cấu hình thì thôi — không đáng để làm sập app.
        }
    }
}
