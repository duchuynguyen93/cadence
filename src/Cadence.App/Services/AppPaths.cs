using System;
using System.IO;

namespace Cadence.App.Services;

/// <summary>
/// Nơi app cất dữ liệu.
///
/// Dùng ApplicationData (trên Windows là %APPDATA%\Cadence) chứ không đặt cạnh file .exe:
/// thư mục Program Files không cho ghi với quyền user thường, và để cạnh exe thì
/// nâng cấp/gỡ app sẽ cuốn theo cả thư viện nhạc đã index.
/// </summary>
public static class AppPaths
{
    public static string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Cadence");

    public static string DatabaseFile => Path.Combine(Root, "library.db");
    public static string ArtworkCache => Path.Combine(Root, "artwork");
    public static string SettingsFile => Path.Combine(Root, "settings.json");

    public static void EnsureCreated()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(ArtworkCache);
    }
}
