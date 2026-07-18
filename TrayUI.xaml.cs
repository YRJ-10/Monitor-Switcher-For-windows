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
        private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        private const int DWMWCP_ROUND = 2;

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
            
            // Enable native rounded corners
            int cornerPref = DWMWCP_ROUND;
            DwmSetWindowAttribute(interopHelper.Handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref cornerPref, sizeof(int));
        }

        public void RefreshProfiles()
        {
            ProfilesPanel.Children.Clear();
            IReadOnlyList<ProfileEntry> profiles = ProfileCatalog.GetProfiles(AppContext.BaseDirectory);
            foreach (ProfileEntry profile in profiles)
            {
                string file = profile.FilePath;
                string profileName = profile.Name;
                
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
                btnEdit.Click += (s, e) => RenameProfile(profile);
                System.Windows.Controls.Grid.SetColumn(btnEdit, 1);
                grid.Children.Add(btnEdit);

                System.Windows.Controls.Button btnDelete = new System.Windows.Controls.Button() { Content = "🗑️", ToolTip = "Delete", Width = 35, Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.LightCoral) };
                btnDelete.Click += (s, e) => DeleteProfile(profile);
                System.Windows.Controls.Grid.SetColumn(btnDelete, 2);
                grid.Children.Add(btnDelete);

                ProfilesPanel.Children.Add(grid);
            }

            if (ProfilesPanel.Children.Count == 0)
            {
                ProfilesPanel.Children.Add(new System.Windows.Controls.TextBlock() { Text = "No profiles yet.", Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Gray), FontStyle = FontStyles.Italic, Margin = new Thickness(5) });
            }
        }

        private void RenameProfile(ProfileEntry profile)
        {
            string newName = PromptInput("Rename Profile", profile.Name);
            if (!string.IsNullOrWhiteSpace(newName) && newName != profile.Name)
            {
                string directory = Path.GetDirectoryName(profile.FilePath)!;
                string newPath = ProfileCatalog.GetProfilePath(directory, newName, profile.IsLogical);
                if (!File.Exists(newPath))
                {
                    File.Move(profile.FilePath, newPath);

                    if (profile.IsLogical)
                    {
                        string oldLegacyPath = ProfileCatalog.GetLegacyCompanionPath(profile.FilePath);
                        string newLegacyPath = ProfileCatalog.GetProfilePath(directory, newName, logical: false);
                        if (File.Exists(oldLegacyPath) && !File.Exists(newLegacyPath))
                        {
                            File.Move(oldLegacyPath, newLegacyPath);
                        }
                    }

                    RefreshProfiles();
                }
            }
        }

        private void DeleteProfile(ProfileEntry profile)
        {
            if (System.Windows.MessageBox.Show("Are you sure you want to delete this profile?", "Confirm", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            {
                File.Delete(profile.FilePath);
                if (profile.IsLogical)
                {
                    string legacyPath = ProfileCatalog.GetLegacyCompanionPath(profile.FilePath);
                    if (File.Exists(legacyPath))
                    {
                        File.Delete(legacyPath);
                    }
                }
                RefreshProfiles();
            }
        }

        private void SaveProfile_Click(object sender, RoutedEventArgs e)
        {
            string newName = PromptInput("Save Profile", "NewProfile");
            if (!string.IsNullOrWhiteSpace(newName))
            {
                string newPath = ProfileCatalog.GetProfilePath(AppContext.BaseDirectory, newName, logical: true);
                try
                {
                    LogicalDisplayProfile profile = LogicalProfileCapture.CaptureCurrent(newName);
                    LogicalProfileStore.Save(newPath, profile);
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
                Width = 340,
                Height = 190,
                MinWidth = 340,
                MinHeight = 190,
                Title = title,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                WindowStyle = WindowStyle.ToolWindow,
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(30, 30, 30)),
                Topmost = true,
                ResizeMode = ResizeMode.NoResize
            };
            System.Windows.Controls.Grid panel = new System.Windows.Controls.Grid() { Margin = new Thickness(16) };
            panel.RowDefinitions.Add(new System.Windows.Controls.RowDefinition() { Height = GridLength.Auto });
            panel.RowDefinitions.Add(new System.Windows.Controls.RowDefinition() { Height = GridLength.Auto });
            panel.RowDefinitions.Add(new System.Windows.Controls.RowDefinition() { Height = new GridLength(1, GridUnitType.Star) });
            panel.RowDefinitions.Add(new System.Windows.Controls.RowDefinition() { Height = GridLength.Auto });

            System.Windows.Controls.TextBlock label = new System.Windows.Controls.TextBlock()
            {
                Text = "Enter profile name:",
                Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.White),
                Margin = new Thickness(0, 0, 0, 8)
            };
            System.Windows.Controls.Grid.SetRow(label, 0);
            panel.Children.Add(label);

            System.Windows.Controls.TextBox textBox = new System.Windows.Controls.TextBox()
            {
                Text = defaultText,
                Margin = new Thickness(0, 0, 0, 12),
                Padding = new Thickness(8, 6, 8, 6),
                FontSize = 14,
                MinHeight = 34,
                VerticalContentAlignment = VerticalAlignment.Center
            };
            System.Windows.Controls.Grid.SetRow(textBox, 1);
            panel.Children.Add(textBox);

            System.Windows.Controls.StackPanel actions = new System.Windows.Controls.StackPanel()
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Right
            };

            System.Windows.Controls.Button btnOk = new System.Windows.Controls.Button() 
            { 
                Content = title.StartsWith("Save", StringComparison.OrdinalIgnoreCase) ? "Save" : "OK",
                MinWidth = 86,
                MinHeight = 34,
                Padding = new Thickness(14, 6, 14, 6),
                Margin = new Thickness(8, 0, 0, 0),
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(60, 60, 60)), 
                Foreground = System.Windows.Media.Brushes.White,
                BorderThickness = new Thickness(0)
            };
            btnOk.Click += (s,e) => prompt.DialogResult = true;

            System.Windows.Controls.Button btnCancel = new System.Windows.Controls.Button()
            {
                Content = "Cancel",
                MinWidth = 86,
                MinHeight = 34,
                Padding = new Thickness(14, 6, 14, 6),
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(45, 45, 45)),
                Foreground = System.Windows.Media.Brushes.White,
                BorderThickness = new Thickness(0)
            };
            btnCancel.Click += (s, e) => prompt.DialogResult = false;

            actions.Children.Add(btnCancel);
            actions.Children.Add(btnOk);
            System.Windows.Controls.Grid.SetRow(actions, 3);
            panel.Children.Add(actions);
            prompt.Content = panel;
            prompt.Loaded += (s, e) =>
            {
                textBox.Focus();
                textBox.SelectAll();
            };

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
