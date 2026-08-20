using System;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Cadence.App.Services;

namespace Cadence.App.Controls;

/// <summary>
/// Ô ảnh bìa: nhận hash, tự nạp ảnh bất đồng bộ, tự vẽ ô giữ chỗ khi chưa có ảnh.
///
/// Vì sao là control tự vẽ chứ không phải <c>Image</c> + converter: ListBox tái sử dụng
/// (recycle) các hàng khi cuộn, nên cùng một control sẽ liên tục bị gán hash khác nhau.
/// Cần theo dõi để kết quả của lần nạp cũ — về sau khi hàng đã hiển thị bài khác —
/// không ghi đè lên ảnh đúng. Đó là việc <see cref="_generation"/> làm.
/// </summary>
public sealed class ArtworkImage : Control
{
    /// <summary>Đặt một lần lúc khởi động ứng dụng, trước khi có control nào được tạo.</summary>
    public static ArtworkService? Service { get; set; }

    public static readonly StyledProperty<string?> HashProperty =
        AvaloniaProperty.Register<ArtworkImage, string?>(nameof(Hash));

    public static readonly StyledProperty<double> RadiusProperty =
        AvaloniaProperty.Register<ArtworkImage, double>(nameof(Radius), 4.0);

    /// <summary>
    /// Bề rộng (pixel) để giải mã ảnh. Nên đặt bằng cỡ hiển thị thực nhân hệ số DPI —
    /// đặt lớn hơn là phí RAM, nhỏ hơn là ảnh bị vỡ.
    /// </summary>
    public static readonly StyledProperty<int> DecodeWidthProperty =
        AvaloniaProperty.Register<ArtworkImage, int>(nameof(DecodeWidth), 64);

    private Bitmap? _bitmap;

    /// <summary>
    /// Tăng lên mỗi lần Hash đổi. Tác vụ nạp bất đồng bộ ghi nhớ giá trị lúc nó bắt đầu
    /// và chỉ áp dụng kết quả nếu con số chưa đổi — nếu hàng đã bị recycle sang bài khác
    /// thì kết quả cũ bị bỏ đi.
    /// </summary>
    private int _generation;

    static ArtworkImage()
    {
        AffectsRender<ArtworkImage>(HashProperty, RadiusProperty);
        HashProperty.Changed.AddClassHandler<ArtworkImage>((control, _) => control.BeginLoad());
    }

    public string? Hash
    {
        get => GetValue(HashProperty);
        set => SetValue(HashProperty, value);
    }

    public double Radius
    {
        get => GetValue(RadiusProperty);
        set => SetValue(RadiusProperty, value);
    }

    public int DecodeWidth
    {
        get => GetValue(DecodeWidthProperty);
        set => SetValue(DecodeWidthProperty, value);
    }

    private async void BeginLoad()
    {
        var generation = Interlocked.Increment(ref _generation);

        // Xoá ảnh cũ ngay lập tức. Không làm vậy thì lúc cuộn, hàng vừa được tái sử
        // dụng sẽ hiện ảnh của bài trước đó trong vài chục ms — nhìn như ảnh nhảy lung tung.
        _bitmap = null;
        InvalidateVisual();

        var hash = Hash;
        if (string.IsNullOrEmpty(hash) || Service is null) return;

        var bitmap = await Service.GetAsync(hash, DecodeWidth);

        // Hàng đã chuyển sang bài khác trong lúc chờ — vứt kết quả này đi.
        if (Volatile.Read(ref _generation) != generation) return;

        _bitmap = bitmap;
        Dispatcher.UIThread.Post(InvalidateVisual);
    }

    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        if (bounds.Width <= 0 || bounds.Height <= 0) return;

        var radius = Math.Min(Radius, Math.Min(bounds.Width, bounds.Height) / 2);
        using var _ = context.PushClip(new RoundedRect(bounds, radius));

        if (_bitmap is null)
        {
            DrawPlaceholder(context, bounds);
            return;
        }

        context.DrawImage(_bitmap, CropToFill(_bitmap.PixelSize, bounds), bounds);
    }

    /// <summary>
    /// Vùng cần cắt từ ảnh gốc để lấp đầy ô mà không méo hình.
    ///
    /// Ảnh bìa thường vuông nên phần lớn trường hợp đây là toàn bộ ảnh, nhưng ảnh quét
    /// từ bìa đĩa vinyl hoặc ảnh nghệ sĩ hay bị chữ nhật. Kéo giãn cho vừa ô sẽ méo mặt
    /// người thấy rõ, nên cắt bớt hai bên là lựa chọn đúng.
    /// </summary>
    private static Rect CropToFill(PixelSize source, Rect destination)
    {
        var sourceRatio = (double)source.Width / source.Height;
        var destinationRatio = destination.Width / destination.Height;

        if (Math.Abs(sourceRatio - destinationRatio) < 0.001)
            return new Rect(0, 0, source.Width, source.Height);

        if (sourceRatio > destinationRatio)
        {
            // Ảnh rộng hơn ô: cắt hai bên trái/phải.
            var width = source.Height * destinationRatio;
            return new Rect((source.Width - width) / 2, 0, width, source.Height);
        }

        // Ảnh cao hơn ô: cắt trên/dưới.
        var height = source.Width / destinationRatio;
        return new Rect(0, (source.Height - height) / 2, source.Width, height);
    }

    /// <summary>
    /// Ô giữ chỗ: nền mờ cùng một nốt nhạc. Vẽ bằng hình học thay vì dùng font ký tự
    /// để không phụ thuộc vào việc máy đích có font chứa emoji nốt nhạc hay không —
    /// trên Windows sạch, ký tự thiếu font sẽ hiện thành ô vuông rỗng.
    /// </summary>
    private void DrawPlaceholder(DrawingContext context, Rect bounds)
    {
        var background = this.FindResource("ElevatedBackground") as IBrush
                         ?? new SolidColorBrush(Color.FromArgb(255, 44, 44, 46));
        var foreground = this.FindResource("TextTertiary") as IBrush
                         ?? new SolidColorBrush(Color.FromArgb(80, 255, 255, 255));

        context.DrawRectangle(background, null, bounds);

        // Nốt nhạc co giãn theo cỡ ô, canh giữa.
        var scale = Math.Min(bounds.Width, bounds.Height) / 24.0;
        if (scale <= 0) return;

        var stemHeight = 11 * scale;
        var headRadius = 2.6 * scale;
        var centre = bounds.Center;

        var stemX = centre.X + headRadius * 0.9;
        var stemTop = centre.Y - stemHeight / 2;
        var stemBottom = centre.Y + stemHeight / 2 - headRadius;

        var pen = new Pen(foreground, Math.Max(1, 1.4 * scale));
        context.DrawLine(pen, new Point(stemX, stemTop), new Point(stemX, stemBottom));
        context.DrawEllipse(foreground, null,
            new Point(stemX - headRadius * 0.9, stemBottom), headRadius, headRadius * 0.8);
    }
}
