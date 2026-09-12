using System.IO.Ports;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace TKPay.Naps.Connection;

/// <summary>
/// Auto-discovers the Ingenico Lane/3000 serial port by USB vendor id 0x0B00,
/// falling back to a CDC-ACM device (usbmodem / ttyACM) if no VID match is found.
/// <list type="bullet">
/// <item>Windows: scans the registry for USB serial ports with Ingenico VID.</item>
/// <item>macOS: finds /dev/cu.usbmodem* via ioreg.</item>
/// <item>Linux: finds /dev/ttyACM* with a matching VID in sysfs.</item>
/// </list>
/// </summary>
public static class PortDiscovery
{
    /// <summary>Ingenico USB vendor id (0x0B00 = 2816).</summary>
    public const int IngenicoVendorId = 0x0B00;

    /// <summary>
    /// Locate the Ingenico Lane/3000 serial port, or <c>null</c> if none is found.
    /// Prefers a device matching VID 0x0B00; otherwise falls back to the first
    /// CDC-ACM device present.
    /// </summary>
    public static string? FindIngenicoPort()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return FindOnMacOs();
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return FindOnWindows();
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return FindOnLinux();
        return null;
    }

    private static string? FindOnMacOs()
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo("ioreg", "-r -c IOUSBHostDevice -l")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = System.Diagnostics.Process.Start(psi);
            if (proc == null) return null;
            var output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(3000);

            foreach (var block in output.Split(new[] { "+-o " }, StringSplitOptions.None))
            {
                if (!block.Contains("\"idVendor\" = 2816") && !block.Contains("INGENICO"))
                    continue;
                var match = Regex.Match(block, "\"IOCalloutDevice\"\\s*=\\s*\"([^\"]+)\"");
                if (match.Success) return match.Groups[1].Value;
                match = Regex.Match(block, "cu\\.usbmodem\\S+");
                if (match.Success) return "/dev/" + match.Value.TrimEnd(',', '"');
            }
        }
        catch { /* ioreg not available or failed */ }

        // Fallback: newest cu.usbmodem (CDC-ACM) device
        try
        {
            return Directory.GetFiles("/dev/", "cu.usbmodem*")
                .OrderByDescending(File.GetLastWriteTime)
                .FirstOrDefault();
        }
        catch { return null; }
    }

#if NET5_0_OR_GREATER
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
#endif
    private static string? FindOnWindows()
    {
        // Scan the registry for USB serial ports with the Ingenico VID.
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\USB");
            if (key == null) return null;

            foreach (var vidPid in key.GetSubKeyNames())
            {
                if (vidPid.IndexOf("VID_0B00", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                using var vidKey = key.OpenSubKey(vidPid);
                if (vidKey == null) continue;
                foreach (var serial in vidKey.GetSubKeyNames())
                {
                    using var devKey = vidKey.OpenSubKey(serial + @"\Device Parameters");
                    if (devKey?.GetValue("PortName") is string portName)
                        return portName;
                }
            }
        }
        catch { /* registry access failed */ }

        // Fallback: first available COM port
        return SerialPort.GetPortNames().FirstOrDefault();
    }

    private static string? FindOnLinux()
    {
        // Check /sys/class/tty/ttyACM*/device/uevent for VID 0B00.
        try
        {
            foreach (var dir in Directory.GetDirectories("/sys/class/tty/", "ttyACM*"))
            {
                var ueventPath = Path.Combine(dir, "device", "uevent");
                if (!File.Exists(ueventPath)) continue;
                var uevent = File.ReadAllText(ueventPath);
                if (uevent.IndexOf("0B00", StringComparison.OrdinalIgnoreCase) >= 0)
                    return "/dev/" + Path.GetFileName(dir);
            }
        }
        catch { /* sysfs not available */ }

        // Fallback: first CDC-ACM device
        try
        {
            return Directory.GetFiles("/dev/", "ttyACM*").FirstOrDefault();
        }
        catch { return null; }
    }

    /// <summary>List all available serial ports.</summary>
    public static string[] ListPorts()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            try
            {
                var devPorts = Directory.GetFiles("/dev/", "cu.usbmodem*")
                    .Concat(Directory.GetFiles("/dev/", "cu.usbserial*"))
                    .ToArray();
                return devPorts.Length > 0 ? devPorts : SerialPort.GetPortNames();
            }
            catch { /* fallthrough */ }
        }
        return SerialPort.GetPortNames();
    }

    /// <summary>
    /// Select the Ingenico port from a caller-supplied list of candidates —
    /// the pure selection logic used by <see cref="FindIngenicoPort"/>, exposed
    /// for testing and for callers holding their own enumeration.
    /// A candidate matches when its vendor id equals <see cref="IngenicoVendorId"/>;
    /// otherwise the first CDC-ACM candidate (usbmodem / ttyACM) is used; otherwise
    /// <c>null</c>.
    /// </summary>
    public static string? SelectIngenicoPort(IEnumerable<SerialPortCandidate> candidates)
    {
        var list = candidates as IReadOnlyList<SerialPortCandidate> ?? candidates.ToList();

        foreach (var c in list)
            if (c.VendorId == IngenicoVendorId)
                return c.PortName;

        foreach (var c in list)
            if (IsCdcAcm(c.PortName))
                return c.PortName;

        return null;
    }

    private static bool IsCdcAcm(string portName) =>
        portName.Contains("usbmodem", StringComparison.OrdinalIgnoreCase) ||
        portName.Contains("ttyACM", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// A discovered serial port and its USB vendor id (or <c>null</c> if unknown),
/// used by <see cref="PortDiscovery.SelectIngenicoPort"/>.
/// </summary>
/// <param name="PortName">Device path or COM name (e.g. "/dev/cu.usbmodem1401", "COM3").</param>
/// <param name="VendorId">USB vendor id, or <c>null</c> if it could not be read.</param>
public readonly record struct SerialPortCandidate(string PortName, int? VendorId);
