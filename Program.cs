using System;
using System.Windows.Forms;

namespace MonitorSwitcher;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        if (args.Length >= 2)
        {
            // CLI Mode (for backward compatibility / script usage)
            string command = args[0].ToLower();
            string profileName = args[1] + ".config";

            try
            {
                if (command == "save")
                {
                    DisplayManager.SaveProfile(profileName);
                }
                else if (command == "load")
                {
                    DisplayManager.LoadProfile(profileName);
                }
                else
                {
                    Console.WriteLine($"Unknown command: {command}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }
            return;
        }

        // GUI Mode (System Tray)
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new TrayApplicationContext());
    }
}
