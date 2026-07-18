using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;

namespace MonitorSwitcher;

public static class LogicalProfileApplier
{
    private const uint SDC_USE_SUPPLIED_DISPLAY_CONFIG = 0x00000020;
    private const uint SDC_VALIDATE = 0x00000040;
    private const uint SDC_APPLY = 0x00000080;
    private const uint SDC_SAVE_TO_DATABASE = 0x00000200;
    private const uint SDC_ALLOW_CHANGES = 0x00000400;

    private const uint DISPLAYCONFIG_PIXELFORMAT_32BPP = 4;
    private const int DISPLAYCONFIG_SCALING_PREFERRED = 128;
    private const int ApplyAttempts = 4;

    public static void Validate(LogicalDisplayProfile profile)
    {
        LogicalProfileStore.Validate(profile);
        DisplayTopologySnapshot topology = DisplayTopologyResolver.QueryCurrent();
        BuiltDisplayConfiguration configuration = BuildConfiguration(profile, topology);
        ValidateConfiguration(configuration, allowChanges: false);
    }

    public static void Apply(LogicalDisplayProfile profile)
    {
        LogicalProfileStore.Validate(profile);
        Exception? lastError = null;

        for (int attempt = 1; attempt <= ApplyAttempts; attempt++)
        {
            try
            {
                ApplyOnce(profile);
                return;
            }
            catch (Exception ex) when (ex is Win32Exception or DisplayResolutionException)
            {
                lastError = ex;
                if (attempt < ApplyAttempts)
                {
                    Thread.Sleep(GetRetryDelay(attempt));
                }
            }
        }

        throw new InvalidOperationException(
            $"Logical profile apply failed after {ApplyAttempts} attempts: {lastError?.Message}",
            lastError);
    }

    private static void ApplyOnce(LogicalDisplayProfile profile)
    {
        DisplayTopologySnapshot topology = DisplayTopologyResolver.QueryCurrent();
        BuiltDisplayConfiguration configuration = BuildConfiguration(profile, topology);

        bool allowChanges = false;
        try
        {
            ValidateConfiguration(configuration, allowChanges: false);
        }
        catch (Win32Exception)
        {
            configuration = BuildConfiguration(profile, topology);
            ValidateConfiguration(configuration, allowChanges: true);
            allowChanges = true;
        }

        uint flags = SDC_APPLY | SDC_USE_SUPPLIED_DISPLAY_CONFIG | SDC_SAVE_TO_DATABASE;
        if (allowChanges)
        {
            flags |= SDC_ALLOW_CHANGES;
        }

        int error = DisplayManager.SetDisplayConfig(
            (uint)configuration.Paths.Length,
            configuration.Paths,
            (uint)configuration.Modes.Length,
            configuration.Modes,
            flags);
        if (error != 0)
        {
            throw new Win32Exception(error, $"SetDisplayConfig apply failed with code {error}.");
        }

        VerifyAppliedProfile(profile);
    }

