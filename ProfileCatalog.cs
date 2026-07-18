using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MonitorSwitcher;

public sealed record ProfileEntry(string Name, string FilePath, bool IsLogical);

public static class ProfileCatalog
{
    public static IReadOnlyList<ProfileEntry> GetProfiles(string directory)
    {
        Dictionary<string, ProfileEntry> profiles = new(StringComparer.OrdinalIgnoreCase);

        foreach (string file in Directory.GetFiles(directory, $"*{LogicalProfileStore.FileExtension}"))
        {
            string name = GetProfileName(file);
            profiles[name] = new ProfileEntry(name, file, IsLogical: true);
        }

        foreach (string file in Directory.GetFiles(directory, "*.config"))
        {
            string name = GetProfileName(file);
            profiles.TryAdd(name, new ProfileEntry(name, file, IsLogical: false));
        }

        return profiles.Values
            .OrderBy(profile => profile.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static string ResolveProfileFile(string profileId, string directory)
    {
        IReadOnlyList<ProfileEntry> profiles = GetProfiles(directory);
        ProfileEntry? exact = profiles.FirstOrDefault(profile =>
            profile.Name.Equals(profileId, StringComparison.OrdinalIgnoreCase));
        if (exact is not null)
        {
            return exact.FilePath;
        }

        string prefix = profileId + ".";
        ProfileEntry? prefixed = profiles.FirstOrDefault(profile =>
            profile.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        if (prefixed is not null)
        {
            return prefixed.FilePath;
        }

        throw new FileNotFoundException($"Profile '{profileId}' was not found.", Path.Combine(directory, profileId));
    }

    public static string GetProfileName(string filePath)
    {
        string filename = Path.GetFileName(filePath);
        if (filename.EndsWith(LogicalProfileStore.FileExtension, StringComparison.OrdinalIgnoreCase))
        {
            return filename[..^LogicalProfileStore.FileExtension.Length];
        }

        return Path.GetFileNameWithoutExtension(filename);
    }

    public static string GetProfilePath(string directory, string profileName, bool logical)
    {
        string extension = logical ? LogicalProfileStore.FileExtension : ".config";
        return Path.Combine(directory, profileName + extension);
    }

    public static string GetLegacyCompanionPath(string profileFile)
    {
        string directory = Path.GetDirectoryName(profileFile) ?? AppContext.BaseDirectory;
        return GetProfilePath(directory, GetProfileName(profileFile), logical: false);
    }
}
