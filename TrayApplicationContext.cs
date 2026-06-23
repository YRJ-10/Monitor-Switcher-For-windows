using System;
using System.IO;
using System.Windows.Forms;
using System.Drawing;

namespace MonitorSwitcher;

public class TrayApplicationContext : ApplicationContext
{
    private NotifyIcon trayIcon;
    private ContextMenuStrip contextMenu;

    public TrayApplicationContext()
    {
        contextMenu = new ContextMenuStrip();
        BuildMenu();

        trayIcon = new NotifyIcon()
        {
            Icon = SystemIcons.Application,
            ContextMenuStrip = contextMenu,
            Visible = true,
            Text = "Monitor Switcher"
        };
    }

    private void BuildMenu()
    {
        contextMenu.Items.Clear();

        // Title
        ToolStripMenuItem titleItem = new ToolStripMenuItem("Monitor Switcher") { Enabled = false };
        contextMenu.Items.Add(titleItem);
        contextMenu.Items.Add(new ToolStripSeparator());

        // Load configs from current directory
        string[] configFiles = Directory.GetFiles(Directory.GetCurrentDirectory(), "*.config");
        if (configFiles.Length > 0)
        {
            foreach (string file in configFiles)
            {
                string profileName = Path.GetFileNameWithoutExtension(file);
                ToolStripMenuItem profileItem = new ToolStripMenuItem(profileName, null, (s, e) => LoadProfile(profileName));
                contextMenu.Items.Add(profileItem);
            }
            contextMenu.Items.Add(new ToolStripSeparator());
        }

        // Save Current Profile
        contextMenu.Items.Add(new ToolStripMenuItem("Save Current Profile...", null, SaveProfile));

        // Refresh Menu
        contextMenu.Items.Add(new ToolStripMenuItem("Refresh List", null, (s, e) => BuildMenu()));
        
        contextMenu.Items.Add(new ToolStripSeparator());

        // Exit
        contextMenu.Items.Add(new ToolStripMenuItem("Exit", null, Exit));
    }

    private void LoadProfile(string profileName)
    {
        try
        {
            string profilePath = Path.Combine(Directory.GetCurrentDirectory(), profileName + ".config");
            DisplayManager.LoadProfile(profilePath);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error loading profile: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void SaveProfile(object? sender, EventArgs e)
    {
        Form prompt = new Form()
        {
            Width = 300,
            Height = 150,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            Text = "Save Profile",
            StartPosition = FormStartPosition.CenterScreen,
            TopMost = true
        };
        Label textLabel = new Label() { Left = 20, Top = 20, Text = "Enter profile name:" };
        TextBox inputBox = new TextBox() { Left = 20, Top = 50, Width = 240 };
        Button confirmation = new Button() { Text = "Save", Left = 160, Width = 100, Top = 80, DialogResult = DialogResult.OK };
        
        prompt.Controls.Add(textLabel);
        prompt.Controls.Add(inputBox);
        prompt.Controls.Add(confirmation);
        prompt.AcceptButton = confirmation;

        if (prompt.ShowDialog() == DialogResult.OK)
        {
            string profileName = inputBox.Text.Trim();
            if (!string.IsNullOrEmpty(profileName))
            {
                try
                {
                    string profilePath = Path.Combine(Directory.GetCurrentDirectory(), profileName + ".config");
                    DisplayManager.SaveProfile(profilePath);
                    BuildMenu(); // Refresh menu
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error saving profile: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }
    }

    private void Exit(object? sender, EventArgs e)
    {
        trayIcon.Visible = false;
        Application.Exit();
    }
}