    private static BuiltDisplayConfiguration BuildConfiguration(
        LogicalDisplayProfile profile,
        DisplayTopologySnapshot topology)
    {
        LogicalProfileResolution resolution = DisplayTopologyResolver.Resolve(profile, topology);
        if (!resolution.Success)
        {
            throw new DisplayResolutionException(string.Join(" ", resolution.Errors));
        }

        List<LogicalMonitorMatch> enabledMatches = resolution.Matches
            .Where(match => match.Requested.Enabled)
            .OrderByDescending(match => match.Requested.Primary)
            .ToList();

        Dictionary<LogicalMonitorMatch, int> selectedPaths = SelectUniqueSourcePaths(enabledMatches, topology);
        LogicalMonitorConfiguration primary = enabledMatches.Single(match => match.Requested.Primary).Requested;
        int originX = primary.Bounds.X;
        int originY = primary.Bounds.Y;

        List<DisplayManager.DISPLAYCONFIG_PATH_INFO> paths = new(enabledMatches.Count);
        List<DisplayManager.DISPLAYCONFIG_MODE_INFO> modes = new(enabledMatches.Count);

        foreach (LogicalMonitorMatch match in enabledMatches)
        {
            DisplayManager.DISPLAYCONFIG_PATH_INFO path = topology.Paths[selectedPaths[match]];
            uint sourceModeIndex = (uint)modes.Count;

            DisplayManager.DISPLAYCONFIG_TARGET_MODE targetMode =
                DisplayTopologyResolver.TryGetTargetMode(topology, path, out DisplayManager.DISPLAYCONFIG_TARGET_MODE currentTargetMode)
                    ? currentTargetMode
                    : DisplayTopologyResolver.GetPreferredTargetMode(path);

            path.flags |= DisplayTopologyResolver.DISPLAYCONFIG_PATH_ACTIVE;
            path.sourceInfo.modeInfoIdx = EncodeSourceModeIndex(path.flags, sourceModeIndex);
            path.targetInfo.rotation = ToNativeOrientation(match.Requested.Orientation);
            path.targetInfo.scaling = DISPLAYCONFIG_SCALING_PREFERRED;
            path.targetInfo.scanLineOrdering = 0;
            path.targetInfo.targetAvailable = true;

            if (match.Requested.RefreshRate.Numerator > 0 && match.Requested.RefreshRate.Denominator > 0)
            {
                path.targetInfo.refreshRate = new DisplayManager.DISPLAYCONFIG_RATIONAL
                {
                    Numerator = match.Requested.RefreshRate.Numerator,
                    Denominator = match.Requested.RefreshRate.Denominator
                };
            }

            modes.Add(new DisplayManager.DISPLAYCONFIG_MODE_INFO
            {
                infoType = DisplayManager.DISPLAYCONFIG_MODE_INFO_TYPE.DISPLAYCONFIG_MODE_INFO_TYPE_SOURCE,
                id = path.sourceInfo.id,
                adapterId = path.sourceInfo.adapterId,
                modeInfo = new DisplayManager.DISPLAYCONFIG_MODE_INFO_UNION
                {
                    sourceMode = new DisplayManager.DISPLAYCONFIG_SOURCE_MODE
                    {
                        width = match.Requested.Bounds.Width,
                        height = match.Requested.Bounds.Height,
                        pixelFormat = DISPLAYCONFIG_PIXELFORMAT_32BPP,
                        position = new DisplayManager.POINTL
                        {
                            x = match.Requested.Bounds.X - originX,
                            y = match.Requested.Bounds.Y - originY
                        }
                    }
                }
            });

            uint targetModeIndex = (uint)modes.Count;
            path.targetInfo.modeInfoIdx = EncodeTargetModeIndex(path.flags, targetModeIndex);
            modes.Add(new DisplayManager.DISPLAYCONFIG_MODE_INFO
            {
                infoType = DisplayManager.DISPLAYCONFIG_MODE_INFO_TYPE.DISPLAYCONFIG_MODE_INFO_TYPE_TARGET,
                id = path.targetInfo.id,
                adapterId = path.targetInfo.adapterId,
                modeInfo = new DisplayManager.DISPLAYCONFIG_MODE_INFO_UNION
                {
                    targetMode = targetMode
                }
            });

            paths.Add(path);
        }

        return new BuiltDisplayConfiguration(paths.ToArray(), modes.ToArray());
    }

