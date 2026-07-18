using System;
using System.Globalization;
using System.IO;
using System.Security;
using System.Text;
using Microsoft.Win32;

namespace MonitorSwitcher;

internal sealed record EdidIdentity(
    string ManufacturerId,
    string ProductCode,
    string? SerialNumber,
    string? MonitorName);

internal static class EdidIdentityReader
{
    private const int MinimumEdidLength = 128;

    public static EdidIdentity? TryRead(string? monitorDevicePath)
    {
        string? instanceId = GetDeviceInstanceId(monitorDevicePath);
        if (instanceId is null)
        {
            return null;
        }

        try
        {
            using RegistryKey baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Default);
            using RegistryKey? parametersKey = baseKey.OpenSubKey(
                $@"SYSTEM\CurrentControlSet\Enum\{instanceId}\Device Parameters",
                writable: false);

            if (parametersKey?.GetValue("EDID") is not byte[] edid || edid.Length < MinimumEdidLength)
            {
                return null;
            }

            ushort manufacturerCode = (ushort)((edid[8] << 8) | edid[9]);
            ushort productCode = (ushort)(edid[10] | (edid[11] << 8));
            uint numericSerial = BitConverter.ToUInt32(edid, 12);

            string? descriptorSerial = ReadDescriptor(edid, 0xFF);
            string? serial = descriptorSerial;
            if (string.IsNullOrWhiteSpace(serial) && numericSerial != 0 && numericSerial != uint.MaxValue)
            {
                serial = numericSerial.ToString(CultureInfo.InvariantCulture);
            }

            return new EdidIdentity(
                DecodeManufacturerId(manufacturerCode),
                productCode.ToString("X4", CultureInfo.InvariantCulture),
                NormalizeOptional(serial),
                NormalizeOptional(ReadDescriptor(edid, 0xFC)));
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }

    public static string DecodeManufacturerId(ushort manufacturerCode)
    {
        Span<char> characters = stackalloc char[3];
        characters[0] = DecodeManufacturerCharacter((manufacturerCode >> 10) & 0x1F);
        characters[1] = DecodeManufacturerCharacter((manufacturerCode >> 5) & 0x1F);
        characters[2] = DecodeManufacturerCharacter(manufacturerCode & 0x1F);
        return new string(characters);
    }

    private static char DecodeManufacturerCharacter(int value)
    {
        return value is >= 1 and <= 26 ? (char)('A' + value - 1) : '?';
    }

    private static string? GetDeviceInstanceId(string? monitorDevicePath)
    {
        if (string.IsNullOrWhiteSpace(monitorDevicePath))
        {
            return null;
        }

        string value = monitorDevicePath.Trim();
        if (value.StartsWith(@"\\?\", StringComparison.Ordinal))
        {
            value = value[4..];
        }

        int interfaceClassStart = value.IndexOf("#{", StringComparison.Ordinal);
        if (interfaceClassStart >= 0)
        {
            value = value[..interfaceClassStart];
        }

        value = value.Replace('#', '\\');
        return value.StartsWith(@"DISPLAY\", StringComparison.OrdinalIgnoreCase) ? value : null;
    }

    private static string? ReadDescriptor(byte[] edid, byte descriptorType)
    {
        const int firstDescriptorOffset = 54;
        const int descriptorLength = 18;

        for (int offset = firstDescriptorOffset; offset + descriptorLength <= edid.Length && offset < 126; offset += descriptorLength)
        {
            if (edid[offset] == 0 &&
                edid[offset + 1] == 0 &&
                edid[offset + 2] == 0 &&
                edid[offset + 3] == descriptorType)
            {
                return Encoding.ASCII.GetString(edid, offset + 5, 13);
            }
        }

        return null;
    }

    private static string? NormalizeOptional(string? value)
    {
        string? normalized = value?.Trim('\0', '\r', '\n', ' ');
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }
}
