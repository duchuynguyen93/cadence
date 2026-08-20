using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Cadence.App.ViewModels;
using Cadence.Core.Models;

namespace Cadence.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        // Khung cửa sổ gốc đã bị gỡ (ExtendClientAreaChromeHints="NoChrome") nên
        // toàn bộ hành vi của title bar phải tự nối lại bằng tay.
        TitleBar.PointerPressed += OnTitleBarPressed;
        CloseButton.Click += (_, _) => Close();
        MinimiseButton.Click += (_, _) => WindowState = WindowState.Minimized;
        ZoomButton.Click += OnZoomClicked;

        AddFolderButton.Click += OnAddFolderClicked;
        TrackList.DoubleTapped += OnTrackDoubleTapped;
        TrackList.KeyDown += OnTrackListKeyDown;

        // Đèn giao thông xám đi khi cửa sổ mất focus — chi tiết nhỏ nhưng thiếu nó
        // thì cảm giác "macOS" mất ngay.
        Activated += (_, _) => TrafficCluster.Classes.Add("active");
        Deactivated += (_, _) => TrafficCluster.Classes.Remove("active");

        // Trên macOS hệ điều hành đã tự vẽ đèn giao thông thật ở đúng chỗ đó, nên
        // cụm của ta sẽ chồng lên thành 6 chấm. App này ship cho Windows — bộ đèn
        // tự vẽ chỉ cần thiết ở đó — nên ẩn đi khi chạy trên Mac để lúc dev nhìn
        // vẫn giống thành phẩm.
        if (OperatingSystem.IsMacOS()) TrafficCluster.IsVisible = false;
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
