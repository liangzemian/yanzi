using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace Yanzi.ExtensionHost;

internal static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static int _serverBusy;
    private static readonly object ServerOutputGate = new();
    private static TextWriter? _serverOutput;

    [STAThread]
    private static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        AppendHostLog($"ExtensionHost start: pid={Environment.ProcessId}, args={string.Join(" ", args)}");

        try
        {
            if (args.Any(static arg => string.Equals(arg, "--server", StringComparison.OrdinalIgnoreCase)))
            {
                return RunServerLoop();
            }

            if (!TryGetArgumentValue(args, "--assembly", out var assemblyPath) || string.IsNullOrWhiteSpace(assemblyPath))
            {
                AppendHostLog("ExtensionHost failed: missing --assembly.");
                Console.Error.WriteLine("缺少 --assembly 参数。");
                return 2;
            }

            var result = ExecuteSingleRun(assemblyPath);
            if (!string.IsNullOrEmpty(result.Output))
            {
                Console.Out.Write(result.Output);
            }

            if (!string.IsNullOrWhiteSpace(result.Error))
            {
                Console.Error.Write(result.Error);
            }

            return result.ExitCode;
        }
        catch (Exception ex)
        {
            AppendHostLog($"ExtensionHost outer failure: pid={Environment.ProcessId}, error={ex}");
            Console.Error.WriteLine(ex.ToString());
            return 1;
        }
    }

    private static int RunServerLoop()
    {
        _serverOutput = Console.Out;
        AppendHostLog($"ExtensionHost server loop start: pid={Environment.ProcessId}");
        string? line;
        while ((line = Console.ReadLine()) != null)
        {
            ExtensionHostRequest? request;
            try
            {
                request = JsonSerializer.Deserialize<ExtensionHostRequest>(line, JsonOptions);
            }
            catch (Exception ex)
            {
                WriteServerResponse(new ExtensionHostResponse
                {
                    Status = "error",
                    ExitCode = 1,
                    Error = $"请求解析失败：{ex.Message}"
                });
                continue;
            }

            if (string.Equals(request?.Kind, "status", StringComparison.OrdinalIgnoreCase))
            {
                WriteServerResponse(new ExtensionHostResponse
                {
                    Status = Interlocked.CompareExchange(ref _serverBusy, 0, 0) == 0 ? "idle" : "busy",
                    ExitCode = 0
                });
                continue;
            }

            if (request == null || string.IsNullOrWhiteSpace(request.AssemblyPath))
            {
                WriteServerResponse(new ExtensionHostResponse
                {
                    Status = "error",
                    ExitCode = 2,
                    Error = "缺少 assemblyPath。"
                });
                continue;
            }

            if (Interlocked.CompareExchange(ref _serverBusy, 1, 0) != 0)
            {
                WriteServerResponse(new ExtensionHostResponse
                {
                    Status = "busy",
                    ExitCode = 0,
                    Error = "常驻扩展宿主正忙。"
                });
                continue;
            }

            try
            {
                var response = HandleServerRequest(request);
                WriteServerResponse(response);
            }
            catch (Exception ex)
            {
                AppendHostLog($"ExtensionHost server request failed: pid={Environment.ProcessId}, error={ex}");
                Interlocked.Exchange(ref _serverBusy, 0);
                WriteServerResponse(new ExtensionHostResponse
                {
                    Status = "error",
                    ExitCode = 1,
                    Error = ex.ToString()
                });
            }
        }

        AppendHostLog($"ExtensionHost server loop exit: pid={Environment.ProcessId}");
        return 0;
    }

    private static ExtensionHostResponse HandleServerRequest(ExtensionHostRequest request)
    {
        var completionSource = new TaskCompletionSource<ExecutionResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                completionSource.SetResult(ExecuteSingleRun(
                    request.AssemblyPath!,
                    request.ExtensionDirectory,
                    request.Environment));
            }
            catch (Exception ex)
            {
                completionSource.SetResult(new ExecutionResult(1, string.Empty, ex.ToString()));
            }
        })
        {
            IsBackground = true
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        if (!request.AllowEarlySuccess)
        {
            var completed = completionSource.Task.GetAwaiter().GetResult();
            Interlocked.Exchange(ref _serverBusy, 0);
            return new ExtensionHostResponse
            {
                Status = completed.ExitCode == 0 ? "completed" : "error",
                ExitCode = completed.ExitCode,
                Output = completed.Output,
                Error = completed.Error
            };
        }

        var early = WaitForReadyOrExit(request.ReadyPath, completionSource.Task);
        if (early.Success)
        {
            _ = ObserveBackgroundExecutionAsync(completionSource.Task, request.CompletionPath);
            return new ExtensionHostResponse
            {
                Status = "started",
                ExitCode = 0,
                Output = "native-window-started",
                Error = "原生窗口已启动。"
            };
        }

        var failed = completionSource.Task.GetAwaiter().GetResult();
        Interlocked.Exchange(ref _serverBusy, 0);
        return new ExtensionHostResponse
        {
            Status = "error",
            ExitCode = failed.ExitCode,
            Output = failed.Output,
            Error = string.IsNullOrWhiteSpace(failed.Error) ? early.Error : failed.Error
        };
    }

    private static (bool Success, string Error) WaitForReadyOrExit(string? readyPath, Task<ExecutionResult> completionTask)
    {
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(4);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (!string.IsNullOrWhiteSpace(readyPath) && File.Exists(readyPath))
            {
                return (true, string.Empty);
            }

            if (completionTask.IsCompleted)
            {
                var result = completionTask.GetAwaiter().GetResult();
                return (false, string.IsNullOrWhiteSpace(result.Error)
                    ? $"原生窗口未完成运行时初始化就退出，退出码：{result.ExitCode}"
                    : result.Error);
            }

            Thread.Sleep(80);
        }

        return completionTask.IsCompleted
            ? (false, completionTask.GetAwaiter().GetResult().Error)
            : (true, string.Empty);
    }

    private static async Task ObserveBackgroundExecutionAsync(Task<ExecutionResult> completionTask, string? completionPath)
    {
        try
        {
            var result = await completionTask.ConfigureAwait(false);
            AppendHostLog($"ExtensionHost background execution finished: pid={Environment.ProcessId}, exitCode={result.ExitCode}, outputLength={result.Output.Length}, errorLength={result.Error.Length}");
        }
        catch (Exception ex)
        {
            AppendHostLog($"ExtensionHost background execution failed: pid={Environment.ProcessId}, error={ex}");
        }
        finally
        {
            TryWriteCompletionMarker(completionPath);
            Interlocked.Exchange(ref _serverBusy, 0);
        }
    }

    private static void TryWriteCompletionMarker(string? completionPath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(completionPath))
            {
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(completionPath)!);
            File.WriteAllText(completionPath, DateTimeOffset.Now.ToString("O"));
            AppendHostLog($"ExtensionHost completion marker written: pid={Environment.ProcessId}, path={completionPath}");
        }
        catch
        {
        }
    }

    private static void WriteServerResponse(ExtensionHostResponse response)
    {
        var output = _serverOutput ?? Console.Out;
        lock (ServerOutputGate)
        {
            output.WriteLine(JsonSerializer.Serialize(response));
            output.Flush();
        }
    }

    private static ExecutionResult ExecuteSingleRun(
        string assemblyPath,
        string? extensionDirectoryOverride = null,
        IReadOnlyDictionary<string, string>? environmentOverrides = null)
    {
        if (!File.Exists(assemblyPath))
        {
            AppendHostLog($"ExtensionHost failed: assembly not found, path={assemblyPath}");
            return new ExecutionResult(2, string.Empty, $"没有找到扩展程序集：{assemblyPath}");
        }

        var originalDirectory = Directory.GetCurrentDirectory();
        var previousEnvironment = CaptureEnvironmentSnapshot(environmentOverrides);
        var originalOut = Console.Out;
        var originalError = Console.Error;
        using var outputWriter = new StringWriter();
        using var errorWriter = new StringWriter();

        try
        {
            ApplyEnvironmentOverrides(environmentOverrides);
            var extensionDirectory = !string.IsNullOrWhiteSpace(extensionDirectoryOverride)
                ? extensionDirectoryOverride
                : Environment.GetEnvironmentVariable("YANZI_EXTENSION_DIR");
            if (!string.IsNullOrWhiteSpace(extensionDirectory) && Directory.Exists(extensionDirectory))
            {
                Directory.SetCurrentDirectory(extensionDirectory);
            }
            else
            {
                Directory.SetCurrentDirectory(Path.GetDirectoryName(assemblyPath)!);
            }

            Console.SetOut(outputWriter);
            Console.SetError(errorWriter);
            AppendHostLog($"ExtensionHost working directory set: pid={Environment.ProcessId}, cwd={Directory.GetCurrentDirectory()}, assembly={assemblyPath}");

            var loadContext = new AssemblyLoadContext($"yanzi-extension-host-{Guid.NewGuid():N}", isCollectible: true);
            try
            {
                var assembly = loadContext.LoadFromAssemblyPath(assemblyPath);
                var entryPoint = assembly.EntryPoint;
                if (entryPoint == null)
                {
                    AppendHostLog($"ExtensionHost failed: missing entry point, assembly={assemblyPath}");
                    return new ExecutionResult(2, outputWriter.ToString(), "C# 扩展缺少可执行入口。");
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
                return new ExecutionResult(0, outputWriter.ToString(), errorWriter.ToString());
            }
            catch (TargetInvocationException ex)
            {
                AppendHostLog($"ExtensionHost target invocation failed: pid={Environment.ProcessId}, error={ex.InnerException ?? ex}");
                return new ExecutionResult(1, outputWriter.ToString(), ex.InnerException?.ToString() ?? ex.ToString());
            }
            catch (Exception ex)
            {
                AppendHostLog($"ExtensionHost failed: pid={Environment.ProcessId}, error={ex}");
                return new ExecutionResult(1, outputWriter.ToString(), ex.ToString());
            }
            finally
            {
                Console.SetOut(originalOut);
                Console.SetError(originalError);
                AppendHostLog($"ExtensionHost unloading context: pid={Environment.ProcessId}");
                loadContext.Unload();
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            }
        }
        finally
        {
            Directory.SetCurrentDirectory(originalDirectory);
            RestoreEnvironmentSnapshot(previousEnvironment);
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }
    }

    private static Dictionary<string, string?> CaptureEnvironmentSnapshot(IReadOnlyDictionary<string, string>? environmentOverrides)
    {
        var snapshot = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        if (environmentOverrides == null)
        {
            return snapshot;
        }

        foreach (var pair in environmentOverrides)
        {
            snapshot[pair.Key] = Environment.GetEnvironmentVariable(pair.Key);
        }

        return snapshot;
    }

    private static void ApplyEnvironmentOverrides(IReadOnlyDictionary<string, string>? environmentOverrides)
    {
        if (environmentOverrides == null)
        {
            return;
        }

        foreach (var pair in environmentOverrides)
        {
            Environment.SetEnvironmentVariable(pair.Key, pair.Value);
        }
    }

    private static void RestoreEnvironmentSnapshot(IReadOnlyDictionary<string, string?> snapshot)
    {
        foreach (var pair in snapshot)
        {
            Environment.SetEnvironmentVariable(pair.Key, pair.Value);
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

    private sealed record ExecutionResult(int ExitCode, string Output, string Error);

    private sealed class ExtensionHostRequest
    {
        public string? Kind { get; init; }
        public string? AssemblyPath { get; init; }
        public string? ExtensionDirectory { get; init; }
        public Dictionary<string, string>? Environment { get; init; }
        public bool AllowEarlySuccess { get; init; }
        public string? ReadyPath { get; init; }
        public string? CompletionPath { get; init; }
    }

    private sealed class ExtensionHostResponse
    {
        public string Status { get; init; } = "error";
        public int ExitCode { get; init; }
        public string Output { get; init; } = string.Empty;
        public string Error { get; init; } = string.Empty;
    }
}
