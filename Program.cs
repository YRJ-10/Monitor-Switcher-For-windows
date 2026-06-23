using System;

namespace MonitorSwitcher;

class Program
{
    static void Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.WriteLine("Monitor Switcher Core");
            Console.WriteLine("Usage:");
            Console.WriteLine("  MonitorSwitcher save <profile_name>");
            Console.WriteLine("  MonitorSwitcher load <profile_name>");
            return;
        }

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
    }
}
