using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace MonitorSwitcher;

public sealed class CurrentDisplayTarget
{
    internal int PathIndex { get; init; }
    internal DisplayManager.DISPLAYCONFIG_PATH_INFO Path { get; init; }

    public required LogicalMonitorIdentity Identity { get; init; }
    public required bool IsActive { get; init; }
    public required bool IsAvailable { get; init; }
}

public sealed class DisplayTopologySnapshot
{
    internal DisplayManager.DISPLAYCONFIG_PATH_INFO[] Paths { get; init; } = [];
    internal DisplayManager.DISPLAYCONFIG_MODE_INFO[] Modes { get; init; } = [];

    public required IReadOnlyList<CurrentDisplayTarget> Monitors { get; init; }
}

public sealed record LogicalMonitorMatch(
    LogicalMonitorConfiguration Requested,
    CurrentDisplayTarget Current,
    int ConfidenceScore);

public sealed class LogicalProfileResolution
{
    public required IReadOnlyList<LogicalMonitorMatch> Matches { get; init; }
    public required IReadOnlyList<string> Errors { get; init; }
    public bool Success => Errors.Count == 0;
}

public static class DisplayTopologyResolver
{
    private const uint QDC_ALL_PATHS = 0x00000001;
    internal const uint DISPLAYCONFIG_PATH_ACTIVE = 0x00000001;
    internal const uint DISPLAYCONFIG_PATH_SUPPORT_VIRTUAL_MODE = 0x00000008;
    internal const uint DISPLAYCONFIG_PATH_MODE_IDX_INVALID = 0xFFFFFFFF;
    private const int ERROR_INSUFFICIENT_BUFFER = 122;
    private const int DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME = 2;
    private const int QueryAttempts = 3;

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_DEVICE_INFO_HEADER
    {
        public uint type;
        public uint size;
        public DisplayManager.LUID adapterId;
        public uint id;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DISPLAYCONFIG_TARGET_DEVICE_NAME
    {
        public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
        public uint flags;
        public int outputTechnology;
        public ushort edidManufactureId;
        public ushort edidProductCodeId;
        public uint connectorInstance;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string monitorFriendlyDeviceName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string monitorDevicePath;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_TARGET_PREFERRED_MODE
    {
        public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
        public uint width;
        public uint height;
        public DisplayManager.DISPLAYCONFIG_TARGET_MODE targetMode;
    }

    [DllImport("User32.dll")]
    private static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_TARGET_DEVICE_NAME requestPacket);

    [DllImport("User32.dll", EntryPoint = "DisplayConfigGetDeviceInfo")]
    private static extern int DisplayConfigGetPreferredMode(ref DISPLAYCONFIG_TARGET_PREFERRED_MODE requestPacket);

    public static DisplayTopologySnapshot QueryCurrent()
    {
        const uint queryFlags = QDC_ALL_PATHS;

        for (int attempt = 1; attempt <= QueryAttempts; attempt++)
        {
            int error = DisplayManager.GetDisplayConfigBufferSizes(queryFlags, out uint pathCount, out uint modeCount);
            if (error != 0)
            {
                throw new Win32Exception(error, $"GetDisplayConfigBufferSizes failed with code {error}.");
            }

            DisplayManager.DISPLAYCONFIG_PATH_INFO[] paths = new DisplayManager.DISPLAYCONFIG_PATH_INFO[pathCount];
            DisplayManager.DISPLAYCONFIG_MODE_INFO[] modes = new DisplayManager.DISPLAYCONFIG_MODE_INFO[modeCount];

            error = DisplayManager.QueryDisplayConfig(
                queryFlags,
                ref pathCount,
                paths,
                ref modeCount,
                modes,
                IntPtr.Zero);

            if (error == ERROR_INSUFFICIENT_BUFFER && attempt < QueryAttempts)
            {
                continue;
            }

            if (error != 0)
            {
                throw new Win32Exception(error, $"QueryDisplayConfig failed with code {error}.");
            }

            Array.Resize(ref paths, checked((int)pathCount));
            Array.Resize(ref modes, checked((int)modeCount));

            return new DisplayTopologySnapshot
            {
                Paths = paths,
                Modes = modes,
                Monitors = BuildCurrentTargets(paths)
            };
        }

        throw new InvalidOperationException("Display topology changed repeatedly while it was being read.");
    }

    internal static bool TryGetSourceMode(
        DisplayTopologySnapshot topology,
        CurrentDisplayTarget monitor,
        out DisplayManager.DISPLAYCONFIG_SOURCE_MODE sourceMode)
    {
        DisplayManager.DISPLAYCONFIG_PATH_INFO path = monitor.Path;
        uint modeIndex;

        if ((path.flags & DISPLAYCONFIG_PATH_SUPPORT_VIRTUAL_MODE) != 0)
        {
            modeIndex = (path.sourceInfo.modeInfoIdx >> 16) & 0xFFFF;
            if (modeIndex == 0xFFFF)
            {
                sourceMode = default;
                return false;
            }
        }
        else
        {
            modeIndex = path.sourceInfo.modeInfoIdx;
            if (modeIndex == DISPLAYCONFIG_PATH_MODE_IDX_INVALID)
            {
                sourceMode = default;
                return false;
            }
        }

        if (modeIndex >= topology.Modes.Length ||
            topology.Modes[modeIndex].infoType != DisplayManager.DISPLAYCONFIG_MODE_INFO_TYPE.DISPLAYCONFIG_MODE_INFO_TYPE_SOURCE)
        {
            sourceMode = default;
            return false;
        }

        sourceMode = topology.Modes[modeIndex].modeInfo.sourceMode;
        return true;
    }

    internal static bool TryGetTargetMode(
        DisplayTopologySnapshot topology,
        DisplayManager.DISPLAYCONFIG_PATH_INFO path,
        out DisplayManager.DISPLAYCONFIG_TARGET_MODE targetMode)
    {
        uint modeIndex;
        if ((path.flags & DISPLAYCONFIG_PATH_SUPPORT_VIRTUAL_MODE) != 0)
        {
            modeIndex = (path.targetInfo.modeInfoIdx >> 16) & 0xFFFF;
            if (modeIndex == 0xFFFF)
            {
                targetMode = default;
                return false;
            }
        }
        else
        {
            modeIndex = path.targetInfo.modeInfoIdx;
            if (modeIndex == DISPLAYCONFIG_PATH_MODE_IDX_INVALID)
            {
                targetMode = default;
                return false;
            }
        }

        if (modeIndex >= topology.Modes.Length ||
            topology.Modes[modeIndex].infoType != DisplayManager.DISPLAYCONFIG_MODE_INFO_TYPE.DISPLAYCONFIG_MODE_INFO_TYPE_TARGET)
        {
            targetMode = default;
            return false;
        }

        targetMode = topology.Modes[modeIndex].modeInfo.targetMode;
        return true;
    }

    internal static DisplayManager.DISPLAYCONFIG_TARGET_MODE GetPreferredTargetMode(
        DisplayManager.DISPLAYCONFIG_PATH_INFO path)
    {
        const int displayConfigDeviceInfoGetTargetPreferredMode = 3;
        DISPLAYCONFIG_TARGET_PREFERRED_MODE preferredMode = new()
        {
            header = new DISPLAYCONFIG_DEVICE_INFO_HEADER
            {
                type = displayConfigDeviceInfoGetTargetPreferredMode,
                size = (uint)Marshal.SizeOf<DISPLAYCONFIG_TARGET_PREFERRED_MODE>(),
                adapterId = path.targetInfo.adapterId,
                id = path.targetInfo.id
            }
        };

        int error = DisplayConfigGetPreferredMode(ref preferredMode);
        if (error != 0)
        {
            throw new Win32Exception(error, $"DisplayConfigGetDeviceInfo preferred mode failed with code {error}.");
        }

        return preferredMode.targetMode;
    }

    public static LogicalProfileResolution Resolve(LogicalDisplayProfile profile, DisplayTopologySnapshot topology)
    {
        LogicalProfileStore.Validate(profile);
        ArgumentNullException.ThrowIfNull(topology);

        List<CurrentDisplayTarget> available = topology.Monitors
            .Where(monitor => monitor.IsAvailable)
            .ToList();
        List<LogicalMonitorMatch> matches = new();
        List<string> errors = new();

        IEnumerable<LogicalMonitorConfiguration> requestedMonitors = profile.Monitors
            .OrderByDescending(monitor => GetIdentityStrength(monitor.Identity));

        foreach (LogicalMonitorConfiguration requested in requestedMonitors)
        {
            List<(CurrentDisplayTarget Monitor, int Score)> candidates = available
                .Select(monitor => (Monitor: monitor, Score: ScoreIdentity(requested.Identity, monitor.Identity)))
                .Where(candidate => candidate.Score > 0)
                .OrderByDescending(candidate => candidate.Score)
                .ToList();

            if (candidates.Count == 0)
            {
                errors.Add($"Monitor not found: {GetDisplayLabel(requested.Identity)}");
                continue;
            }

            int bestScore = candidates[0].Score;
            if (candidates.Count(candidate => candidate.Score == bestScore) > 1)
            {
                errors.Add($"Monitor identity is ambiguous: {GetDisplayLabel(requested.Identity)}");
                continue;
            }

            CurrentDisplayTarget match = candidates[0].Monitor;
            matches.Add(new LogicalMonitorMatch(requested, match, bestScore));
            available.Remove(match);
        }

        return new LogicalProfileResolution
        {
            Matches = matches,
            Errors = errors
        };
    }

    private static IReadOnlyList<CurrentDisplayTarget> BuildCurrentTargets(
        DisplayManager.DISPLAYCONFIG_PATH_INFO[] paths)
    {
        Dictionary<string, CurrentDisplayTarget> targets = new(StringComparer.OrdinalIgnoreCase);

        for (int pathIndex = 0; pathIndex < paths.Length; pathIndex++)
        {
            DisplayManager.DISPLAYCONFIG_PATH_INFO path = paths[pathIndex];
            DISPLAYCONFIG_TARGET_DEVICE_NAME targetName = new()
            {
                header = new DISPLAYCONFIG_DEVICE_INFO_HEADER
                {
                    type = DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME,
                    size = (uint)Marshal.SizeOf<DISPLAYCONFIG_TARGET_DEVICE_NAME>(),
                    adapterId = path.targetInfo.adapterId,
                    id = path.targetInfo.id
                },
                monitorFriendlyDeviceName = string.Empty,
                monitorDevicePath = string.Empty
            };

            int error = DisplayConfigGetDeviceInfo(ref targetName);
            if (error != 0)
            {
                continue;
            }

            bool isActive = (path.flags & DISPLAYCONFIG_PATH_ACTIVE) != 0;
            CurrentDisplayTarget target = new()
            {
                PathIndex = pathIndex,
                Path = path,
                Identity = BuildIdentity(targetName),
                IsActive = isActive,
                IsAvailable = path.targetInfo.targetAvailable
            };

            string key = GetTargetKey(path.targetInfo.adapterId, path.targetInfo.id);
            if (!targets.TryGetValue(key, out CurrentDisplayTarget? existing) || (!existing.IsActive && isActive))
            {
                targets[key] = target;
            }
        }

        return targets.Values.ToList();
    }

    private static LogicalMonitorIdentity BuildIdentity(DISPLAYCONFIG_TARGET_DEVICE_NAME target)
    {
        EdidIdentity? edid = EdidIdentityReader.TryRead(target.monitorDevicePath);
        string manufacturerId = edid?.ManufacturerId
            ?? EdidIdentityReader.DecodeManufacturerId(target.edidManufactureId);
        string productCode = edid?.ProductCode
            ?? target.edidProductCodeId.ToString("X4", CultureInfo.InvariantCulture);
        string? serialNumber = NormalizeOptional(edid?.SerialNumber);
        string? devicePath = NormalizeOptional(target.monitorDevicePath);

        string stableId = serialNumber is not null
            ? $"EDID:{manufacturerId}:{productCode}:{NormalizeStableComponent(serialNumber)}"
            : devicePath is not null
                ? $"PATH:{devicePath.ToUpperInvariant()}"
                : $"TARGET:{manufacturerId}:{productCode}:{target.outputTechnology}:{target.connectorInstance}";

        return new LogicalMonitorIdentity
        {
            StableId = stableId,
            ManufacturerId = manufacturerId,
            ProductCode = productCode,
            SerialNumber = serialNumber,
            FriendlyName = NormalizeOptional(target.monitorFriendlyDeviceName) ?? edid?.MonitorName,
            DevicePathHint = devicePath,
            OutputTechnologyHint = target.outputTechnology
        };
    }

    private static int ScoreIdentity(LogicalMonitorIdentity expected, LogicalMonitorIdentity actual)
    {
        if (EqualsIgnoreCase(expected.StableId, actual.StableId))
        {
            return 10_000;
        }

        bool sameManufacturer = EqualsIgnoreCase(expected.ManufacturerId, actual.ManufacturerId);
        bool sameProduct = EqualsIgnoreCase(expected.ProductCode, actual.ProductCode);
        bool sameSerial = !string.IsNullOrWhiteSpace(expected.SerialNumber) &&
            EqualsIgnoreCase(expected.SerialNumber, actual.SerialNumber);

        if (sameManufacturer && sameProduct && sameSerial)
        {
            return 9_000;
        }

        if (!string.IsNullOrWhiteSpace(expected.DevicePathHint) &&
            EqualsIgnoreCase(expected.DevicePathHint, actual.DevicePathHint))
        {
            return 8_000;
        }

        if (!sameManufacturer || !sameProduct)
        {
            return 0;
        }

        int score = 5_000;
        if (expected.OutputTechnologyHint == actual.OutputTechnologyHint)
        {
            score += 500;
        }

        if (!string.IsNullOrWhiteSpace(expected.FriendlyName) &&
            EqualsIgnoreCase(expected.FriendlyName, actual.FriendlyName))
        {
            score += 100;
        }

        return score;
    }

    private static int GetIdentityStrength(LogicalMonitorIdentity identity)
    {
        if (!string.IsNullOrWhiteSpace(identity.SerialNumber))
        {
            return 3;
        }

        if (!string.IsNullOrWhiteSpace(identity.DevicePathHint))
        {
            return 2;
        }

        return 1;
    }

    private static string GetDisplayLabel(LogicalMonitorIdentity identity)
    {
        return identity.FriendlyName ?? identity.StableId;
    }

    private static string GetTargetKey(DisplayManager.LUID adapterId, uint targetId)
    {
        return $"{adapterId.HighPart:X8}:{adapterId.LowPart:X8}:{targetId:X8}";
    }

    private static bool EqualsIgnoreCase(string? left, string? right)
    {
        return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    }

    private static string? NormalizeOptional(string? value)
    {
        string? normalized = value?.Trim('\0', '\r', '\n', ' ');
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private static string NormalizeStableComponent(string value)
    {
        char[] normalized = value
            .Where(character => char.IsLetterOrDigit(character) || character is '-' or '_')
            .Select(char.ToUpperInvariant)
            .ToArray();

        return normalized.Length > 0 ? new string(normalized) : "UNKNOWN";
    }
}
