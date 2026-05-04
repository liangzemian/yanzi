using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;

namespace Yanzi.ExtensionHost;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        AppendHostLog($"ExtensionHost start: pid={Environment.ProcessId}, args={string.Join(" ", args)}");

        try
        {
            if (!TryGetArgumentValue(args, "--assembly", out var assemblyPath) || string.IsNullOrWhiteSpace(assemblyPath))
            {
                AppendHostLog("ExtensionHost failed: missing --assembly.");
                Console.Error.WriteLine("缺少 --assembly 参数。");
                return 2;
            }

            if (!File.Exists(assemblyPath))
            {
                AppendHostLog($"ExtensionHost failed: assembly not found, path={assemblyPath}");
                Console.Error.WriteLine($"没有找到扩展程序集：{assemblyPath}");
                return 2;
            }

            var extensionDirectory = Environment.GetEnvironmentVariable("YANZI_EXTENSION_DIR");
            if (!string.IsNullOrWhiteSpace(extensionDirectory) && Directory.Exists(extensionDirectory))
            {
                Directory.SetCurrentDirectory(extensionDirectory);
            }
            else
            {
                Directory.SetCurrentDirectory(Path.GetDirectoryName(assemblyPath)!);
            }
            AppendHostLog($"ExtensionHost working directory set: pid={Environment.ProcessId}, cwd={Directory.GetCurrentDirectory()}, assembly={assemblyPath}");

            var loadContext = new AssemblyLoadContext($"yanzi-extension-host-{Guid.NewGuid():N}", isCollectible: true);
            try
            {
                var assembly = loadContext.LoadFromAssemblyPath(assemblyPath);
                var entryPoint = assembly.EntryPoint;
                if (entryPoint == null)
                {
                    AppendHostLog($"ExtensionHost failed: missing entry point, assembly={assemblyPath}");
                    Console.Error.WriteLine("C# 扩展缺少可执行入口。");
                    return 2;
                }

                AppendHostLog($"ExtensionHost invoking entry point: pid={Environment.ProcessId}, entry={entryPoint.DeclaringType?.FullName}.{entryPoint.Name}");
                object? invocationResult;
                var parameters = entryPoint.GetParameters();
                if (parameters.Length == 0)
                {
                    invocationResult = entryPoint.Invoke(null, null);
                }
                else
                {
                    invocationResult = entryPoint.Invoke(null, [Array.Empty<string>()]);
                }

                if (invocationResult is Task task)
                {
                    AppendHostLog($"ExtensionHost awaiting entry task: pid={Environment.ProcessId}");
                    task.GetAwaiter().GetResult();
                }

                AppendHostLog($"ExtensionHost completed: pid={Environment.ProcessId}, exitCode=0");
                return 0;
            }
            catch (TargetInvocationException ex)
            {
                AppendHostLog($"ExtensionHost target invocation failed: pid={Environment.ProcessId}, error={ex.InnerException ?? ex}");
                Console.Error.WriteLine(ex.InnerException?.ToString() ?? ex.ToString());
                return 1;
            }
            catch (Exception ex)
            {
                AppendHostLog($"ExtensionHost failed: pid={Environment.ProcessId}, error={ex}");
                Console.Error.WriteLine(ex.ToString());
                return 1;
            }
            finally
            {
                AppendHostLog($"ExtensionHost unloading context: pid={Environment.ProcessId}");
                loadContext.Unload();
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            }
        }
        catch (Exception ex)
        {
            AppendHostLog($"ExtensionHost outer failure: pid={Environment.ProcessId}, error={ex}");
            Console.Error.WriteLine(ex.ToString());
            return 1;
        }
    }

    private static void AppendHostLog(string message)
    {
        try
        {
            var logPath = Environment.GetEnvironmentVariable("YANZI_HOST_LOG_PATH");
            if (string.IsNullOrWhiteSpace(logPath))
            {
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
            File.AppendAllText(
                logPath,
                $"{Environment.NewLine}[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}");
        }
        catch
        {
            // The extension host must never fail because diagnostic logging failed.
        }
    }

    private static bool TryGetArgumentValue(string[] args, string key, out string? value)
    {
        for (var index = 0; index < args.Length - 1; index++)
        {
            if (string.Equals(args[index], key, StringComparison.OrdinalIgnoreCase))
            {
                value = args[index + 1];
                return true;
            }
        }

        value = null;
        return false;
    }
}
