using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;

namespace MonitorSwitcher
{
    public partial class TrayUI : Window
    {
        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        private const int DWMWA_SYSTEMBACKDROP_TYPE = 38;
        private const int DWMSBT_TRANSIENTWINDOW = 3; // Acrylic

        public TrayUI()
        {
            InitializeComponent();
            Loaded += TrayUI_Loaded;
        }

        private void TrayUI_Loaded(object sender, RoutedEventArgs e)
        {
            // Enable Acrylic Glassmorphism on Windows 11
            var interopHelper = new System.Windows.Interop.WindowInteropHelper(this);
            int backdropType = DWMSBT_TRANSIENTWINDOW;
            DwmSetWindowAttribute(interopHelper.Handle, DWMWA_SYSTEMBACKDROP_TYPE, ref backdropType, sizeof(int));
        }

        public void RefreshProfiles()
        {
            ProfilesPanel.Children.Clear();
            string[] configFiles = Directory.GetFiles(Directory.GetCurrentDirectory(), "*.config");
            foreach (string file in configFiles)
            {
                string profileName = Path.GetFileNameWithoutExtension(file);
                
                System.Windows.Controls.Grid grid = new System.Windows.Controls.Grid() { Margin = new Thickness(0, 2, 0, 2) };
                grid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition() { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition() { Width = GridLength.Auto });
                grid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition() { Width = GridLength.Auto });

                System.Windows.Controls.Button btnLoad = new System.Windows.Controls.Button() { Content = profileName, HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch, HorizontalContentAlignment = System.Windows.HorizontalAlignment.Left };
                btnLoad.Click += (s, e) => {
                    try { DisplayManager.LoadProfile(file); }
                    catch (Exception ex) { System.Windows.MessageBox.Show($"Error: {ex.Message}"); }
                    this.Hide();
                };
                System.Windows.Controls.Grid.SetColumn(btnLoad, 0);
                grid.Children.Add(btnLoad);

                System.Windows.Controls.Button btnEdit = new System.Windows.Controls.Button() { Content = "✏️", ToolTip = "Rename", Width = 35 };
                btnEdit.Click += (s, e) => RenameProfile(file, profileName);
                System.Windows.Controls.Grid.SetColumn(btnEdit, 1);
                grid.Children.Add(btnEdit);

                System.Windows.Controls.Button btnDelete = new System.Windows.Controls.Button() { Content = "🗑️", ToolTip = "Delete", Width = 35, Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.LightCoral) };
                btnDelete.Click += (s, e) => DeleteProfile(file);
                System.Windows.Controls.Grid.SetColumn(btnDelete, 2);
                grid.Children.Add(btnDelete);

                ProfilesPanel.Children.Add(grid);
            }

            if (ProfilesPanel.Children.Count == 0)
            {
                ProfilesPanel.Children.Add(new System.Windows.Controls.TextBlock() { Text = "No profiles yet.", Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Gray), FontStyle = FontStyles.Italic, Margin = new Thickness(5) });
            }
        }

        private void RenameProfile(string filePath, string oldName)
        {
            string newName = PromptInput("Rename Profile", oldName);
            if (!string.IsNullOrWhiteSpace(newName) && newName != oldName)
            {
                string newPath = Path.Combine(Path.GetDirectoryName(filePath)!, newName + ".config");
                if (!File.Exists(newPath))
                {
                    File.Move(filePath, newPath);
                    RefreshProfiles();
                }
            }
        }

        private void DeleteProfile(string filePath)
        {
            if (System.Windows.MessageBox.Show("Are you sure you want to delete this profile?", "Confirm", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            {
                File.Delete(filePath);
                RefreshProfiles();
            }
        }

        private void SaveProfile_Click(object sender, RoutedEventArgs e)
        {
            string newName = PromptInput("Save Profile", "NewProfile");
            if (!string.IsNullOrWhiteSpace(newName))
            {
                string newPath = Path.Combine(Directory.GetCurrentDirectory(), newName + ".config");
                try
                {
                    DisplayManager.SaveProfile(newPath);
                    RefreshProfiles();
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show($"Error: {ex.Message}");
                }
            }
        }

        private string PromptInput(string title, string defaultText)
        {
            Window prompt = new Window()
            {
                Width = 300, Height = 140, Title = title,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                WindowStyle = WindowStyle.ToolWindow,
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(30, 30, 30)),
                Topmost = true
            };
            System.Windows.Controls.StackPanel panel = new System.Windows.Controls.StackPanel() { Margin = new Thickness(15) };
            
            System.Windows.Controls.TextBox textBox = new System.Windows.Controls.TextBox() { Text = defaultText, Margin = new Thickness(0,0,0,15), Padding = new Thickness(5), FontSize = 14 };
            System.Windows.Controls.Button btnOk = new System.Windows.Controls.Button() { Content = "OK", Width = 80, HorizontalAlignment = System.Windows.HorizontalAlignment.Right, Padding = new Thickness(5) };
            btnOk.Click += (s,e) => prompt.DialogResult = true;
            
            panel.Children.Add(new System.Windows.Controls.TextBlock() { Text = "Enter profile name:", Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.White), Margin = new Thickness(0,0,0,5) });
            panel.Children.Add(textBox);
            panel.Children.Add(btnOk);
            prompt.Content = panel;

            if (prompt.ShowDialog() == true)
                return textBox.Text.Trim();
            return string.Empty;
        }

        private void Exit_Click(object sender, RoutedEventArgs e)
        {
            System.Windows.Forms.Application.Exit();
        }

        private void Window_Deactivated(object sender, EventArgs e)
        {
            this.Hide();
        }
    }
}
