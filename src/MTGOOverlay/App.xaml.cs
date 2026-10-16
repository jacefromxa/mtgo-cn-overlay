using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;

namespace MTGOOverlay;

public partial class App : System.Windows.Application
{
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern IntPtr FindWindow(string? lpClassName, string lpWindowName);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    private const int SW_RESTORE = 9;

    private static readonly string LogFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MTGOOverlay", "runtime.log");

    public static void Log(string msg)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogFile)!);
            File.AppendAllText(LogFile, $"[{DateTime.Now:HH:mm:ss.fff}] {msg}\n");
        }
        catch { }
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        var currentProc = Process.GetCurrentProcess();
        Log($"OnStartup: PID={currentProc.Id}, ProcessName={currentProc.ProcessName}");

        var existingProcesses = Process.GetProcessesByName(currentProc.ProcessName)
            .Where(p => p.Id != currentProc.Id)
            .ToList();

        if (existingProcesses.Count > 0)
        {
            Log($"Another instance exists ({existingProcesses.Count} instances). Searching for settings window...");
            IntPtr hSettings = FindWindow(null, "MTGO CN Overlay - 设置与控制中心");
            if (hSettings != IntPtr.Zero)
            {
                Log($"Found existing settings window ({hSettings}). Restoring and bringing to foreground.");
                ShowWindow(hSettings, SW_RESTORE);
                SetForegroundWindow(hSettings);
                Shutdown();
                return;
            }

            // If no window exists, the previous instance is an orphaned/headless background process.
            // Terminate the orphan and let the new interactive instance launch fresh!
            Log("No visible window found on existing instance. Terminating orphan process to restart fresh.");
            foreach (var p in existingProcesses)
            {
                try
                {
                    p.Kill();
                    p.WaitForExit(1000);
                }
                catch { }
            }
        }

        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
        {
            Log($"UnhandledException: {args.ExceptionObject}");
        };

        DispatcherUnhandledException += (s, args) =>
        {
            Log($"DispatcherUnhandledException: {args.Exception}");
            args.Handled = true;
        };

        base.OnStartup(e);
        Log("OnStartup completed successfully.");
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log($"OnExit called: Code={e.ApplicationExitCode}\nStackTrace:\n{Environment.StackTrace}");
        base.OnExit(e);
    }
}
