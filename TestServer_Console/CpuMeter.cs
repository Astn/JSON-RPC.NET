using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace TestServer_Console;

/// <summary>CPU consumed during one benchmark window, as a share of the cores available to the process.</summary>
internal readonly record struct CpuUsage(double? SystemPercent, double? ProcessPercent, double? ClientPercent, int Cores)
{
    internal string TrailingText => "cpu: system " + Format(SystemPercent) + ", process " + Format(ProcessPercent)
        + (ClientPercent.HasValue ? ", clients " + Format(ClientPercent) : "");

    private static string Format(double? value) => value.HasValue ? $"{value.Value:0.#} %" : "n/a";
}

/// <summary>Reads aggregate and process CPU counters only at the edges of a timed run.</summary>
internal sealed class CpuMeter
{
    private enum Source { None, Cgroup, ProcStat, Windows }

    private readonly long _started;
    private readonly double? _processStart;
    private readonly Source _source;
    private readonly double _capacity;
    private readonly ulong _systemStart;
    private readonly ulong _systemTotalStart;

    private CpuMeter()
    {
        Cores = Environment.ProcessorCount;
        _started = Stopwatch.GetTimestamp();
        _processStart = ProcessTime();
        if (OperatingSystem.IsLinux())
        {
            if (TryQuota(out double quota))
            {
                if (TryCgroupUsage(out _systemStart))
                {
                    _source = Source.Cgroup;
                    _capacity = quota;
                }
            }
            else if (TryProcStat(out _systemStart, out _systemTotalStart))
            {
                _source = Source.ProcStat;
            }
        }
        else if (OperatingSystem.IsWindows() && TryWindowsSystem(out _systemStart, out _systemTotalStart))
        {
            _source = Source.Windows;
        }
    }

    internal int Cores { get; }

    internal static CpuMeter Start() => new CpuMeter();

    internal CpuUsage Stop(double? clientSeconds = null)
    {
        double wall = Stopwatch.GetElapsedTime(_started).TotalSeconds;
        double? processEnd = ProcessTime();
        double? process = _processStart.HasValue && processEnd.HasValue
            ? Percent(processEnd.Value - _processStart.Value, wall * Cores) : null;
        double? system = null;
        if (_source == Source.Cgroup && TryCgroupUsage(out ulong usage))
            system = Percent((usage - _systemStart) / 1_000_000.0, wall * _capacity);
        else if (_source == Source.ProcStat && TryProcStat(out ulong busy, out ulong total))
            system = Percent(busy - _systemStart, total - _systemTotalStart);
        else if (_source == Source.Windows && TryWindowsSystem(out ulong winBusy, out ulong winTotal))
            system = Percent(winBusy - _systemStart, winTotal - _systemTotalStart);
        return new CpuUsage(system, process, clientSeconds.HasValue ? Percent(clientSeconds.Value, wall * Cores) : null, Cores);
    }

    private static double Percent(double amount, double capacity) => capacity > 0
        ? Math.Clamp(100 * amount / capacity, 0, 100) : 0;

    private static double? ProcessTime()
    {
        try
        {
            using var process = Process.GetCurrentProcess();
            return process.TotalProcessorTime.TotalSeconds;
        }
        catch (Exception) { return null; }
    }

    internal static bool TryQuota(out double cores)
    {
        cores = 0;
        try
        {
            if (!File.Exists("/sys/fs/cgroup/cpu.max")) return false;
            var parts = File.ReadAllText("/sys/fs/cgroup/cpu.max").Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2 || parts[0] == "max" ||
                !double.TryParse(parts[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double quota) ||
                !double.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double period) ||
                quota <= 0 || period <= 0) return false;
            cores = quota / period;
            return true;
        }
        catch (Exception) { return false; }
    }

    private static bool TryCgroupUsage(out ulong usage)
    {
        usage = 0;
        try
        {
            var line = File.ReadLines("/sys/fs/cgroup/cpu.stat").FirstOrDefault(s => s.StartsWith("usage_usec ", StringComparison.Ordinal));
            return line != null && ulong.TryParse(line.AsSpan("usage_usec ".Length), out usage);
        }
        catch (Exception) { return false; }
    }

    private static bool TryProcStat(out ulong busy, out ulong total)
    {
        busy = total = 0;
        try
        {
            var parts = File.ReadLines("/proc/stat").First().Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 6 || parts[0] != "cpu") return false;
            for (int i = 1; i < parts.Length; i++)
            {
                if (!ulong.TryParse(parts[i], out ulong value)) return false;
                total += value;
            }
            busy = total - ulong.Parse(parts[4]) - ulong.Parse(parts[5]);
            return true;
        }
        catch (Exception) { return false; }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileTime { internal uint Low; internal uint High; internal readonly ulong Value => ((ulong)High << 32) | Low; }

    private static bool TryWindowsSystem(out ulong busy, out ulong total)
    {
        busy = total = 0;
        if (!GetSystemTimes(out var idle, out var kernel, out var user)) return false;
        busy = kernel.Value - idle.Value + user.Value;
        total = kernel.Value + user.Value;
        return true;
    }

    /// <summary>CPU seconds used by the calling thread; null when the platform cannot provide them.</summary>
    internal static double? ThreadTime()
    {
        if (OperatingSystem.IsWindows())
        {
            if (!GetThreadTimes(GetCurrentThread(), out _, out _, out var kernel, out var user)) return null;
            return (kernel.Value + user.Value) / 10_000_000.0;
        }
        if (OperatingSystem.IsLinux())
        {
            if (clock_gettime(3, out var time) != 0) return null; // CLOCK_THREAD_CPUTIME_ID
            return time.Seconds + time.Nanoseconds / 1_000_000_000.0;
        }
        return null;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Timespec { internal long Seconds; internal long Nanoseconds; }

    [DllImport("kernel32.dll")]
    private static extern bool GetSystemTimes(out FileTime idle, out FileTime kernel, out FileTime user);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentThread();

    [DllImport("kernel32.dll")]
    private static extern bool GetThreadTimes(IntPtr thread, out FileTime creation, out FileTime exit, out FileTime kernel, out FileTime user);

    [DllImport("libc", EntryPoint = "clock_gettime")]
    private static extern int clock_gettime(int clockId, out Timespec time);
}
