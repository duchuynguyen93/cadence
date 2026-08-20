using System.Runtime.InteropServices;

namespace Cadence.Audio;

/// <summary>
/// Nạp thư viện native của BASS và các plugin codec.
///
/// ManagedBass trên NuGet CHỈ là lớp wrapper P/Invoke — nó không chứa bass.dll.
/// Native binary phải tải riêng từ un4seen.com (xem scripts/fetch-bass.sh) và
/// nằm cạnh file .exe khi chạy.
/// </summary>
public static class BassNativeLoader
{
    private static readonly Lock Gate = new();
    private static bool _loaded;
    private static readonly List<int> PluginHandles = [];

    /// <summary>
    /// Tên plugin (không kèm đuôi/tiền tố) cần nạp để mở rộng codec.
    /// BASS lõi đã có sẵn MP3/MP4/AAC/WAV/AIFF; FLAC phải cần plugin.
    /// </summary>
    private static readonly string[] Plugins = ["bassflac"];

    /// <summary>Các plugin đã nạp thành công — hiện ở màn hình About/Settings.</summary>
    public static IReadOnlyList<string> LoadedPlugins { get; private set; } = [];

    /// <summary>
    /// Idempotent — gọi nhiều lần vẫn an toàn.
    /// </summary>
    /// <exception cref="DllNotFoundException">Không tìm thấy thư viện native của BASS.</exception>
    public static void EnsureLoaded()
    {
        lock (Gate)
        {
            if (_loaded) return;

            bool initialised;
            try
            {
                initialised = ManagedBass.Bass.Init();
            }
            catch (DllNotFoundException inner)
            {
                // Thiếu hẳn file thư viện thì P/Invoke ném DllNotFoundException NGAY,
                // Bass.Init không kịp trả về gì cả. Trước đây chỉ kiểm tra giá trị trả về
                // nên nhánh này lọt lưới, và người dùng nhận nguyên một bãi log dlopen
                // dài hai màn hình thay vì câu "chạy fetch-bass.sh đi".
                throw new InvalidOperationException(
                    $"Không tìm thấy thư viện native của BASS ({NativeLibraryName}). " +
                    "Nó phải nằm cùng thư mục với file thực thi — chạy scripts/fetch-bass.sh. " +
                    "Lưu ý: chỉ project thực thi mới copy native lib; project nào tham chiếu " +
                    "Cadence.Audio cũng phải tự lo phần này.", inner);
            }

            if (!initialised)
            {
                var error = ManagedBass.Bass.LastError;

                // Already initialised nghĩa là có code khác đã init trước — không phải lỗi.
                if (error != ManagedBass.Errors.Already)
                {
                    throw new InvalidOperationException(
                        $"Không khởi tạo được BASS (lỗi: {error}). " +
                        "Thường là do không có thiết bị âm thanh khả dụng.");
                }
            }

            var loaded = new List<string>();
            foreach (var plugin in Plugins)
            {
                // BASS_PluginLoad nhận tên file; tự tìm trong thư mục của process.
                var handle = ManagedBass.Bass.PluginLoad(PluginFileName(plugin));
                if (handle != 0)
                {
                    PluginHandles.Add(handle);
                    loaded.Add(plugin);
                }
                // Thiếu plugin không phải lỗi chết người: mất codec đó thôi,
                // các định dạng còn lại vẫn phát bình thường.
            }

            LoadedPlugins = loaded;
            _loaded = true;
        }
    }

    public static void Unload()
    {
        lock (Gate)
        {
            if (!_loaded) return;

            foreach (var handle in PluginHandles) ManagedBass.Bass.PluginFree(handle);
            PluginHandles.Clear();
            ManagedBass.Bass.Free();
            _loaded = false;
        }
    }

    private static string NativeLibraryName =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "bass.dll"
        : RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "libbass.dylib"
        : "libbass.so";

    private static string PluginFileName(string name) =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? $"{name}.dll"
        : RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? $"lib{name}.dylib"
        : $"lib{name}.so";
}
