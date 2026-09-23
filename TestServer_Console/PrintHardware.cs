using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Hardware.Info;

public class HardwarePrinter
{


    private static readonly StringBuilder sb = new StringBuilder(8192); // Preallocate generous capacity for reuse
    private static readonly StringBuilder lineSb = new StringBuilder(1024); // Preallocate for lines

    private static readonly string[] keys = new string[]
    {
        "Name",
        "Manufacturer",
        "Description",
        "Cores",
        "Logical Processors",
        "Current Clock",
        "Max Clock",
        "L1 Instr Cache",
        "L1 Data Cache",
        "L2 Cache",
        "L3 Cache",
        "Socket",
        "Usage"
    };

    internal static void PrintHardware(IHardwareInfo hardwareInfo)
    {
        // Enable ANSI escape sequences on Windows for colors (call once, but safe to repeat)
        if (OperatingSystem.IsWindows())
        {
            var handle = GetStdHandle(-11);
            if (GetConsoleMode(handle, out uint mode))
            {
                SetConsoleMode(handle, mode | 0x0004); // ENABLE_VIRTUAL_TERMINAL_PROCESSING
            }
        }

        
        // Reuse builders by clearing
        sb.Clear();

        const string Reset = "\u001b[0m";
        const string Cyan = "\u001b[36m";
        const string Blue = "\u001b[34m";
        const string Gray = "\u001b[90m";

        int boxWidth = Console.IsOutputRedirected ? 100 : Console.WindowWidth;
        int contentWidth = boxWidth - 4; // For "│ " and " │"

        int cpuIndex = 1;
        foreach (var cpu in hardwareInfo.CpuList)
        {
            // Header with dynamic width
            string headerStart = $"┌── CPU {cpuIndex} ──";
            int remainingDashes = boxWidth - headerStart.Length - 1;
            sb.Append(Cyan).Append(headerStart).Append(new string('─', remainingDashes)).Append(Reset).Append('\n');

            // Get values as array to avoid list allocation
            string[] values =
            [
                cpu.Name,
                cpu.Manufacturer,
                cpu.Description,
                cpu.NumberOfCores.ToString(),
                cpu.NumberOfLogicalProcessors.ToString(),
                $"{cpu.CurrentClockSpeed} MHz",
                $"{cpu.MaxClockSpeed} MHz",
                $"{cpu.L1InstructionCacheSize / 1024} KB",
                $"{cpu.L1DataCacheSize  / 1024} KB",
                $"{cpu.L2CacheSize  / 1024 / 1024} MB",
                $"{cpu.L3CacheSize  / 1024 / 1024} MB",
                cpu.SocketDesignation,
                $"{cpu.PercentProcessorTime}%"
            ];

            // Flow layout
            lineSb.Clear();
            lineSb.Append("│ ");
            int currentPosition = 2; // Visible chars after start
            int maxPosition = boxWidth - 2; // Before closing " │"

            bool firstInLine = true;
            for (int i = 0; i < keys.Length; i++)
            {
                string key = keys[i];
                string value = values[i];

                int separatorVisible = firstInLine ? 0 : 3; // " | "
                int entryVisible = key.Length + 2 + value.Length; // ": "
                int totalAdded = separatorVisible + entryVisible;

                if (currentPosition + totalAdded > maxPosition)
                {
                    // Pad and close current line
                    int padNeeded = maxPosition - currentPosition + 1;
                    lineSb.Append(new string(' ', padNeeded)).Append("│");
                    sb.Append(lineSb).Append('\n');

                    // Start new line
                    lineSb.Clear();
                    lineSb.Append("│ ");
                    currentPosition = 2;
                    firstInLine = true;
                }

                if (!firstInLine)
                {
                    lineSb.Append(Gray).Append(" | ").Append(Reset);
                    currentPosition += 3;
                }

                lineSb.Append(Blue).Append(key).Append(Reset).Append(": ");
                currentPosition += key.Length + 2;

                string valueColor = "";
                if (key == "Usage")
                {
                    valueColor = GetUsageAnsi(cpu.PercentProcessorTime);
                }

                lineSb.Append(valueColor).Append(value).Append(Reset);
                currentPosition += value.Length;

                firstInLine = false;
            }

            // Close last line if content present
            if (lineSb.Length > 2)
            {
                int padNeeded = maxPosition - currentPosition + 1;
                lineSb.Append(new string(' ', padNeeded)).Append("│");
                sb.Append(lineSb).Append('\n');
            }

            // Bottom border
            string bottomStart = "└──";
            int bottomDashes = boxWidth - bottomStart.Length - 1;
            sb.Append(Cyan).Append(bottomStart).Append(new string('─', bottomDashes)).Append(Reset).Append('\n');

            // Space between CPUs
            sb.Append('\n');

            cpuIndex++;
        }

        // Single write to console
        if (!Console.IsOutputRedirected)
        {
            Console.SetCursorPosition(0, 0);
        }
        Console.Write(sb.ToString());
    }

// Helper for usage color ANSI
    static string GetUsageAnsi(double percent)
    {
        if (percent < 30) return "\u001b[32m"; // Green
        if (percent < 70) return "\u001b[33m"; // Yellow
        return "\u001b[31m"; // Red
    }

// DLL imports for enabling ANSI on Windows
    [SupportedOSPlatform("windows")]
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetStdHandle(int nStdHandle);

    [SupportedOSPlatform("windows")]
    [DllImport("kernel32.dll")]
    private static extern bool GetConsoleMode(IntPtr hConsoleHandle, out uint lpMode);

    [SupportedOSPlatform("windows")]
    [DllImport("kernel32.dll")]
    private static extern bool SetConsoleMode(IntPtr hConsoleHandle, uint dwMode);
}