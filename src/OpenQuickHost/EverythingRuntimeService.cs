using System.Diagnostics;
using System.IO;

namespace OpenQuickHost;

public static class EverythingRuntimeService
{
    private const string RuntimeDirectoryName = "EverythingRuntime";
    private const string RuntimeExecutableName = "Everything.exe";
    private static readonly Lock SyncLock = new();
    private static int? _launchedProcessId;

    public static void EnsureStartedInBackground()
    {
        _ = Task.Run(() => EnsureRunning());
    }

    public static bool IsProcessRunning()
    {
        return EverythingProcessExists();
    }

    public static bool HasBundledRuntime()
    {
        return File.Exists(GetBundledRuntimeExecutablePath());
    }

    public static bool ShowInteractiveSetup()
    {
        var runtimeExecutablePath = GetBundledRuntimeExecutablePath();
        if (!File.Exists(runtimeExecutablePath))
        {
            HostAssets.AppendLog($"Everything interactive setup skipped: bundled executable not found at {runtimeExecutablePath}.");
            return false;
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = runtimeExecutablePath,
                WorkingDirectory = Path.GetDirectoryName(runtimeExecutablePath) ?? AppContext.BaseDirectory,
                UseShellExecute = true
            };

            Process.Start(startInfo);
            HostAssets.AppendLog("Everything interactive setup launched.");
            return true;
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"Everything interactive setup launch failed: {ex.Message}");
            return false;
        }
    }

    public static bool EnsureRunning()
    {
        lock (SyncLock)
        {
            if (EverythingSearchService.IsIpcReachable())
            {
                return true;
            }

            if (EverythingProcessExists())
            {
                HostAssets.AppendLog("Everything runtime skipped: existing Everything process detected.");
                return false;
            }
            var runtimeExecutablePath = GetBundledRuntimeExecutablePath();
            if (!File.Exists(runtimeExecutablePath))
            {
                HostAssets.AppendLog($"Everything runtime skipped: bundled executable not found at {runtimeExecutablePath}.");
                return false;
            }

            Directory.CreateDirectory(HostAssets.EverythingRuntimeDataPath);

            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = runtimeExecutablePath,
                    Arguments = BuildArguments(),
                    WorkingDirectory = Path.GetDirectoryName(runtimeExecutablePath) ?? AppContext.BaseDirectory,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };

                var process = Process.Start(startInfo);
                _launchedProcessId = process?.Id;
                HostAssets.AppendLog($"Everything runtime launch requested: path={runtimeExecutablePath}, args={startInfo.Arguments}");
            }
            catch (Exception ex)
            {
                HostAssets.AppendLog($"Everything runtime launch failed: {ex.Message}");
                return false;
            }

            return WaitForIpcReady(TimeSpan.FromSeconds(5));
        }
    }

    public static void StopOwnedRuntime()
    {
        int? processId;
        lock (SyncLock)
        {
            processId = _launchedProcessId;
            _launchedProcessId = null;
        }

        if (processId == null)
        {
            return;
        }

        try
        {
            using var process = Process.GetProcessById(processId.Value);
            if (process.HasExited)
            {
                return;
            }

            process.Kill(entireProcessTree: true);
            HostAssets.AppendLog($"Everything runtime stopped: pid={processId.Value}");
        }
        catch (ArgumentException)
        {
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"Everything runtime stop failed: pid={processId.Value}, error={ex.Message}");
        }
    }

    private static string GetBundledRuntimeExecutablePath()
    {
        return Path.Combine(AppContext.BaseDirectory, RuntimeDirectoryName, RuntimeExecutableName);
    }

    private static bool EverythingProcessExists()
    {
        try
        {
            return Process.GetProcessesByName("Everything").Any();
        }
        catch
        {
            return false;
        }
    }

    private static bool WaitForIpcReady(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (EverythingSearchService.IsIpcReachable())
            {
                HostAssets.AppendLog("Everything runtime is reachable over IPC.");
                return true;
            }

            Thread.Sleep(200);
        }

        HostAssets.AppendLog("Everything runtime did not become reachable before timeout.");
        return false;
    }

    private static string BuildArguments()
    {
        return string.Join(
            ' ',
            [
                "-startup",
                "-config", Quote(HostAssets.EverythingRuntimeConfigPath),
                "-db", Quote(HostAssets.EverythingRuntimeDatabasePath)
            ]);
    }

    private static string Quote(string value)
    {
        return $"\"{value}\"";
    }
}
