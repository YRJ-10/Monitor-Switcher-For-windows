using System;
using System.Collections.Generic;

namespace MonitorSwitcher;

public sealed class LogicalDisplayProfile
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public required string Name { get; init; }
    public DateTimeOffset CapturedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public required List<LogicalMonitorConfiguration> Monitors { get; init; }
}

public sealed class LogicalMonitorConfiguration
{
    public required LogicalMonitorIdentity Identity { get; init; }
    public bool Enabled { get; init; }
    public bool Primary { get; init; }
    public DisplayRectangle Bounds { get; init; }
    public DisplayOrientation Orientation { get; init; } = DisplayOrientation.Landscape;
    public DisplayRefreshRate RefreshRate { get; init; }
}

public sealed class LogicalMonitorIdentity
{
    // StableId is derived from EDID identity. The path and connector are matching fallbacks.
    public required string StableId { get; init; }
    public string? ManufacturerId { get; init; }
    public string? ProductCode { get; init; }
    public string? SerialNumber { get; init; }
    public string? FriendlyName { get; init; }
    public string? DevicePathHint { get; init; }
    public int? OutputTechnologyHint { get; init; }
}

public readonly record struct DisplayRectangle(int X, int Y, uint Width, uint Height);

public readonly record struct DisplayRefreshRate(uint Numerator, uint Denominator);

public enum DisplayOrientation
{
    Landscape,
    Portrait,
    LandscapeFlipped,
    PortraitFlipped
}
