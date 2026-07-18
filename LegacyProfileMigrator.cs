using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace MonitorSwitcher;

public static class LegacyProfileMigrator
{
    private const int MaximumPathCount = 64;
    private const int MaximumModeCount = 256;

    public static IReadOnlyList<string> MigrateDirectory(string directory)
    {
        string[] legacyFiles = Directory.GetFiles(directory, "*.config");
        if (legacyFiles.Length == 0)
        {
            throw new FileNotFoundException("No legacy .config profiles were found.", directory);
        }

        List<LegacyProfileData> legacyProfiles = legacyFiles
            .Select(ReadLegacyProfile)
            .ToList();
        DisplayTopologySnapshot topology = DisplayTopologyResolver.QueryCurrent();
        List<CurrentDisplayTarget> currentMonitors = topology.Monitors
            .Where(monitor => monitor.IsAvailable)
            .ToList();

        Dictionary<LegacyTargetKey, CurrentDisplayTarget> targetMap = BuildTargetMap(
            legacyProfiles,
            currentMonitors,
            topology);

        List<(string Path, LogicalDisplayProfile Profile)> migrations = new();
        foreach (LegacyProfileData legacyProfile in legacyProfiles)
        {
            string profileName = ProfileCatalog.GetProfileName(legacyProfile.Filename);
            string logicalPath = ProfileCatalog.GetProfilePath(directory, profileName, logical: true);
            if (File.Exists(logicalPath))
            {
                continue;
            }

            LogicalDisplayProfile logicalProfile = ConvertProfile(
                profileName,
                legacyProfile,
                currentMonitors,
                targetMap);
            LogicalProfileStore.Validate(logicalProfile);
            migrations.Add((logicalPath, logicalProfile));
        }

        foreach ((string path, LogicalDisplayProfile profile) in migrations)
        {
            LogicalProfileStore.Save(path, profile);
        }

        return migrations.Select(migration => migration.Path).ToList();
    }

    private static Dictionary<LegacyTargetKey, CurrentDisplayTarget> BuildTargetMap(
        IReadOnlyList<LegacyProfileData> profiles,
        IReadOnlyList<CurrentDisplayTarget> currentMonitors,
        DisplayTopologySnapshot topology)
    {
        Dictionary<LegacyTargetKey, List<ModeSignature>> legacyModes = new();
        foreach (LegacyProfileData profile in profiles)
        {
            foreach (DisplayManager.DISPLAYCONFIG_PATH_INFO path in profile.Paths)
            {
                LegacyTargetKey key = LegacyTargetKey.From(path.targetInfo);
                if (!legacyModes.TryGetValue(key, out List<ModeSignature>? signatures))
                {
                    signatures = new List<ModeSignature>();
                    legacyModes[key] = signatures;
                }

                if (TryGetLegacyTargetMode(profile, path, out DisplayManager.DISPLAYCONFIG_TARGET_MODE targetMode))
                {
                    ModeSignature signature = ModeSignature.From(targetMode);
                    if (!signatures.Contains(signature))
                    {
                        signatures.Add(signature);
                    }
                }
            }
        }

        if (legacyModes.Count != currentMonitors.Count)
        {
            throw new InvalidDataException(
                $"Legacy profiles describe {legacyModes.Count} monitors, but Windows currently reports {currentMonitors.Count} available monitors.");
        }

        Dictionary<CurrentDisplayTarget, List<ModeSignature>> currentModes = currentMonitors.ToDictionary(
            monitor => monitor,
            monitor => GetCurrentModeSignatures(topology, monitor));

        LegacyTargetKey[] legacyTargets = legacyModes.Keys.ToArray();
        Dictionary<LegacyTargetKey, CurrentDisplayTarget> candidate = new();
        Dictionary<LegacyTargetKey, CurrentDisplayTarget>? best = null;
        int bestScore = int.MinValue;
        int bestCount = 0;

        AssignTarget(0, 0, new HashSet<CurrentDisplayTarget>());

        if (best is null || bestCount != 1)
        {
            throw new InvalidDataException("Legacy monitor identities could not be mapped uniquely to the monitors currently reported by Windows.");
        }

        return best;

        void AssignTarget(int index, int totalScore, HashSet<CurrentDisplayTarget> used)
        {
            if (index == legacyTargets.Length)
            {
                if (totalScore > bestScore)
                {
                    bestScore = totalScore;
                    best = new Dictionary<LegacyTargetKey, CurrentDisplayTarget>(candidate);
                    bestCount = 1;
                }
                else if (totalScore == bestScore)
                {
                    bestCount++;
                }

                return;
            }

            LegacyTargetKey legacyTarget = legacyTargets[index];
            foreach (CurrentDisplayTarget currentMonitor in currentMonitors)
            {
                if (used.Contains(currentMonitor))
                {
                    continue;
                }

                int score = ScoreModes(legacyModes[legacyTarget], currentModes[currentMonitor]);
                if (score <= 0)
                {
                    continue;
                }

                used.Add(currentMonitor);
                candidate[legacyTarget] = currentMonitor;
                AssignTarget(index + 1, totalScore + score, used);
                candidate.Remove(legacyTarget);
                used.Remove(currentMonitor);
            }
        }
    }