    private static Dictionary<LogicalMonitorMatch, int> SelectUniqueSourcePaths(
        IReadOnlyList<LogicalMonitorMatch> matches,
        DisplayTopologySnapshot topology)
    {
        Dictionary<LogicalMonitorMatch, List<int>> candidates = matches.ToDictionary(
            match => match,
            match => FindCandidatePaths(match.Current, topology)
                .OrderByDescending(pathIndex => pathIndex == match.Current.PathIndex)
                .ThenByDescending(pathIndex =>
                    (topology.Paths[pathIndex].flags & DisplayTopologyResolver.DISPLAYCONFIG_PATH_ACTIVE) != 0)
                .ThenBy(pathIndex => topology.Paths[pathIndex].sourceInfo.id)
                .ToList());

        LogicalMonitorMatch? noPath = matches.FirstOrDefault(match => candidates[match].Count == 0);
        if (noPath is not null)
        {
            throw new DisplayResolutionException($"No live display path is available for {GetLabel(noPath.Requested.Identity)}.");
        }

        List<LogicalMonitorMatch> solveOrder = matches
            .OrderBy(match => candidates[match].Count)
            .ThenByDescending(match => match.Requested.Primary)
            .ToList();
        Dictionary<LogicalMonitorMatch, int> selected = new();
        HashSet<string> usedSources = new(StringComparer.OrdinalIgnoreCase);

        if (!TryAssignPath(0, solveOrder, candidates, topology, selected, usedSources))
        {
            throw new DisplayResolutionException("Windows did not report enough independent display sources for this profile.");
        }

        return selected;
    }

    private static bool TryAssignPath(
        int index,
        IReadOnlyList<LogicalMonitorMatch> order,
        IReadOnlyDictionary<LogicalMonitorMatch, List<int>> candidates,
        DisplayTopologySnapshot topology,
        IDictionary<LogicalMonitorMatch, int> selected,
        ISet<string> usedSources)
    {
        if (index == order.Count)
        {
            return true;
        }

        LogicalMonitorMatch match = order[index];
        foreach (int pathIndex in candidates[match])
        {
            string sourceKey = GetSourceKey(topology.Paths[pathIndex].sourceInfo);
            if (!usedSources.Add(sourceKey))
            {
                continue;
            }

            selected[match] = pathIndex;
            if (TryAssignPath(index + 1, order, candidates, topology, selected, usedSources))
            {
                return true;
            }

            selected.Remove(match);
            usedSources.Remove(sourceKey);
        }

        return false;
    }

    private static IEnumerable<int> FindCandidatePaths(
        CurrentDisplayTarget monitor,
        DisplayTopologySnapshot topology)
    {
        DisplayManager.DISPLAYCONFIG_PATH_TARGET_INFO expected = monitor.Path.targetInfo;
        for (int index = 0; index < topology.Paths.Length; index++)
        {
            DisplayManager.DISPLAYCONFIG_PATH_TARGET_INFO current = topology.Paths[index].targetInfo;
            if (current.targetAvailable &&
                current.id == expected.id &&
                SameAdapter(current.adapterId, expected.adapterId))
            {
                yield return index;
            }
        }
    }

    private static void ValidateConfiguration(BuiltDisplayConfiguration configuration, bool allowChanges)
    {
        uint flags = SDC_VALIDATE | SDC_USE_SUPPLIED_DISPLAY_CONFIG;
        if (allowChanges)
        {
            flags |= SDC_ALLOW_CHANGES;
        }

        int error = DisplayManager.SetDisplayConfig(
            (uint)configuration.Paths.Length,
            configuration.Paths,
            (uint)configuration.Modes.Length,
            configuration.Modes,
            flags);
        if (error != 0)
        {
            throw new Win32Exception(error, $"SetDisplayConfig validation failed with code {error}.");
        }
    }

