using System;
using System.ComponentModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Cadence.App.ViewModels;
using Cadence.Core.Models;

namespace Cadence.App.Views;

public partial class MainWindow : Window
{
    /// <summary>Kích thước cửa sổ ở chế độ thu gọn, tính bằng đơn vị độc lập DPI.</summary>
    private const double MiniWidth = 420;
    private const double MiniHeight = 132;

    /// <summary>
    /// Kích thước và ràng buộc của cửa sổ trước khi thu gọn, để khôi phục nguyên
    /// trạng. Thiếu phần này thì mở rộng lại sẽ về kích thước mặc định, xoá mất
    /// kích thước mà người dùng đã tự chỉnh.
    /// </summary>
    private Size? _restoreSize;
    private WindowState _restoreState = WindowState.Normal;

    public MainWindow()
    {
        InitializeComponent();

        // Title bar do ta tự vẽ (xem ApplyWindowChrome) nên mọi hành vi của nó —
        // kéo cửa sổ, đóng, thu nhỏ, phóng to — phải tự nối lại bằng tay.
        TitleBar.PointerPressed += OnTitleBarPressed;
        CloseButton.Click += (_, _) => Close();
        MinimiseButton.Click += (_, _) => WindowState = WindowState.Minimized;
        ZoomButton.Click += OnZoomClicked;

        // Bố cục thu gọn có cụm điều khiển cửa sổ riêng: một control chỉ nằm được
        // ở một chỗ trong cây, nên không dùng lại cụm của bố cục đầy đủ được.
        MiniTitleBar.PointerPressed += OnTitleBarPressed;
        MiniCloseButton.Click += (_, _) => Close();
        MiniMinimiseButton.Click += (_, _) => WindowState = WindowState.Minimized;

        DataContextChanged += OnDataContextChanged;

        AddFolderButton.Click += OnAddFolderClicked;
        TrackList.DoubleTapped += OnTrackDoubleTapped;
        TrackList.KeyDown += OnTrackListKeyDown;

        // Nút caption mờ đi khi cửa sổ mất focus, giống hành vi của Windows.
        Activated += (_, _) => SetCaptionActive(true);
        Deactivated += (_, _) => SetCaptionActive(false);

        // Ký hiệu nút giữa phải đổi theo trạng thái: phóng to và khôi phục là hai
        // hành động khác nhau, hiện cùng một hình thì nút nói dối một nửa số lần.
        PropertyChanged += (_, e) =>
        {
            if (e.Property == WindowStateProperty) UpdateMaximiseGlyph();
        };
        UpdateMaximiseGlyph();

        ApplyWindowChrome();
    }

    /// <summary>
    /// Quyết định ai vẽ khung cửa sổ: hệ điều hành hay chúng ta.
    /// </summary>
    /// <remarks>
    /// Không thể đặt trong XAML vì câu trả lời khác nhau theo nền tảng, và đặt
    /// sai thì hỏng theo hai kiểu ngược nhau.
    /// <para>
    /// Trên Windows, <c>WindowDecorations.Full</c> để hệ điều hành tiếp tục vẽ
    /// title bar của nó. Cộng với <c>ExtendClientAreaToDecorationsHint</c>, nội
    /// dung của ta bị kéo lên nằm DƯỚI khung đó: chữ tiêu đề của Windows đè lên
    /// cụm đèn giao thông tự vẽ, và ba nút hệ thống đè lên ô tìm kiếm.
    /// <c>BorderOnly</c> bỏ title bar nhưng giữ viền — nên vẫn resize, vẫn snap,
    /// vẫn có bóng đổ, mà toàn bộ hàng trên cùng là của ta. Vẫn cần
    /// <c>ExtendClientAreaToDecorationsHint</c> kèm theo, vì cái viền còn lại đó
    /// vẫn được Windows vẽ bằng màu hệ thống.
    /// </para>
    /// <para>
    /// Trên macOS thì ngược lại: giữ <c>Full</c> để hệ điều hành vẽ đèn giao
    /// thông thật ở đúng vị trí và đúng hành vi, rồi ẩn cụm tự vẽ đi — nếu không
    /// sẽ thành sáu chấm chồng nhau. <c>BorderOnly</c> ở đây sẽ để lại một cửa
    /// sổ không có nút đóng nào cả.
    /// </para>
    /// </remarks>
    private void ApplyWindowChrome()
    {
        if (OperatingSystem.IsMacOS())
        {
            WindowDecorations = WindowDecorations.Full;
            ExtendClientAreaToDecorationsHint = true;

            // Hệ điều hành đã vẽ đèn giao thông thật ở góc trái. Ẩn cụm nút của
            // ta đi và chừa đúng chỗ cho chúng, nếu không tiêu đề căn giữa sẽ
            // lệch và nút hệ thống sẽ đè lên nội dung.
            CaptionCluster.IsVisible = false;
            MiniCaptionCluster.IsVisible = false;
            return;
        }

        WindowDecorations = WindowDecorations.BorderOnly;

        // Vẫn phải bật, dù đã BorderOnly.
        //
        // BorderOnly bỏ title bar nhưng GIỮ khung resize — và Windows vẫn vẽ
        // khung đó bằng màu hệ thống. Không kéo nội dung ra phủ lên thì client
        // area bắt đầu bên dưới khung, để lộ một dải xám mỏng chạy ngang phía
        // trên title bar tự vẽ, trông đúng như hai lớp giao diện chồng nhau.
        // Cũng chính dải đó đẩy ba nút caption thụt vào khỏi mép phải.
        ExtendClientAreaToDecorationsHint = true;

        // Không có đèn giao thông của hệ điều hành ở góc trái, nên không cần
        // chừa chỗ — bỏ khoảng đệm để tiêu đề nằm đúng giữa cửa sổ.
        MacTrafficSpacer.Width = 0;
        MiniMacTrafficSpacer.Width = 0;
    }

