using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace Anagnostes.Controls;

/// <summary>
/// Renders animated equalizer bars resembling the classic WinAMP equalizer visualiser.
/// When <see cref="IsActive"/> is true the bars animate; otherwise they decay to zero.
/// </summary>
public partial class EqBarsControl : UserControl
{
    private const int   BarCount   = 18;
    private const int   BarWidth   = 8;
    private const int   BarGap     = 4;
    private const float EqMaxHeight = 38f;

    private readonly float[]         _heights  = new float[BarCount];
    private readonly float[]         _targets  = new float[BarCount];
    private readonly float[]         _velocity = new float[BarCount];
    private readonly DispatcherTimer _timer;
    private readonly Random          _rng = new();

    // WinAMP palette: green → yellow → orange → red
    private static readonly IBrush[] BarBrushes =
    [
        new SolidColorBrush(Color.FromRgb(0x18, 0xC4, 0x28)),
        new SolidColorBrush(Color.FromRgb(0x6C, 0xE0, 0x18)),
        new SolidColorBrush(Color.FromRgb(0xB8, 0xE8, 0x10)),
        new SolidColorBrush(Color.FromRgb(0xE8, 0xC0, 0x10)),
        new SolidColorBrush(Color.FromRgb(0xF0, 0x80, 0x10)),
        new SolidColorBrush(Color.FromRgb(0xF0, 0x40, 0x10)),
    ];

    public static readonly StyledProperty<bool> IsActiveProperty =
        AvaloniaProperty.Register<EqBarsControl, bool>(nameof(IsActive));

    public bool IsActive
    {
        get => GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }

    public EqBarsControl()
    {
        InitializeComponent();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _timer.Tick += OnTick;
        _timer.Start();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        var canvas = BarsCanvas;
        if (canvas == null) return;

        var totalWidth = BarCount * (BarWidth + BarGap) - BarGap;
        var startX     = (canvas.Bounds.Width - totalWidth) / 2.0;

        // Update targets
        for (int i = 0; i < BarCount; i++)
        {
            if (IsActive)
            {
                if (_rng.NextDouble() < 0.35)
                    _targets[i] = (float)(_rng.NextDouble() * EqMaxHeight);
            }
            else
            {
                _targets[i] = 0;
            }

            // Spring physics towards target
            var diff = _targets[i] - _heights[i];
            _velocity[i] = _velocity[i] * 0.55f + diff * 0.20f;
            _heights[i]  = Math.Clamp(_heights[i] + _velocity[i], 0, EqMaxHeight);
        }

        // Redraw
        canvas.Children.Clear();
        var canvasHeight = canvas.Bounds.Height;
        if (canvasHeight <= 0) canvasHeight = EqMaxHeight;

        for (int i = 0; i < BarCount; i++)
        {
            var h = Math.Max(2f, _heights[i]);
            var brushIndex = (int)Math.Round((h / EqMaxHeight) * (BarBrushes.Length - 1));
            brushIndex = Math.Clamp(brushIndex, 0, BarBrushes.Length - 1);

            var rect = new Avalonia.Controls.Shapes.Rectangle
            {
                Width  = BarWidth,
                Height = h,
                Fill   = BarBrushes[brushIndex],
            };
            Canvas.SetLeft(rect, startX + i * (BarWidth + BarGap));
            Canvas.SetTop(rect, canvasHeight - h);   // grow upward from bottom
            canvas.Children.Add(rect);
        }
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        OnTick(null, EventArgs.Empty);
    }
}
