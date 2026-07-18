using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MonitorSwitcher;

public static class LogicalProfileStore
{
    public const string FileExtension = ".profile.json";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static void Save(string filename, LogicalDisplayProfile profile)
    {
        Validate(profile);

        string? directory = Path.GetDirectoryName(filename);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string json = JsonSerializer.Serialize(profile, SerializerOptions);
        string temporaryFile = filename + ".tmp";
        try
        {
            File.WriteAllText(temporaryFile, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.Move(temporaryFile, filename, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryFile))
            {
                File.Delete(temporaryFile);
            }
        }
    }

    public static LogicalDisplayProfile Load(string filename)
    {
        if (!File.Exists(filename))
        {
            throw new FileNotFoundException("Logical profile file not found.", filename);
        }

        string json = File.ReadAllText(filename);
        LogicalDisplayProfile? profile = JsonSerializer.Deserialize<LogicalDisplayProfile>(json, SerializerOptions);
        if (profile is null)
        {
            throw new InvalidDataException($"Logical profile is empty or invalid: {filename}");
        }

        Validate(profile);
        return profile;
    }

    public static void Validate(LogicalDisplayProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        if (profile.SchemaVersion != LogicalDisplayProfile.CurrentSchemaVersion)
        {
            throw new InvalidDataException($"Unsupported logical profile schema version: {profile.SchemaVersion}");
        }

        if (string.IsNullOrWhiteSpace(profile.Name))
        {
            throw new InvalidDataException("Logical profile name is required.");
        }

        if (profile.Monitors is null || profile.Monitors.Count == 0)
        {
            throw new InvalidDataException("Logical profile must contain at least one monitor.");
        }

        List<LogicalMonitorConfiguration> enabledMonitors = profile.Monitors.Where(monitor => monitor.Enabled).ToList();
        if (enabledMonitors.Count == 0)
        {
            throw new InvalidDataException("Logical profile must enable at least one monitor.");
        }

        if (enabledMonitors.Count(monitor => monitor.Primary) != 1)
        {
            throw new InvalidDataException("Logical profile must have exactly one enabled primary monitor.");
        }

        HashSet<string> stableIds = new(StringComparer.OrdinalIgnoreCase);
        foreach (LogicalMonitorConfiguration monitor in profile.Monitors)
        {
            if (monitor.Identity is null || string.IsNullOrWhiteSpace(monitor.Identity.StableId))
            {
                throw new InvalidDataException("Every monitor must have a stable identity.");
            }

            if (!stableIds.Add(monitor.Identity.StableId))
            {
                throw new InvalidDataException($"Duplicate monitor identity: {monitor.Identity.StableId}");
            }

            if (!monitor.Enabled && monitor.Primary)
            {
                throw new InvalidDataException($"Disabled monitor cannot be primary: {monitor.Identity.StableId}");
            }

            if (monitor.Enabled && (monitor.Bounds.Width == 0 || monitor.Bounds.Height == 0))
            {
                throw new InvalidDataException($"Enabled monitor must have a valid resolution: {monitor.Identity.StableId}");
            }

            bool hasRefreshNumerator = monitor.RefreshRate.Numerator > 0;
            bool hasRefreshDenominator = monitor.RefreshRate.Denominator > 0;
            if (hasRefreshNumerator != hasRefreshDenominator)
            {
                throw new InvalidDataException($"Refresh rate must contain both numerator and denominator: {monitor.Identity.StableId}");
            }
        }
    }
}
