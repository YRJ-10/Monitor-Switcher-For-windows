using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MonitorSwitcher;

public static class LogicalProfileCapture
{
    public static LogicalDisplayProfile CaptureCurrent(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Profile name is required.", nameof(name));
        }

        DisplayTopologySnapshot topology = DisplayTopologyResolver.QueryCurrent();
        List<CurrentDisplayTarget> monitors = topology.Monitors
            .Where(monitor => monitor.IsAvailable)
            .OrderByDescending(monitor => monitor.IsActive)
            .ThenBy(monitor => monitor.Identity.FriendlyName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (monitors.Count == 0)
        {
            throw new InvalidOperationException("Windows did not report any available monitors.");
        }

        Dictionary<CurrentDisplayTarget, DisplayManager.DISPLAYCONFIG_SOURCE_MODE> activeModes = new();
        foreach (CurrentDisplayTarget monitor in monitors.Where(monitor => monitor.IsActive))
        {
            if (!DisplayTopologyResolver.TryGetSourceMode(topology, monitor, out DisplayManager.DISPLAYCONFIG_SOURCE_MODE sourceMode))
            {
                throw new InvalidDataException($"Windows did not return a source mode for {monitor.Identity.FriendlyName ?? monitor.Identity.StableId}.");
            }

            activeModes[monitor] = sourceMode;
        }

        CurrentDisplayTarget? primary = monitors.FirstOrDefault(monitor =>
            activeModes.TryGetValue(monitor, out DisplayManager.DISPLAYCONFIG_SOURCE_MODE mode) &&
            mode.position.x == 0 &&
            mode.position.y == 0);
        primary ??= monitors.FirstOrDefault(monitor => monitor.IsActive);

        List<LogicalMonitorConfiguration> configurations = monitors.Select(monitor =>
        {
            bool isActive = activeModes.TryGetValue(monitor, out DisplayManager.DISPLAYCONFIG_SOURCE_MODE sourceMode);
            return new LogicalMonitorConfiguration
            {
                Identity = monitor.Identity,
                Enabled = isActive,
                Primary = ReferenceEquals(monitor, primary),
                Bounds = isActive
                    ? new DisplayRectangle(sourceMode.position.x, sourceMode.position.y, sourceMode.width, sourceMode.height)
                    : default,
                Orientation = FromNativeOrientation(monitor.Path.targetInfo.rotation),
                RefreshRate = new DisplayRefreshRate(
                    monitor.Path.targetInfo.refreshRate.Numerator,
                    monitor.Path.targetInfo.refreshRate.Denominator)
            };
        }).ToList();

        LogicalDisplayProfile profile = new()
        {
            Name = name.Trim(),
            Monitors = configurations
        };

        LogicalProfileStore.Validate(profile);
        return profile;
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
}