    private static LogicalDisplayProfile ConvertProfile(
        string name,
        LegacyProfileData legacyProfile,
        IReadOnlyList<CurrentDisplayTarget> currentMonitors,
        IReadOnlyDictionary<LegacyTargetKey, CurrentDisplayTarget> targetMap)
    {
        Dictionary<CurrentDisplayTarget, LegacyMonitorState> enabledStates = new();
        foreach (DisplayManager.DISPLAYCONFIG_PATH_INFO path in legacyProfile.Paths)
        {
            LegacyTargetKey targetKey = LegacyTargetKey.From(path.targetInfo);
            if (!targetMap.TryGetValue(targetKey, out CurrentDisplayTarget? currentMonitor))
            {
                throw new InvalidDataException($"Legacy target could not be mapped while converting {name}.");
            }

            if (!TryGetLegacySourceMode(legacyProfile, path, out DisplayManager.DISPLAYCONFIG_SOURCE_MODE sourceMode))
            {
                throw new InvalidDataException($"Legacy source mode is missing while converting {name}.");
            }

            enabledStates[currentMonitor] = new LegacyMonitorState(path, sourceMode);
        }

        CurrentDisplayTarget? primary = enabledStates
            .Where(entry => entry.Value.SourceMode.position.x == 0 && entry.Value.SourceMode.position.y == 0)
            .Select(entry => entry.Key)
            .FirstOrDefault();
        primary ??= enabledStates.Keys.FirstOrDefault();

        List<LogicalMonitorConfiguration> monitors = currentMonitors.Select(currentMonitor =>
        {
            if (!enabledStates.TryGetValue(currentMonitor, out LegacyMonitorState? state))
            {
                return new LogicalMonitorConfiguration
                {
                    Identity = currentMonitor.Identity,
                    Enabled = false,
                    Primary = false
                };
            }

            DisplayManager.DISPLAYCONFIG_SOURCE_MODE sourceMode = state.SourceMode;
            return new LogicalMonitorConfiguration
            {
                Identity = currentMonitor.Identity,
                Enabled = true,
                Primary = ReferenceEquals(currentMonitor, primary),
                Bounds = new DisplayRectangle(
                    sourceMode.position.x,
                    sourceMode.position.y,
                    sourceMode.width,
                    sourceMode.height),
                Orientation = FromNativeOrientation(state.Path.targetInfo.rotation),
                RefreshRate = new DisplayRefreshRate(
                    state.Path.targetInfo.refreshRate.Numerator,
                    state.Path.targetInfo.refreshRate.Denominator)
            };
        }).ToList();

        return new LogicalDisplayProfile
        {
            Name = name,
            Monitors = monitors
        };
    }

    private static List<ModeSignature> GetCurrentModeSignatures(
        DisplayTopologySnapshot topology,
        CurrentDisplayTarget monitor)
    {
        List<ModeSignature> signatures = new();
        if (DisplayTopologyResolver.TryGetTargetMode(topology, monitor.Path, out DisplayManager.DISPLAYCONFIG_TARGET_MODE currentMode))
        {
            signatures.Add(ModeSignature.From(currentMode));
        }

        DisplayManager.DISPLAYCONFIG_TARGET_MODE preferredMode =
            DisplayTopologyResolver.GetPreferredTargetMode(monitor.Path);
        ModeSignature preferredSignature = ModeSignature.From(preferredMode);
        if (!signatures.Contains(preferredSignature))
        {
            signatures.Add(preferredSignature);
        }

        return signatures;
    }

    private static int ScoreModes(
        IReadOnlyList<ModeSignature> legacyModes,
        IReadOnlyList<ModeSignature> currentModes)
    {
        int best = 0;
        foreach (ModeSignature legacy in legacyModes)
        {
            foreach (ModeSignature current in currentModes)
            {
                int score = 0;
                if (legacy.Width == current.Width && legacy.Height == current.Height)
                {
                    score = 10_000;
                }
                else if (legacy.Width == current.Height && legacy.Height == current.Width)
                {
                    score = 7_000;
                }

                if (score > 0 && Math.Abs(legacy.RefreshHertz - current.RefreshHertz) < 0.6)
                {
                    score += 500;
                }

                best = Math.Max(best, score);
            }
        }

        return best;
    }

    private static LegacyProfileData ReadLegacyProfile(string filename)
    {
        using FileStream stream = new(filename, FileMode.Open, FileAccess.Read, FileShare.Read);
        using BinaryReader reader = new(stream);
        if (stream.Length < sizeof(uint) * 2)
        {
            throw new InvalidDataException($"Legacy profile is empty or corrupt: {filename}");
        }

        uint pathCount = reader.ReadUInt32();
        uint modeCount = reader.ReadUInt32();
        if (pathCount is 0 or > MaximumPathCount || modeCount is 0 or > MaximumModeCount)
        {
            throw new InvalidDataException($"Legacy profile has invalid array sizes: {filename}");
        }

        return new LegacyProfileData(
            filename,
            ReadStructArray<DisplayManager.DISPLAYCONFIG_PATH_INFO>(reader, pathCount),
            ReadStructArray<DisplayManager.DISPLAYCONFIG_MODE_INFO>(reader, modeCount));
    }

