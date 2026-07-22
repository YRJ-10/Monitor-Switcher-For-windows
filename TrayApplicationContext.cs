using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Windows.Interop;
using Microsoft.Win32;

namespace MonitorSwitcher;

public class TrayApplicationContext : ApplicationContext
{
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const int PopupMargin = 10;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int X,
        int Y,
        int cx,
        int cy,
        uint uFlags);

    private NotifyIcon trayIcon;
    private TrayUI trayUI;
    private LocalApiServer? localApiServer;

    public TrayApplicationContext()
    {
        // Initialize WPF within WinForms app
        if (System.Windows.Application.Current == null)
        {
            new System.Windows.Application();
        }

        trayUI = new TrayUI();

        trayIcon = new NotifyIcon()
        {
            Icon = CreateMonitorIcon(),
            Visible = true,
            Text = "Monitor Switcher"
        };

        trayIcon.MouseClick += TrayIcon_MouseClick;
        SystemEvents.DisplaySettingsChanged += SystemEvents_DisplaySettingsChanged;

        try
        {
            localApiServer = new LocalApiServer();
            localApiServer.Start();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Local API failed to start: {ex.Message}", "Monitor Switcher", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void TrayIcon_MouseClick(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left || e.Button == MouseButtons.Right)
        {
            Screen primaryScreen = Screen.PrimaryScreen ?? Screen.FromPoint(Cursor.Position);
            if (trayUI.IsVisible && IsWindowOnScreen(primaryScreen))
            {
                trayUI.Hide();
            }
            else
            {
                trayUI.RefreshProfiles();
                PositionOnPrimaryScreen(primaryScreen);
                if (!trayUI.IsVisible)
                {
                    trayUI.Show();
                }
                trayUI.Activate();
            }
        }
    }

    private void PositionOnPrimaryScreen(Screen primaryScreen)
    {
        IntPtr windowHandle = new WindowInteropHelper(trayUI).EnsureHandle();
        if (!GetWindowRect(windowHandle, out RECT windowRect))
        {
            return;
        }

        int width = windowRect.Right - windowRect.Left;
        int height = windowRect.Bottom - windowRect.Top;
        Rectangle workingArea = primaryScreen.WorkingArea;
        int x = Math.Max(workingArea.Left, workingArea.Right - width - PopupMargin);
        int y = Math.Max(workingArea.Top, workingArea.Bottom - height - PopupMargin);

        SetWindowPos(
            windowHandle,
            IntPtr.Zero,
            x,
            y,
            0,
            0,
            SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
    }

    private bool IsWindowOnScreen(Screen screen)
    {
        IntPtr windowHandle = new WindowInteropHelper(trayUI).EnsureHandle();
        if (!GetWindowRect(windowHandle, out RECT windowRect))
        {
            return false;
        }

        Rectangle windowBounds = Rectangle.FromLTRB(
            windowRect.Left,
            windowRect.Top,
            windowRect.Right,
            windowRect.Bottom);
        return screen.WorkingArea.IntersectsWith(windowBounds);
    }

    private void SystemEvents_DisplaySettingsChanged(object? sender, EventArgs e)
    {
        trayUI.Dispatcher.BeginInvoke(() => trayUI.Hide());
    }

    private Icon CreateMonitorIcon()
    {
        int size = 32;
        Bitmap bmp = new Bitmap(size, size);
        using (Graphics g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            // Draw monitor bezel
            using (Brush bezelBrush = new SolidBrush(Color.White))
            {
                g.FillRoundedRectangle(bezelBrush, 2, 4, 28, 18, 2);
            }

            // Draw monitor screen
            using (Brush screenBrush = new SolidBrush(Color.Black))
            {
                g.FillRectangle(screenBrush, 4, 6, 24, 14);
            }

            // Draw stand and base
            using (Brush standBrush = new SolidBrush(Color.LightGray))
            {
                g.FillRectangle(standBrush, 14, 22, 4, 6); // stand
                g.FillRoundedRectangle(standBrush, 8, 28, 16, 2, 1); // base
            }
        }

        IntPtr hIcon = bmp.GetHicon();
        return Icon.FromHandle(hIcon);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            SystemEvents.DisplaySettingsChanged -= SystemEvents_DisplaySettingsChanged;
            localApiServer?.Dispose();
            trayIcon.Dispose();
        }

        base.Dispose(disposing);
    }
}

// Extension method to draw rounded rectangle for the icon
public static class GraphicsExtension
{
    public static void FillRoundedRectangle(this Graphics g, Brush brush, float x, float y, float width, float height, float radius)
    {
        System.Drawing.Drawing2D.GraphicsPath path = new System.Drawing.Drawing2D.GraphicsPath();
        path.AddArc(x, y, radius * 2, radius * 2, 180, 90);
        path.AddArc(x + width - (radius * 2), y, radius * 2, radius * 2, 270, 90);
        path.AddArc(x + width - (radius * 2), y + height - (radius * 2), radius * 2, radius * 2, 0, 90);
        path.AddArc(x, y + height - (radius * 2), radius * 2, radius * 2, 90, 90);
        path.CloseFigure();
        g.FillPath(brush, path);
    }
}
