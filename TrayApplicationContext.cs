using System;
using System.Drawing;
using System.Windows.Forms;

namespace MonitorSwitcher;

public class TrayApplicationContext : ApplicationContext
{
    private NotifyIcon trayIcon;
    private TrayUI trayUI;

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
    }

    private void TrayIcon_MouseClick(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left || e.Button == MouseButtons.Right)
        {
            if (trayUI.IsVisible)
            {
                trayUI.Hide();
            }
            else
            {
                trayUI.RefreshProfiles();
                
                // Position the window near the tray
                var cursorPosition = Cursor.Position;
                var screen = Screen.FromPoint(cursorPosition);
                
                trayUI.Left = screen.WorkingArea.Right - trayUI.Width - 10;
                trayUI.Top = screen.WorkingArea.Bottom - trayUI.Height - 10;
                
                trayUI.Show();
                trayUI.Activate();
            }
        }
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