    private void SetCaptionActive(bool active)
    {
        foreach (var cluster in new[] { CaptionCluster, MiniCaptionCluster })
        {
            if (active) cluster.Classes.Add("active");
            else cluster.Classes.Remove("active");
        }
    }

    /// <summary>Ký hiệu Segoe cho phóng to và khôi phục.</summary>
    private const string MaximiseGlyph = "\uE922";
    private const string RestoreGlyph = "\uE923";

    private void UpdateMaximiseGlyph() =>
        ZoomButton.Content = WindowState == WindowState.Maximized ? RestoreGlyph : MaximiseGlyph;

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MainWindowViewModel.IsMiniMode)) return;
        if (DataContext is not MainWindowViewModel viewModel) return;

        ApplyMiniMode(viewModel.IsMiniMode);
    }

    /// <summary>
    /// Đổi hình dạng cửa sổ theo chế độ. Chỉ đụng tới cửa sổ — phần nội dung nào
    /// hiện ra là do binding IsVisible trong XAML lo.
    /// </summary>
    private void ApplyMiniMode(bool mini)
    {
        if (mini)
        {
            _restoreState = WindowState;
            _restoreSize = new Size(Width, Height);

            // Rời trạng thái phóng to trước khi đặt kích thước: cửa sổ đang
            // maximized sẽ bỏ qua Width/Height, và kết quả là một bố cục thu gọn
            // bị kéo giãn ra toàn màn hình.
            WindowState = WindowState.Normal;

            // Nới ràng buộc tối thiểu trước, nếu không MinWidth cũ (720) sẽ chặn
            // ngay lệnh đặt Width bên dưới.
            MinWidth = MiniWidth;
            MinHeight = MiniHeight;
            Width = MiniWidth;
            Height = MiniHeight;
            CanResize = false;

            // Nổi trên cùng: mục đích của chế độ này là xem được bài đang phát
            // trong lúc làm việc khác. Nằm dưới cửa sổ khác thì nó vô nghĩa.
            Topmost = true;
            return;
        }

        Topmost = false;
        CanResize = true;
        MinWidth = 720;
        MinHeight = 440;

        if (_restoreSize is { } size)
        {
            Width = size.Width;
            Height = size.Height;
        }

        WindowState = _restoreState;
    }

    private void OnTitleBarPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

        // Double-click lên title bar để phóng to/thu nhỏ — hành vi chuẩn của cả
        // macOS lẫn Windows, người dùng làm theo phản xạ.
        if (e.ClickCount == 2)
        {
            ToggleMaximised();
            return;
        }

        BeginMoveDrag(e);
    }

    private void OnZoomClicked(object? sender, RoutedEventArgs e) => ToggleMaximised();

    private void ToggleMaximised() =>
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;

    private async void OnAddFolderClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel) return;

        try
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Chọn thư mục nhạc",
                AllowMultiple = false,
            });

            var folder = folders.FirstOrDefault()?.TryGetLocalPath();
            if (!string.IsNullOrWhiteSpace(folder)) await viewModel.AddFolderAsync(folder);
        }
        catch (Exception error)
        {
            // Hộp thoại chọn file là API của hệ điều hành — có thể ném lỗi khi user
            // huỷ bằng cách lạ hoặc khi chạy trong môi trường không có shell.
            // Không được để nó làm sập app.
            viewModel.StatusMessage = $"Không mở được hộp thoại chọn thư mục: {error.Message}";
        }
    }

    private void OnTrackDoubleTapped(object? sender, TappedEventArgs e) => PlaySelected();

    /// <summary>
    /// Enter để phát bài đang chọn. Không phải để cho đẹp: dùng bàn phím duyệt danh
    /// sách dài nhanh hơn chuột nhiều, và mọi trình phát nhạc đều hỗ trợ phím này.
    /// </summary>
    private void OnTrackListKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Enter or Key.Return)) return;

        PlaySelected();
        e.Handled = true;
    }

    private void PlaySelected()
    {
        if (DataContext is not MainWindowViewModel viewModel) return;
        if (TrackList.SelectedItem is not Track track) return;

        viewModel.PlayTrackCommand.Execute(track);
    }
}