    private static bool TryGetLegacySourceMode(
        LegacyProfileData profile,
        DisplayManager.DISPLAYCONFIG_PATH_INFO path,
        out DisplayManager.DISPLAYCONFIG_SOURCE_MODE sourceMode)
    {
        uint modeIndex = GetSourceModeIndex(path);
        if (modeIndex >= profile.Modes.Length ||
            profile.Modes[modeIndex].infoType != DisplayManager.DISPLAYCONFIG_MODE_INFO_TYPE.DISPLAYCONFIG_MODE_INFO_TYPE_SOURCE)
        {
            sourceMode = default;
            return false;
        }

        sourceMode = profile.Modes[modeIndex].modeInfo.sourceMode;
        return true;
    }

    private static bool TryGetLegacyTargetMode(
        LegacyProfileData profile,
        DisplayManager.DISPLAYCONFIG_PATH_INFO path,
        out DisplayManager.DISPLAYCONFIG_TARGET_MODE targetMode)
    {
        uint modeIndex = GetTargetModeIndex(path);
        if (modeIndex >= profile.Modes.Length ||
            profile.Modes[modeIndex].infoType != DisplayManager.DISPLAYCONFIG_MODE_INFO_TYPE.DISPLAYCONFIG_MODE_INFO_TYPE_TARGET)
        {
            targetMode = default;
            return false;
        }

        targetMode = profile.Modes[modeIndex].modeInfo.targetMode;
        return true;
    }

    private static uint GetSourceModeIndex(DisplayManager.DISPLAYCONFIG_PATH_INFO path)
    {
        return (path.flags & DisplayTopologyResolver.DISPLAYCONFIG_PATH_SUPPORT_VIRTUAL_MODE) != 0
            ? (path.sourceInfo.modeInfoIdx >> 16) & 0xFFFF
            : path.sourceInfo.modeInfoIdx;
    }

    private static uint GetTargetModeIndex(DisplayManager.DISPLAYCONFIG_PATH_INFO path)
    {
        return (path.flags & DisplayTopologyResolver.DISPLAYCONFIG_PATH_SUPPORT_VIRTUAL_MODE) != 0
            ? (path.targetInfo.modeInfoIdx >> 16) & 0xFFFF
            : path.targetInfo.modeInfoIdx;
    }

    private static T[] ReadStructArray<T>(BinaryReader reader, uint count) where T : struct
    {
        int structSize = Marshal.SizeOf<T>();
        int byteCount = checked(structSize * (int)count);
        byte[] bytes = reader.ReadBytes(byteCount);
        if (bytes.Length != byteCount)
        {
            throw new EndOfStreamException("Legacy profile ended before all display data could be read.");
        }

        T[] result = new T[count];
        IntPtr buffer = Marshal.AllocHGlobal(byteCount);
        try
        {
            Marshal.Copy(bytes, 0, buffer, byteCount);
            for (int index = 0; index < count; index++)
            {
                result[index] = Marshal.PtrToStructure<T>(buffer + index * structSize);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }

        return result;
    }

    private static DisplayOrientation FromNativeOrientation(int rotation)
    {
        return rotation switch
        {
            1 => DisplayOrientation.Landscape,
            2 => DisplayOrientation.Portrait,
            3 => DisplayOrientation.LandscapeFlipped,
            4 => DisplayOrientation.PortraitFlipped,
            _ => DisplayOrientation.Landscape
        };
    }

    private sealed record LegacyProfileData(
        string Filename,
        DisplayManager.DISPLAYCONFIG_PATH_INFO[] Paths,
        DisplayManager.DISPLAYCONFIG_MODE_INFO[] Modes);

    private sealed record LegacyMonitorState(
        DisplayManager.DISPLAYCONFIG_PATH_INFO Path,
        DisplayManager.DISPLAYCONFIG_SOURCE_MODE SourceMode);

    private readonly record struct LegacyTargetKey(int AdapterHigh, uint AdapterLow, uint TargetId)
    {
        public static LegacyTargetKey From(DisplayManager.DISPLAYCONFIG_PATH_TARGET_INFO target)
        {
            return new LegacyTargetKey(target.adapterId.HighPart, target.adapterId.LowPart, target.id);
        }
    }

    private readonly record struct ModeSignature(uint Width, uint Height, double RefreshHertz)
    {
        public static ModeSignature From(DisplayManager.DISPLAYCONFIG_TARGET_MODE targetMode)
        {
            DisplayManager.DISPLAYCONFIG_VIDEO_SIGNAL_INFO signal = targetMode.targetVideoSignalInfo;
            double refresh = signal.vSyncFreq.Denominator == 0
                ? 0
                : (double)signal.vSyncFreq.Numerator / signal.vSyncFreq.Denominator;
            return new ModeSignature(signal.activeSize.cx, signal.activeSize.cy, refresh);
        }
    }
}
