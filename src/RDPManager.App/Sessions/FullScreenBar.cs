using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace RDPManager.App.Sessions;

/// <summary>
/// The full-screen drop-down bar (DESIGN: Agreed layout, owner decision 2026-11-05). It is a separate
/// top-most window because nothing in Avalonia can draw over the RDP control. It appears when the mouse
/// touches the top edge of the full-screen monitor, shows briefly on entering full screen, and hides about
/// a second after the mouse leaves it.
/// </summary>
public sealed class FullScreenBar : Window
{
    private const int EdgePixels = 2;
    private static readonly TimeSpan HideDelay = TimeSpan.FromMilliseconds(1000);
    private static readonly TimeSpan IntroDuration = TimeSpan.FromSeconds(2);

    private readonly PixelRect _screen;
    private readonly DispatcherTimer _timer;
    private DateTime _hideAfter = DateTime.MaxValue;

    public FullScreenBar(string title, PixelRect screen, Action exitFullScreen, Action minimise, Action disconnect, Action close)
    {
        _screen = screen;
        WindowDecorations = Avalonia.Controls.WindowDecorations.None;
        Topmost = true;
        ShowInTaskbar = false;
        CanResize = false;
        ShowActivated = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.Manual;

        Button Make(string text, Action action)
        {
            var button = new Button { Content = text };
            button.Click += (_, _) => action();
            return button;
        }

        Content = new Border
        {
            Padding = new Thickness(10, 6),
            BorderThickness = new Thickness(1, 0, 1, 1),
            [!Border.BorderBrushProperty] = new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("DropTargetBorder"),
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Children =
                {
                    new TextBlock
                    {
                        Text = title,
                        VerticalAlignment = VerticalAlignment.Center,
                        FontWeight = FontWeight.SemiBold,
                        Margin = new Thickness(0, 0, 8, 0),
                    },
                    Make("Exit full screen", exitFullScreen),
                    Make("Minimise", minimise),
                    Make("Disconnect", disconnect),
                    Make("Close session", close),
                },
            },
        };

        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(120), DispatcherPriority.Background, (_, _) => Track());
    }

    /// <summary>True while the full-screen window is minimised: the bar stays hidden.</summary>
    public bool Paused { get; set; }

    /// <summary>Starts tracking the mouse and shows the bar briefly so the user knows it is there.</summary>
    public void Start()
    {
        Reveal();
        _hideAfter = DateTime.UtcNow + IntroDuration;
        _timer.Start();
    }

    public void Stop()
    {
        _timer.Stop();
        Close();
    }

    private void Reveal()
    {
        if (!IsVisible)
        {
            Show();
        }

        var width = (int)(Bounds.Width * RenderScaling);
        Position = new PixelPoint(_screen.X + Math.Max(0, (_screen.Width - width) / 2), _screen.Y);
    }

    private void Track()
    {
        if (Paused)
        {
            if (IsVisible)
            {
                Hide();
            }

            return;
        }

        if (!GetCursorPos(out var cursor))
        {
            return;
        }

        var point = new PixelPoint(cursor.X, cursor.Y);
        var atTopEdge = point.X >= _screen.X && point.X < _screen.Right && point.Y >= _screen.Y && point.Y <= _screen.Y + EdgePixels;
        var overBar = IsVisible && BarRect().Contains(point);

        if (atTopEdge || overBar)
        {
            Reveal();
            _hideAfter = DateTime.MaxValue;
            return;
        }

        if (!IsVisible)
        {
            return;
        }

        if (_hideAfter == DateTime.MaxValue)
        {
            _hideAfter = DateTime.UtcNow + HideDelay;
        }
        else if (DateTime.UtcNow >= _hideAfter)
        {
            Hide();
            _hideAfter = DateTime.MaxValue;
        }
    }

    private PixelRect BarRect()
    {
        var width = (int)(Bounds.Width * RenderScaling);
        var height = (int)(Bounds.Height * RenderScaling);
        return new PixelRect(Position.X, Position.Y, width, height + 8);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CursorPoint
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out CursorPoint point);
}
