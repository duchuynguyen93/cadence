using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Cadence.Core.Models;

namespace Cadence.App.Converters;

public static class AppConverters
{
    /// <summary>TimeSpan -> "3:45". Binding thẳng TimeSpan sẽ ra "00:03:45.000" — không dùng được.</summary>
    public static readonly IValueConverter Duration =
        new FuncValueConverter<TimeSpan, string>(time =>
            time.TotalHours >= 1
                ? $"{(int)time.TotalHours}:{time.Minutes:00}:{time.Seconds:00}"
                : $"{time.Minutes}:{time.Seconds:00}");

    /// <summary>0 -> chuỗi rỗng. Số track/năm bằng 0 nghĩa là "không có dữ liệu", đừng hiện số 0.</summary>
    public static readonly IValueConverter BlankIfZero =
        new FuncValueConverter<uint, string>(value => value == 0 ? string.Empty : value.ToString());

    /// <summary>Dùng để tô sáng nút lặp khi đang bật (bất kể All hay One).</summary>
    public static readonly IValueConverter IsRepeatOn =
        new FuncValueConverter<RepeatMode, bool>(mode => mode != RepeatMode.Off);

    public static readonly IValueConverter IsRepeatOne =
        new FuncValueConverter<RepeatMode, bool>(mode => mode == RepeatMode.One);

    /// <summary>Bitrate kbps -> "320 kbps", rỗng nếu không rõ.</summary>
    public static readonly IValueConverter Bitrate =
        new FuncValueConverter<int, string>(value => value <= 0 ? string.Empty : $"{value} kbps");
}
