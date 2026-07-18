using System;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace MonitorSwitcher;

static class Program
{
    private const string SingleInstanceMutexName = @"Local\MonitorSwitcher.TrayApp";

    [STAThread]
    static void Main(string[] args)
    {
        if (args.Length >= 2)
        {
            // CLI Mode (for backward compatibility / script usage)
            string command = args[0].ToLower();
            string profileArgument = args[1];

            try
            {
                if (command is "save" or "save-logical")
                {
                    string filename = EnsureExtension(profileArgument, LogicalProfileStore.FileExtension);
                    string profileName = ProfileCatalog.GetProfileName(filename);
                    LogicalProfileStore.Save(filename, LogicalProfileCapture.CaptureCurrent(profileName));
                }
                else if (command == "save-legacy")
                {
                    DisplayManager.SaveProfile(EnsureExtension(profileArgument, ".config"));
                }
                else if (command == "migrate-legacy")
                {
                    foreach (string migratedFile in LegacyProfileMigrator.MigrateDirectory(profileArgument))
                    {
                        Console.WriteLine($"Migrated: {migratedFile}");
                    }
                }
                else if (command == "load")
                {
                    DisplayManager.LoadProfile(ResolveProfileArgument(profileArgument));
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

        using Mutex singleInstanceMutex = new Mutex(true, SingleInstanceMutexName, out bool createdNew);
        if (!createdNew)
        {
            return;
        }

        // GUI Mode (System Tray)
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new TrayApplicationContext());
    }

    private static string ResolveProfileArgument(string profileArgument)
    {
        if (File.Exists(profileArgument))
        {
            return profileArgument;
        }

        string logicalPath = EnsureExtension(profileArgument, LogicalProfileStore.FileExtension);
        if (File.Exists(logicalPath))
        {
            return logicalPath;
        }

        string legacyPath = EnsureExtension(profileArgument, ".config");
        if (File.Exists(legacyPath))
        {
            return legacyPath;
        }

        return ProfileCatalog.ResolveProfileFile(profileArgument, Directory.GetCurrentDirectory());
    }

    private static string EnsureExtension(string profileArgument, string extension)
    {
        return profileArgument.EndsWith(extension, StringComparison.OrdinalIgnoreCase)
            ? profileArgument
            : profileArgument + extension;
    }
}
