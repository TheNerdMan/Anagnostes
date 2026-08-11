using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Anagnostes.ViewModels;
using Anagnostes.Services;

namespace Anagnostes;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _vm = new MainViewModel();
        _vm.PropertyChanged += OnVmPropertyChanged;
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(MainViewModel.TtsState) or nameof(MainViewModel.IsPlaying)))
            return;

        if (PlayStateIndicator == null) return;
        PlayStateIndicator.Text = _vm.TtsState switch
        {
            TtsState.Speaking => "▶ ",
            TtsState.Paused   => "⏸ ",
            TtsState.Loading  => "⟳ ",
            _                 => "■ ",
        };
    }

    protected override void OnClosed(System.EventArgs e)
    {
        _vm.PropertyChanged -= OnVmPropertyChanged;
        _vm.Dispose();
        base.OnClosed(e);
    }

    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
        => BeginMoveDrag(e);

    private void CloseButton_Click(object? sender, RoutedEventArgs e)
        => Close();

    private void MinimizeButton_Click(object? sender, RoutedEventArgs e)
        => WindowState = WindowState.Minimized;

    private void MaximizeButton_Click(object? sender, RoutedEventArgs e)
        => WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
}