    private static void VerifyAppliedProfile(LogicalDisplayProfile profile)
    {
        DisplayTopologySnapshot topology = DisplayTopologyResolver.QueryCurrent();
        LogicalProfileResolution resolution = DisplayTopologyResolver.Resolve(profile, topology);
        if (!resolution.Success)
        {
            throw new DisplayResolutionException(string.Join(" ", resolution.Errors));
        }

        LogicalMonitorConfiguration primary = profile.Monitors.Single(monitor => monitor.Enabled && monitor.Primary);
        foreach (LogicalMonitorMatch match in resolution.Matches)
        {
            if (match.Current.IsActive != match.Requested.Enabled)
            {
                throw new DisplayResolutionException($"Windows returned an unexpected active state for {GetLabel(match.Requested.Identity)}.");
            }

            if (!match.Requested.Enabled)
            {
                continue;
            }

            if (!DisplayTopologyResolver.TryGetSourceMode(topology, match.Current, out DisplayManager.DISPLAYCONFIG_SOURCE_MODE sourceMode))
            {
                throw new DisplayResolutionException($"Windows did not return the applied mode for {GetLabel(match.Requested.Identity)}.");
            }

            int expectedX = match.Requested.Bounds.X - primary.Bounds.X;
            int expectedY = match.Requested.Bounds.Y - primary.Bounds.Y;
            if (sourceMode.position.x != expectedX ||
                sourceMode.position.y != expectedY ||
                sourceMode.width != match.Requested.Bounds.Width ||
                sourceMode.height != match.Requested.Bounds.Height)
            {
                throw new DisplayResolutionException($"Windows applied a different layout for {GetLabel(match.Requested.Identity)}.");
            }
        }
    }

    private static uint EncodeSourceModeIndex(uint pathFlags, uint modeIndex)
    {
        if ((pathFlags & DisplayTopologyResolver.DISPLAYCONFIG_PATH_SUPPORT_VIRTUAL_MODE) == 0)
        {
            return modeIndex;
        }

        if (modeIndex > ushort.MaxValue - 1)
        {
            throw new InvalidDataException("Display source mode index exceeds the Windows limit.");
        }

        const uint invalidCloneGroup = 0xFFFF;
        return (modeIndex << 16) | invalidCloneGroup;
    }

    private static uint EncodeTargetModeIndex(uint pathFlags, uint modeIndex)
    {
        if ((pathFlags & DisplayTopologyResolver.DISPLAYCONFIG_PATH_SUPPORT_VIRTUAL_MODE) == 0)
        {
            return modeIndex;
        }

        if (modeIndex > ushort.MaxValue - 1)
        {
            throw new InvalidDataException("Display target mode index exceeds the Windows limit.");
        }

        const uint invalidDesktopModeIndex = 0xFFFF;
        return (modeIndex << 16) | invalidDesktopModeIndex;
    }

    private static int ToNativeOrientation(DisplayOrientation orientation)
    {
        return orientation switch
        {
            DisplayOrientation.Landscape => 1,
            DisplayOrientation.Portrait => 2,
            DisplayOrientation.LandscapeFlipped => 3,
            DisplayOrientation.PortraitFlipped => 4,
            _ => throw new ArgumentOutOfRangeException(nameof(orientation))
        };
    }

    private static int GetRetryDelay(int completedAttempt)
    {
        return completedAttempt switch
        {
            1 => 250,
            2 => 750,
            _ => 1_500
        };
    }

    private static string GetSourceKey(DisplayManager.DISPLAYCONFIG_PATH_SOURCE_INFO source)
    {
        return $"{source.adapterId.HighPart:X8}:{source.adapterId.LowPart:X8}:{source.id:X8}";
    }

    private static bool SameAdapter(DisplayManager.LUID left, DisplayManager.LUID right)
    {
        return left.HighPart == right.HighPart && left.LowPart == right.LowPart;
    }

    private static string GetLabel(LogicalMonitorIdentity identity)
    {
        return identity.FriendlyName ?? identity.StableId;
    }

    private sealed record BuiltDisplayConfiguration(
        DisplayManager.DISPLAYCONFIG_PATH_INFO[] Paths,
        DisplayManager.DISPLAYCONFIG_MODE_INFO[] Modes);

    private sealed class DisplayResolutionException : Exception
    {
        public DisplayResolutionException(string message)
            : base(message)
        {
        }
    }
}
