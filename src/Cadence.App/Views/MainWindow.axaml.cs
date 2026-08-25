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

        // Khung cửa sổ gốc đã bị gỡ (ExtendClientAreaChromeHints="NoChrome") nên
        // toàn bộ hành vi của title bar phải tự nối lại bằng tay.
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

        // Đèn giao thông xám đi khi cửa sổ mất focus — chi tiết nhỏ nhưng thiếu nó
        // thì cảm giác "macOS" mất ngay.
        Activated += (_, _) => SetTrafficActive(true);
        Deactivated += (_, _) => SetTrafficActive(false);

        // Trên macOS hệ điều hành đã tự vẽ đèn giao thông thật ở đúng chỗ đó, nên
        // cụm của ta sẽ chồng lên thành 6 chấm. App này ship cho Windows — bộ đèn
        // tự vẽ chỉ cần thiết ở đó — nên ẩn đi khi chạy trên Mac để lúc dev nhìn
        // vẫn giống thành phẩm.
        if (OperatingSystem.IsMacOS())
        {
            TrafficCluster.IsVisible = false;
            MiniTrafficCluster.IsVisible = false;
        }
    }

    private void SetTrafficActive(bool active)
    {
        foreach (var cluster in new[] { TrafficCluster, MiniTrafficCluster })
        {
            if (active) cluster.Classes.Add("active");
            else cluster.Classes.Remove("active");
        }
    }

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
