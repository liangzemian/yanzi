using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using Basic.Reference.Assemblies;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace OpenQuickHost;

public static class ScriptExtensionRunner
{
    private const string CSharpCacheVersion = "v5";
    private const int MaxExtensionHostWorkerPoolSize = 4;
    private static readonly JsonSerializerOptions ExtensionHostWorkerJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };
    private static readonly object ExtensionHostWorkerPoolGate = new();
    private static readonly List<ExtensionHostWorkerClient> ExtensionHostWorkers = [];

    public static async Task WarmupExtensionHostAsync(CancellationToken cancellationToken = default)
    {
        var worker = await AcquireExtensionHostWorkerAsync(requireNativeWindowSlot: false, cancellationToken);
        if (worker == null)
        {
            HostAssets.AppendLog("ScriptRunner warmup skipped: worker host unavailable.");
            return;
        }

        worker.RequestLock.Release();
        HostAssets.AppendLog($"ScriptRunner worker host warmed up: pid={worker.ProcessId}");
    }

    public static async Task<ScriptExecutionResult> PreparePortableAssetsAsync(
        CommandItem command,
        CancellationToken cancellationToken = default)
    {
        if (!string.Equals(command.Runtime, "csharp", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(command.Runtime, "cs", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(command.Runtime, "c#", StringComparison.OrdinalIgnoreCase))
        {
            return new ScriptExecutionResult(true, string.Empty, string.Empty, 0);
        }

        var isInline = string.Equals(command.EntryMode, "inline", StringComparison.OrdinalIgnoreCase);
        var source = isInline
            ? command.InlineScriptSource
            : await ReadEntrySourceAsync(command, cancellationToken);
        if (string.IsNullOrWhiteSpace(source))
        {
            return new ScriptExecutionResult(false, string.Empty, "C# 扩展缺少源码入口。", -1);
        }

        return await EnsureCSharpBuildAsync(command, source, ShouldUseNativeWindowMode(command, source), cancellationToken);
    }

    public static bool CanExecute(CommandItem command)
    {
        if (string.IsNullOrWhiteSpace(command.Runtime) || string.IsNullOrWhiteSpace(command.ExtensionDirectoryPath))
        {
            return false;
        }

        if (string.Equals(command.EntryMode, "inline", StringComparison.OrdinalIgnoreCase))
        {
            return !string.IsNullOrWhiteSpace(command.InlineScriptSource);
        }

        return !string.IsNullOrWhiteSpace(command.EntryPoint);
    }

    public static async Task<ScriptExecutionResult> ExecuteAsync(
        CommandItem command,
        string? inputText,
        string launchSource,
        CancellationToken cancellationToken = default)
    {
        return await ExecuteAsync(command, inputText, launchSource, null, cancellationToken);
    }

    public static async Task<ScriptExecutionResult> ExecuteAsync(
        CommandItem command,
        string? inputText,
        string launchSource,
        IReadOnlyDictionary<string, string>? state,
        CancellationToken cancellationToken = default)
    {
        var executionStopwatch = Stopwatch.StartNew();
        if (!CanExecute(command))
        {
            return new ScriptExecutionResult(false, string.Empty, "扩展没有可执行脚本入口。", -1);
        }

        HostAssets.AppendLog(
            $"ScriptRunner execute start: id={command.ExtensionId}, title={command.Title}, runtime={command.Runtime}, uiMode={command.UiMode ?? "none"}, launchSource={launchSource}, inputLength={(inputText ?? string.Empty).Length}");

        var isInline = string.Equals(command.EntryMode, "inline", StringComparison.OrdinalIgnoreCase);
        var result = command.Runtime?.ToLowerInvariant() switch
        {
            "powershell" or "ps1" => await ExecutePowerShellEntryAsync(command, inputText, launchSource, state, isInline, cancellationToken),

            "csharp" or "cs" or "c#" => await ExecuteCSharpEntryAsync(command, inputText, launchSource, state, isInline, cancellationToken),

            _ => new ScriptExecutionResult(false, string.Empty, $"当前还不支持脚本运行时：{command.Runtime}", -1)
        };

        HostAssets.AppendLog(
            $"ScriptRunner execute done: id={command.ExtensionId}, title={command.Title}, success={result.Success}, exitCode={result.ExitCode}, elapsedMs={executionStopwatch.ElapsedMilliseconds}, outputLength={result.Output.Length}, errorLength={result.Error.Length}");
        return result;
    }

    private static async Task<ScriptExecutionResult> ExecutePowerShellEntryAsync(
        CommandItem command,
        string? inputText,
        string launchSource,
        IReadOnlyDictionary<string, string>? state,
        bool isInline,
        CancellationToken cancellationToken)
    {
        var entryPath = isInline
            ? await MaterializeInlineScriptAsync(command, ".ps1", cancellationToken)
            : Path.Combine(command.ExtensionDirectoryPath!, command.EntryPoint!);
        if (!File.Exists(entryPath))
        {
            return new ScriptExecutionResult(false, string.Empty, $"没有找到脚本入口：{entryPath}", -1);
        }

        try
        {
            return await ExecutePowerShellAsync(command, entryPath, inputText, launchSource, state, cancellationToken);
        }
        finally
        {
            if (isInline)
            {
                TryDeleteTempFile(entryPath);
            }
        }
    }

    private static async Task<ScriptExecutionResult> ExecuteCSharpEntryAsync(
        CommandItem command,
        string? inputText,
        string launchSource,
        IReadOnlyDictionary<string, string>? state,
        bool isInline,
        CancellationToken cancellationToken)
    {
        var source = isInline
            ? command.InlineScriptSource
            : await ReadEntrySourceAsync(command, cancellationToken);
        return string.IsNullOrWhiteSpace(source)
            ? new ScriptExecutionResult(false, string.Empty, "C# 扩展缺少源码入口。", -1)
            : await ExecuteCSharpAsync(command, source, inputText, launchSource, state, cancellationToken);
    }

    private static async Task<string> MaterializeInlineScriptAsync(CommandItem command, string extension, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.ExtensionDirectoryPath))
        {
            throw new InvalidOperationException("内联脚本缺少扩展目录。");
        }

        if (string.IsNullOrWhiteSpace(command.InlineScriptSource))
        {
            throw new InvalidOperationException("内联脚本缺少 script.source。");
        }

        Directory.CreateDirectory(command.ExtensionDirectoryPath);
        var tempScriptPath = Path.Combine(command.ExtensionDirectoryPath, $".yanzi-inline-{Guid.NewGuid():N}{extension}");
        await File.WriteAllTextAsync(
            tempScriptPath,
            command.InlineScriptSource,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: true),
            cancellationToken);
        return tempScriptPath;
    }

    private static async Task<ScriptExecutionResult> ExecutePowerShellAsync(
        CommandItem command,
        string entryPath,
        string? inputText,
        string launchSource,
        IReadOnlyDictionary<string, string>? state,
        CancellationToken cancellationToken)
    {
        var context = CreateContext(command, inputText, launchSource, state);
        var contextPath = Path.Combine(Path.GetTempPath(), $"yanzi-{command.ExtensionId}-{Guid.NewGuid():N}.json");
        var stateUpdatePath = Path.Combine(Path.GetTempPath(), $"yanzi-{command.ExtensionId}-{Guid.NewGuid():N}-state.json");
        var wrapperPath = Path.Combine(Path.GetTempPath(), $"yanzi-{command.ExtensionId}-{Guid.NewGuid():N}-wrapper.ps1");

        try
        {
            await File.WriteAllTextAsync(
                contextPath,
                JsonSerializer.Serialize(context, JsonOptions),
                Encoding.UTF8,
                cancellationToken);
            await File.WriteAllTextAsync(
                wrapperPath,
                BuildPowerShellWrapperScript(entryPath, inputText ?? string.Empty, contextPath),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: true),
                cancellationToken);

            var startInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -File {Quote(wrapperPath)}",
                WorkingDirectory = command.ExtensionDirectoryPath!,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            ApplyRuntimeEnvironment(startInfo, command, inputText, contextPath, stateUpdatePath, null, launchSource);

            return await RunProcessAsync(startInfo, "脚本", stateUpdatePath, null, cancellationToken);
        }
        catch (Exception ex)
        {
            return new ScriptExecutionResult(false, string.Empty, ex.Message, -1);
        }
        finally
        {
            TryDeleteTempFile(contextPath);
            TryDeleteTempFile(stateUpdatePath);
            TryDeleteTempFile(wrapperPath);
        }
    }

    private static async Task<ScriptExecutionResult> ExecuteCSharpAsync(
        CommandItem command,
        string source,
        string? inputText,
        string launchSource,
        IReadOnlyDictionary<string, string>? state,
        CancellationToken cancellationToken)
    {
        var compileStopwatch = Stopwatch.StartNew();
        var useNativeWindowMode = ShouldUseNativeWindowMode(command, source);
        var context = CreateContext(command, inputText, launchSource, state);
        var contextPath = Path.Combine(Path.GetTempPath(), $"yanzi-{command.ExtensionId}-{Guid.NewGuid():N}.json");
        var stateUpdatePath = Path.Combine(Path.GetTempPath(), $"yanzi-{command.ExtensionId}-{Guid.NewGuid():N}-state.json");
        var readyPath = useNativeWindowMode
            ? Path.Combine(Path.GetTempPath(), $"yanzi-{command.ExtensionId}-{Guid.NewGuid():N}-ready.txt")
            : null;

        try
        {
            await File.WriteAllTextAsync(
                contextPath,
                JsonSerializer.Serialize(context, JsonOptions),
                Encoding.UTF8,
                cancellationToken);

            var build = await EnsureCSharpBuildAsync(command, source, useNativeWindowMode, cancellationToken);
            HostAssets.AppendLog(
                $"ScriptRunner csharp build done: id={command.ExtensionId}, title={command.Title}, success={build.Success}, nativeWindowMode={useNativeWindowMode}, elapsedMs={compileStopwatch.ElapsedMilliseconds}, output={build.Output.Trim()}");
            if (!build.Success)
            {
                return build;
            }
            var assemblyPath = build.Output.Trim();
            if (File.Exists(assemblyPath))
            {
                return await ExecuteManagedAssemblyAsync(
                    command,
                    assemblyPath,
                    contextPath,
                    stateUpdatePath,
                    readyPath,
                    useNativeWindowMode,
                    launchSource,
                    cancellationToken);
            }

            return new ScriptExecutionResult(false, string.Empty, $"没有找到已编译的 C# 扩展输出：{assemblyPath}", -1);
        }
        catch (Exception ex)
        {
            return new ScriptExecutionResult(false, string.Empty, ex.Message, -1);
        }
        finally
        {
            TryDeleteTempFile(contextPath);
            TryDeleteTempFile(stateUpdatePath);
            if (!useNativeWindowMode)
            {
                TryDeleteTempFile(readyPath ?? string.Empty);
            }
        }
    }

    private static async Task<ScriptExecutionResult> EnsureCSharpBuildAsync(
        CommandItem command,
        string source,
        bool useNativeWindowMode,
        CancellationToken cancellationToken)
    {
        var cacheFingerprint = string.Join(
            "\n---\n",
            CSharpCacheVersion,
            command.ExtensionId ?? string.Empty,
            source,
            CSharpProgramSource,
            CSharpRuntimeSource);
        var sourceHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(cacheFingerprint)))[..16].ToLowerInvariant();
        var buildRoot = Path.Combine(command.ExtensionDirectoryPath!, ".yanzi-csharp-cache", sourceHash);
        var dllPath = Path.Combine(buildRoot, "bin", "Release", "net9.0", "YanziExtension.dll");
        if (File.Exists(dllPath))
        {
            return new ScriptExecutionResult(true, dllPath, string.Empty, 0);
        }

        Directory.CreateDirectory(buildRoot);
        var outputDirectory = Path.GetDirectoryName(dllPath)!;
        Directory.CreateDirectory(outputDirectory);
        await File.WriteAllTextAsync(Path.Combine(buildRoot, "Action.cs"), source, Encoding.UTF8, cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(buildRoot, "Program.cs"), CSharpProgramSource, Encoding.UTF8, cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(buildRoot, "YanziRuntime.cs"), CSharpRuntimeSource, Encoding.UTF8, cancellationToken);
        var syntaxTrees = new[]
        {
            CSharpSyntaxTree.ParseText(SourceText.From(source, Encoding.UTF8), new CSharpParseOptions(LanguageVersion.Latest), path: "Action.cs", cancellationToken: cancellationToken),
            CSharpSyntaxTree.ParseText(SourceText.From(CSharpProgramSource, Encoding.UTF8), new CSharpParseOptions(LanguageVersion.Latest), path: "Program.cs", cancellationToken: cancellationToken),
            CSharpSyntaxTree.ParseText(SourceText.From(CSharpRuntimeSource, Encoding.UTF8), new CSharpParseOptions(LanguageVersion.Latest), path: "YanziRuntime.cs", cancellationToken: cancellationToken)
        };

        var compilation = CSharpCompilation.Create(
            assemblyName: "YanziExtension",
            syntaxTrees: syntaxTrees,
            references: BuildCSharpMetadataReferences(),
            options: new CSharpCompilationOptions(
                OutputKind.ConsoleApplication,
                optimizationLevel: OptimizationLevel.Release,
                nullableContextOptions: NullableContextOptions.Enable));

        await using var peStream = new FileStream(dllPath, FileMode.Create, FileAccess.Write, FileShare.Read);
        await using var pdbStream = new FileStream(
            Path.Combine(outputDirectory, "YanziExtension.pdb"),
            FileMode.Create,
            FileAccess.Write,
            FileShare.Read);
        var emitResult = compilation.Emit(peStream, pdbStream: pdbStream, cancellationToken: cancellationToken);
        if (emitResult.Success && File.Exists(dllPath))
        {
            return new ScriptExecutionResult(true, dllPath, string.Empty, 0);
        }

        var diagnostics = emitResult.Diagnostics
            .Where(static diagnostic => diagnostic.Severity >= DiagnosticSeverity.Warning)
            .Select(static diagnostic => diagnostic.ToString())
            .ToArray();
        var error = diagnostics.Length == 0
            ? "C# 扩展编译失败。"
            : string.Join(Environment.NewLine, diagnostics);
        if (useNativeWindowMode)
        {
            error = BuildNativeWindowReferenceDebugInfo() + Environment.NewLine + error;
        }
        return new ScriptExecutionResult(false, string.Empty, error, -1);
    }

    private static IReadOnlyList<MetadataReference> BuildCSharpMetadataReferences()
    {
        var references = new List<MetadataReference>(
            global::Basic.Reference.Assemblies.Net90.ReferenceInfos.All
                .Select(static info => (MetadataReference)info.Reference));

        var bundledDirectory = GetBundledNativeWindowReferenceDirectory();
        if (!string.IsNullOrWhiteSpace(bundledDirectory) && Directory.Exists(bundledDirectory))
        {
            references.AddRange(Directory.EnumerateFiles(bundledDirectory, "*.dll", SearchOption.TopDirectoryOnly)
                .Select(static path => (MetadataReference)MetadataReference.CreateFromFile(path)));
        }

        var referenceDirectories = new[]
        {
            GetWindowsDesktopReferenceDirectory()
        }
        .Where(path => !string.IsNullOrWhiteSpace(path))
        .Cast<string>()
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

        if (referenceDirectories.Length > 0)
        {
            references.AddRange(referenceDirectories
                .SelectMany(static directory => Directory.EnumerateFiles(directory, "*.dll", SearchOption.TopDirectoryOnly))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(static path => (MetadataReference)MetadataReference.CreateFromFile(path))
                .ToArray());
        }

        var runtimeReferences = BuildNativeWindowRuntimeReferences();
        if (runtimeReferences.Count > 0)
        {
            references.AddRange(runtimeReferences);
        }

        return references
            .GroupBy(static reference => reference.Display, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .ToArray();
    }

    private static string GetBundledNativeWindowReferenceDirectory()
    {
        return Path.Combine(AppContext.BaseDirectory, "NativeWindowRefs");
    }

    private static string? GetWindowsDesktopReferenceDirectory()
    {
        return GetReferencePackDirectory("Microsoft.WindowsDesktop.App.Ref", "net9.0", "net8.0", "net6.0");
    }

    private static string? GetNetCoreReferenceDirectory()
    {
        return GetReferencePackDirectory("Microsoft.NETCore.App.Ref", "net9.0", "net8.0", "net6.0");
    }

    private static string? GetReferencePackDirectory(string packName, params string[] tfmCandidates)
    {
        var packsRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet", "packs", packName);
        if (!Directory.Exists(packsRoot))
        {
            return null;
        }

        var candidate = Directory.EnumerateDirectories(packsRoot)
            .Select(path => new DirectoryInfo(path))
            .Where(info => Version.TryParse(info.Name, out var version) && version.Major == 9)
            .OrderByDescending(info => Version.Parse(info.Name))
            .SelectMany(info => tfmCandidates.Select(tfm => Path.Combine(info.FullName, "ref", tfm)))
            .FirstOrDefault(Directory.Exists);

        if (candidate != null)
        {
            return candidate;
        }

        return Directory.EnumerateDirectories(packsRoot)
            .Select(path => new DirectoryInfo(path))
            .OrderByDescending(info =>
            {
                return Version.TryParse(info.Name, out var parsed) ? parsed : new Version(0, 0);
            })
            .SelectMany(info => tfmCandidates.Select(tfm => Path.Combine(info.FullName, "ref", tfm)))
            .FirstOrDefault(Directory.Exists);
    }

    private static string? GetSharedRuntimeDirectory(string sharedName)
    {
        var sharedRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet", "shared", sharedName);
        if (!Directory.Exists(sharedRoot))
        {
            return null;
        }

        return Directory.EnumerateDirectories(sharedRoot)
            .Select(path => new DirectoryInfo(path))
            .OrderByDescending(info =>
            {
                return Version.TryParse(info.Name, out var parsed) ? parsed : new Version(0, 0);
            })
            .Select(static info => info.FullName)
            .FirstOrDefault(Directory.Exists);
    }

    private static IReadOnlyList<MetadataReference> BuildNativeWindowRuntimeReferences()
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        static void AddAssemblyPath(HashSet<string> set, Assembly? assembly)
        {
            if (assembly == null || assembly.IsDynamic)
            {
                return;
            }

            var location = assembly.Location;
            if (!string.IsNullOrWhiteSpace(location) && File.Exists(location))
            {
                set.Add(location);
            }
        }

        static void AddCandidateFile(HashSet<string> set, string? directory, string fileName)
        {
            if (string.IsNullOrWhiteSpace(directory))
            {
                return;
            }

            var fullPath = Path.Combine(directory, fileName);
            if (File.Exists(fullPath))
            {
                set.Add(fullPath);
            }
        }

        static void AddTrustedPlatformAssembly(HashSet<string> set, string? tpaValue, string fileName)
        {
            if (string.IsNullOrWhiteSpace(tpaValue))
            {
                return;
            }

            var match = tpaValue
                .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault(path => string.Equals(Path.GetFileName(path), fileName, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(match) && File.Exists(match))
            {
                set.Add(match);
            }
        }

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            AddAssemblyPath(paths, assembly);
        }

        var knownAssemblies = new[]
        {
            typeof(object).Assembly,
            typeof(Task).Assembly,
            typeof(Enumerable).Assembly,
            typeof(Uri).Assembly,
            typeof(System.Windows.Window).Assembly,
            typeof(System.Windows.Controls.Button).Assembly,
            typeof(System.Windows.Media.Brush).Assembly,
            typeof(System.Windows.Markup.XmlLanguage).Assembly
        };

        foreach (var assembly in knownAssemblies)
        {
            AddAssemblyPath(paths, assembly);
        }

        var runtimeDirectory = Path.GetDirectoryName(typeof(object).Assembly.Location);
        var coreSharedDirectory = GetSharedRuntimeDirectory("Microsoft.NETCore.App");
        var windowsDesktopSharedDirectory = GetSharedRuntimeDirectory("Microsoft.WindowsDesktop.App");
        var appDirectory = AppContext.BaseDirectory;
        var trustedPlatformAssemblies = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;

        foreach (var fileName in new[]
                 {
                     "System.Private.CoreLib.dll",
                     "System.Runtime.dll",
                     "System.Console.dll",
                     "System.Collections.dll",
                     "System.ObjectModel.dll",
                     "System.Linq.dll",
                     "System.Runtime.Extensions.dll",
                     "System.Text.RegularExpressions.dll",
                     "System.Threading.dll",
                     "System.Threading.Tasks.dll",
                     "netstandard.dll",
                     "WindowsBase.dll",
                     "PresentationCore.dll",
                     "PresentationFramework.dll",
                     "System.Xaml.dll"
                 })
        {
            AddTrustedPlatformAssembly(paths, trustedPlatformAssemblies, fileName);
        }

        foreach (var directory in new[] { runtimeDirectory, coreSharedDirectory, windowsDesktopSharedDirectory, appDirectory }.Where(static path => !string.IsNullOrWhiteSpace(path)))
        {
            AddCandidateFile(paths, directory, "System.Private.CoreLib.dll");
            AddCandidateFile(paths, directory, "System.Runtime.dll");
            AddCandidateFile(paths, directory, "System.Console.dll");
            AddCandidateFile(paths, directory, "System.Collections.dll");
            AddCandidateFile(paths, directory, "System.ObjectModel.dll");
            AddCandidateFile(paths, directory, "System.Linq.dll");
            AddCandidateFile(paths, directory, "System.Runtime.Extensions.dll");
            AddCandidateFile(paths, directory, "System.Text.RegularExpressions.dll");
            AddCandidateFile(paths, directory, "System.Threading.dll");
            AddCandidateFile(paths, directory, "System.Threading.Tasks.dll");
            AddCandidateFile(paths, directory, "netstandard.dll");
            AddCandidateFile(paths, directory, "WindowsBase.dll");
            AddCandidateFile(paths, directory, "PresentationCore.dll");
            AddCandidateFile(paths, directory, "PresentationFramework.dll");
            AddCandidateFile(paths, directory, "System.Xaml.dll");
        }

        return paths
            .Select(static path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .ToArray();
    }

    private static string BuildNativeWindowReferenceDebugInfo()
    {
        var bundledDirectory = GetBundledNativeWindowReferenceDirectory();
        var sharedCore = GetSharedRuntimeDirectory("Microsoft.NETCore.App") ?? "(missing)";
        var sharedDesktop = GetSharedRuntimeDirectory("Microsoft.WindowsDesktop.App") ?? "(missing)";
        var packCore = GetNetCoreReferenceDirectory() ?? "(missing)";
        var packDesktop = GetWindowsDesktopReferenceDirectory() ?? "(missing)";
        var tpaValue = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
        var tpaFiles = new[]
        {
            "PresentationFramework.dll",
            "PresentationCore.dll",
            "WindowsBase.dll",
            "System.Xaml.dll"
        }
        .Select(fileName =>
        {
            var match = string.IsNullOrWhiteSpace(tpaValue)
                ? null
                : tpaValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
                    .FirstOrDefault(path => string.Equals(Path.GetFileName(path), fileName, StringComparison.OrdinalIgnoreCase));
            return $"{fileName}={(string.IsNullOrWhiteSpace(match) ? "(missing)" : match)}";
        });

        return string.Join(
            Environment.NewLine,
            [
                $"NativeWindow refs: bundled={(Directory.Exists(bundledDirectory) ? bundledDirectory : "(missing)")}",
                $"NativeWindow refs: corePack={packCore}",
                $"NativeWindow refs: desktopPack={packDesktop}",
                $"NativeWindow refs: coreShared={sharedCore}",
                $"NativeWindow refs: desktopShared={sharedDesktop}",
                .. tpaFiles.Select(static line => $"NativeWindow refs: {line}")
            ]);
    }

    private static async Task<ScriptExecutionResult> ExecuteManagedAssemblyAsync(
        CommandItem command,
        string assemblyPath,
        string contextPath,
        string stateUpdatePath,
        string? readyPath,
        bool useNativeWindowMode,
        string launchSource,
        CancellationToken cancellationToken)
    {
        if (useNativeWindowMode)
        {
            return await ExecuteManagedAssemblyOutOfProcessAsync(
                command,
                assemblyPath,
                contextPath,
                stateUpdatePath,
                readyPath,
                useNativeWindowMode,
                launchSource,
                cancellationToken);
        }

        var workerResult = await TryExecuteManagedAssemblyViaWorkerAsync(
            command,
            assemblyPath,
            contextPath,
            stateUpdatePath,
            readyPath,
            useNativeWindowMode,
            launchSource,
            cancellationToken);
        if (workerResult != null)
        {
            return workerResult;
        }

        return await ExecuteManagedAssemblyOutOfProcessAsync(
            command,
            assemblyPath,
            contextPath,
            stateUpdatePath,
            readyPath,
            useNativeWindowMode,
            launchSource,
            cancellationToken);
    }

    private static async Task<ScriptExecutionResult?> TryExecuteManagedAssemblyViaWorkerAsync(
        CommandItem command,
        string assemblyPath,
        string contextPath,
        string stateUpdatePath,
        string? readyPath,
        bool useNativeWindowMode,
        string launchSource,
        CancellationToken cancellationToken)
    {
        var client = await AcquireExtensionHostWorkerAsync(useNativeWindowMode, cancellationToken);
        if (client == null)
        {
            HostAssets.AppendLog("ScriptRunner worker pool unavailable or exhausted; falling back to transient host.");
            return null;
        }

        try
        {
            var completionPath = useNativeWindowMode
                ? Path.Combine(Path.GetTempPath(), $"yanzi-{command.ExtensionId}-{Guid.NewGuid():N}-worker-done.txt")
                : null;
            var request = new ExtensionHostWorkerRequest
            {
                AssemblyPath = assemblyPath,
                ExtensionDirectory = command.ExtensionDirectoryPath!,
                Environment = BuildRuntimeEnvironmentMap(command, null, contextPath, stateUpdatePath, readyPath, launchSource),
                AllowEarlySuccess = useNativeWindowMode,
                ReadyPath = readyPath,
                CompletionPath = completionPath
            };

            await client.Input.WriteLineAsync(JsonSerializer.Serialize(request));
            await client.Input.FlushAsync();

            var responseLine = await client.Output.ReadLineAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(responseLine))
            {
                HostAssets.AppendLog("ScriptRunner worker host returned empty response, falling back to transient host.");
                ResetExtensionHostWorker(client);
                return null;
            }

            var response = JsonSerializer.Deserialize<ExtensionHostWorkerResponse>(responseLine, ExtensionHostWorkerJsonOptions);
            if (response == null)
            {
                HostAssets.AppendLog("ScriptRunner worker host response parse failed, falling back to transient host.");
                ResetExtensionHostWorker(client);
                return null;
            }

            if (string.Equals(response.Status, "busy", StringComparison.OrdinalIgnoreCase))
            {
                HostAssets.AppendLog($"ScriptRunner worker host busy: pid={client.ProcessId}, falling back to transient host.");
                return null;
            }

            var stateUpdates = await TryReadStateUpdatesAsync(stateUpdatePath, cancellationToken);
            if (string.Equals(response.Status, "started", StringComparison.OrdinalIgnoreCase))
            {
                client.NativeWindowActive = true;
                MonitorWorkerNativeWindowCompletion(client, completionPath);
                return new ScriptExecutionResult(true, "native-window-started", "原生窗口已启动。", 0, stateUpdates);
            }

            if (string.Equals(response.Status, "completed", StringComparison.OrdinalIgnoreCase))
            {
                return new ScriptExecutionResult(
                    true,
                    response.Output?.Trim() ?? string.Empty,
                    response.Error?.Trim() ?? string.Empty,
                    response.ExitCode,
                    stateUpdates);
            }

            return new ScriptExecutionResult(
                false,
                response.Output?.Trim() ?? string.Empty,
                string.IsNullOrWhiteSpace(response.Error) ? $"C# 扩展宿主退出码：{response.ExitCode}" : response.Error.Trim(),
                response.ExitCode,
                stateUpdates);
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"ScriptRunner worker host failed, falling back to transient host: {ex.Message}");
            ResetExtensionHostWorker(client);
            return null;
        }
        finally
        {
            client.RequestLock.Release();
        }
    }

    private static async Task<ScriptExecutionResult> ExecuteManagedAssemblyOutOfProcessAsync(
        CommandItem command,
        string assemblyPath,
        string contextPath,
        string stateUpdatePath,
        string? readyPath,
        bool useNativeWindowMode,
        string launchSource,
        CancellationToken cancellationToken)
    {
        try
        {
            var hostPath = GetExtensionHostProcessPath();
            if (string.IsNullOrWhiteSpace(hostPath) || !File.Exists(hostPath))
            {
                return new ScriptExecutionResult(false, string.Empty, "没有找到扩展宿主进程。", -1);
            }

            var isDetachedNativeWindow = useNativeWindowMode;
            var startInfo = new ProcessStartInfo
            {
                FileName = hostPath,
                WorkingDirectory = command.ExtensionDirectoryPath!,
                UseShellExecute = false,
                RedirectStandardOutput = !isDetachedNativeWindow,
                RedirectStandardError = !isDetachedNativeWindow,
                CreateNoWindow = true
            };
            if (!isDetachedNativeWindow)
            {
                startInfo.StandardOutputEncoding = Encoding.UTF8;
                startInfo.StandardErrorEncoding = Encoding.UTF8;
            }

            startInfo.ArgumentList.Add("--assembly");
            startInfo.ArgumentList.Add(assemblyPath);
            ApplyRuntimeEnvironment(startInfo, command, null, contextPath, stateUpdatePath, readyPath, launchSource);
            return await RunProcessAsync(
                startInfo,
                "C# 扩展宿主",
                stateUpdatePath,
                readyPath,
                cancellationToken,
                allowEarlySuccess: isDetachedNativeWindow,
                trackedCommand: isDetachedNativeWindow ? command : null,
                trackedLaunchSource: isDetachedNativeWindow ? launchSource : null);
        }
        catch (Exception ex)
        {
            return new ScriptExecutionResult(false, string.Empty, ex.Message, -1);
        }
    }

    private static string? GetExtensionHostProcessPath()
    {
        var candidate = Path.Combine(AppContext.BaseDirectory, "ExtensionHost", "Yanzi.ExtensionHost.exe");
        if (File.Exists(candidate))
        {
            return candidate;
        }

        return null;
    }

    private static async Task<string?> ReadEntrySourceAsync(CommandItem command, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.EntryPoint) || string.IsNullOrWhiteSpace(command.ExtensionDirectoryPath))
        {
            return null;
        }

        var entryPath = Path.Combine(command.ExtensionDirectoryPath, command.EntryPoint);
        return File.Exists(entryPath)
            ? await File.ReadAllTextAsync(entryPath, Encoding.UTF8, cancellationToken)
            : null;
    }

    private static async Task<ScriptExecutionResult> RunProcessAsync(
        ProcessStartInfo startInfo,
        string label,
        string? stateUpdatePath,
        string? readyPath,
        CancellationToken cancellationToken,
        bool allowEarlySuccess = false,
        CommandItem? trackedCommand = null,
        string? trackedLaunchSource = null)
    {
        var processStopwatch = Stopwatch.StartNew();
        var process = new Process { StartInfo = startInfo };
        process.Start();
        var argumentText = startInfo.ArgumentList.Count > 0
            ? string.Join(" ", startInfo.ArgumentList)
            : startInfo.Arguments;
        HostAssets.AppendLog(
            $"ScriptRunner process started: label={label}, file={startInfo.FileName}, args={argumentText}, pid={process.Id}, allowEarlySuccess={allowEarlySuccess}, workingDir={startInfo.WorkingDirectory}");
        Task<string>? outputTask = startInfo.RedirectStandardOutput
            ? process.StandardOutput.ReadToEndAsync(cancellationToken)
            : null;
        Task<string>? errorTask = startInfo.RedirectStandardError
            ? process.StandardError.ReadToEndAsync(cancellationToken)
            : null;

        if (allowEarlySuccess)
        {
            var earlyResult = await WaitForNativeWindowStartupAsync(process, label, readyPath, cancellationToken);
            if (earlyResult != null)
            {
                if (earlyResult.Success)
                {
                    if (trackedCommand != null)
                    {
                        try
                        {
                            RunningExtensionRegistry.RegisterNativeWindowProcess(
                                trackedCommand,
                                process,
                                trackedLaunchSource ?? "unknown");
                        }
                        catch (Exception ex)
                        {
                            HostAssets.AppendLog(
                                $"ScriptRunner native-window registration skipped: label={label}, pid={TryGetProcessId(process)}, error={ex.Message}");
                        }
                    }

                    _ = ObserveProcessAfterEarlySuccessAsync(process, label, stateUpdatePath, outputTask, errorTask);
                    HostAssets.AppendLog(
                        $"ScriptRunner process early success: label={label}, pid={process.Id}, elapsedMs={processStopwatch.ElapsedMilliseconds}");
                    return earlyResult;
                }

                HostAssets.AppendLog(
                    $"ScriptRunner process early failure: label={label}, pid={TryGetProcessId(process)}, elapsedMs={processStopwatch.ElapsedMilliseconds}, error={earlyResult.Error}");
                process.Dispose();
                return earlyResult;
            }
        }

        await process.WaitForExitAsync(cancellationToken);

        var output = outputTask == null ? string.Empty : (await outputTask).Trim();
        var error = errorTask == null ? string.Empty : (await errorTask).Trim();
        var stateUpdates = await TryReadStateUpdatesAsync(stateUpdatePath, cancellationToken);
        HostAssets.AppendLog(
            $"ScriptRunner process exited: label={label}, pid={process.Id}, exitCode={process.ExitCode}, elapsedMs={processStopwatch.ElapsedMilliseconds}, outputLength={output.Length}, errorLength={error.Length}");
        var result = process.ExitCode == 0
            ? new ScriptExecutionResult(true, output, error, process.ExitCode, stateUpdates)
            : new ScriptExecutionResult(false, output, string.IsNullOrWhiteSpace(error) ? $"{label}退出码：{process.ExitCode}" : error, process.ExitCode, stateUpdates);
        process.Dispose();
        return result;
    }

    private static async Task<ScriptExecutionResult?> WaitForNativeWindowStartupAsync(
        Process process,
        string label,
        string? readyPath,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(4);

        while (DateTimeOffset.UtcNow < deadline)
        {
            if (!string.IsNullOrWhiteSpace(readyPath) && File.Exists(readyPath))
            {
                return new ScriptExecutionResult(
                    true,
                    "native-window-started",
                    "原生窗口已启动。",
                    0);
            }

            if (process.HasExited)
            {
                var exitCode = TryGetProcessExitCode(process);
                return new ScriptExecutionResult(
                    false,
                    string.Empty,
                    string.IsNullOrWhiteSpace(readyPath) || !File.Exists(readyPath)
                        ? $"{label}未完成运行时初始化就退出，退出码：{exitCode}"
                        : $"{label}在原生窗口稳定启动前退出，退出码：{exitCode}",
                    int.TryParse(exitCode, out var parsedExitCode) ? parsedExitCode : -1);
            }

            await Task.Delay(100, cancellationToken);
        }

        return process.HasExited
            ? new ScriptExecutionResult(false, string.Empty, $"{label}在原生窗口稳定启动前退出，退出码：{TryGetProcessExitCode(process)}", process.ExitCode)
            : new ScriptExecutionResult(true, "native-window-started", "原生窗口已启动。", 0);
    }

    private static bool ShouldUseNativeWindowMode(CommandItem command, string source)
    {
        if (command.UsesNativeWindowUi)
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(source))
        {
            return false;
        }

        return source.Contains("new Window", StringComparison.Ordinal) ||
               source.Contains("new System.Windows.Window", StringComparison.Ordinal) ||
               source.Contains("ShowDialog()", StringComparison.Ordinal) ||
               source.Contains(".ShowDialog(", StringComparison.Ordinal) ||
               source.Contains("WindowStartupLocation", StringComparison.Ordinal) ||
               source.Contains("WindowStyle", StringComparison.Ordinal);
    }

    private static async Task ObserveProcessAfterEarlySuccessAsync(
        Process process,
        string label,
        string? stateUpdatePath,
        Task<string>? outputTask,
        Task<string>? errorTask)
    {
        try
        {
            process.EnableRaisingEvents = true;
            await process.WaitForExitAsync();
            var output = outputTask == null ? string.Empty : (await outputTask).Trim();
            var error = errorTask == null ? string.Empty : (await errorTask).Trim();
            var stateUpdates = await TryReadStateUpdatesAsync(stateUpdatePath, CancellationToken.None);
            HostAssets.AppendLog(
                $"ScriptRunner process exited after early success: label={label}, pid={process.Id}, exitCode={process.ExitCode}, elapsedMs={TryGetProcessElapsedMilliseconds(process)}, outputLength={output.Length}, errorLength={error.Length}, stateUpdateCount={stateUpdates.Count}");
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"ScriptRunner process observe failed after early success: label={label}, pid={TryGetProcessId(process)}, error={ex.Message}");
        }
        finally
        {
            process.Dispose();
        }
    }

    private static string TryGetProcessExitCode(Process process)
    {
        try
        {
            return process.ExitCode.ToString();
        }
        catch
        {
            return "unknown";
        }
    }

    private static string TryGetProcessId(Process process)
    {
        try
        {
            return process.Id.ToString();
        }
        catch
        {
            return "unknown";
        }
    }

    private static int TryGetWorkerProcessId(Process process)
    {
        try
        {
            return process.Id;
        }
        catch
        {
            return -1;
        }
    }

    private static string TryGetProcessElapsedMilliseconds(Process process)
    {
        try
        {
            return ((long)(DateTime.Now - process.StartTime).TotalMilliseconds).ToString();
        }
        catch
        {
            return "unknown";
        }
    }

    private static ScriptExecutionContext CreateContext(CommandItem command, string? inputText, string launchSource, IReadOnlyDictionary<string, string>? state)
    {
        var settings = AppSettingsStore.Load();
        var agentApiBaseUrl = settings.EnableAgentApi
            ? $"http://127.0.0.1:{settings.AgentApiPort}"
            : string.Empty;
        return new ScriptExecutionContext(
            command.ExtensionId,
            command.Title,
            command.ExtensionDirectoryPath!,
            ExtensionStorageService.GetExtensionStorageDirectoryPath(command.ExtensionId),
            inputText ?? string.Empty,
            launchSource,
            DateTimeOffset.Now,
            command.Permissions,
            new Dictionary<string, string>(state ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase),
            agentApiBaseUrl,
            settings.AgentApiToken);
    }

    private static void ApplyRuntimeEnvironment(
        CommandItem command,
        string contextPath,
        string stateUpdatePath,
        string launchSource)
    {
        var settings = AppSettingsStore.Load();
        Environment.SetEnvironmentVariable("YANZI_INPUT", string.Empty);
        Environment.SetEnvironmentVariable("YANZI_CONTEXT_PATH", contextPath);
        Environment.SetEnvironmentVariable("YANZI_STATE_UPDATES_PATH", stateUpdatePath);
        Environment.SetEnvironmentVariable("YANZI_EXTENSION_ID", command.ExtensionId);
        Environment.SetEnvironmentVariable("YANZI_EXTENSION_DIR", command.ExtensionDirectoryPath!);
        Environment.SetEnvironmentVariable("YANZI_EXTENSION_DATA_DIR", ExtensionStorageService.GetExtensionStorageDirectoryPath(command.ExtensionId));
        Environment.SetEnvironmentVariable("YANZI_LAUNCH_SOURCE", launchSource);
        Environment.SetEnvironmentVariable("YANZI_AGENT_API_BASE_URL", settings.EnableAgentApi
            ? $"http://127.0.0.1:{settings.AgentApiPort}"
            : string.Empty);
        Environment.SetEnvironmentVariable("YANZI_AGENT_API_TOKEN", settings.AgentApiToken ?? string.Empty);
        Environment.SetEnvironmentVariable("YANZI_HOST_LOG_PATH", HostAssets.HostLogPath);
    }

    private static void ApplyRuntimeEnvironment(
        ProcessStartInfo startInfo,
        CommandItem command,
        string? inputText,
        string contextPath,
        string stateUpdatePath,
        string? readyPath,
        string launchSource)
    {
        var settings = AppSettingsStore.Load();
        startInfo.Environment["YANZI_INPUT"] = inputText ?? string.Empty;
        startInfo.Environment["YANZI_CONTEXT_PATH"] = contextPath;
        startInfo.Environment["YANZI_STATE_UPDATES_PATH"] = stateUpdatePath;
        if (!string.IsNullOrWhiteSpace(readyPath))
        {
            startInfo.Environment["YANZI_READY_PATH"] = readyPath;
        }
        startInfo.Environment["YANZI_EXTENSION_ID"] = command.ExtensionId;
        startInfo.Environment["YANZI_EXTENSION_DIR"] = command.ExtensionDirectoryPath!;
        startInfo.Environment["YANZI_EXTENSION_DATA_DIR"] = ExtensionStorageService.GetExtensionStorageDirectoryPath(command.ExtensionId);
        startInfo.Environment["YANZI_LAUNCH_SOURCE"] = launchSource;
        startInfo.Environment["YANZI_AGENT_API_BASE_URL"] = settings.EnableAgentApi
            ? $"http://127.0.0.1:{settings.AgentApiPort}"
            : string.Empty;
        startInfo.Environment["YANZI_AGENT_API_TOKEN"] = settings.AgentApiToken ?? string.Empty;
        startInfo.Environment["YANZI_HOST_LOG_PATH"] = HostAssets.HostLogPath;
    }

    private static Dictionary<string, string> BuildRuntimeEnvironmentMap(
        CommandItem command,
        string? inputText,
        string contextPath,
        string stateUpdatePath,
        string? readyPath,
        string launchSource)
    {
        var settings = AppSettingsStore.Load();
        var environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["YANZI_INPUT"] = inputText ?? string.Empty,
            ["YANZI_CONTEXT_PATH"] = contextPath,
            ["YANZI_STATE_UPDATES_PATH"] = stateUpdatePath,
            ["YANZI_EXTENSION_ID"] = command.ExtensionId,
            ["YANZI_EXTENSION_DIR"] = command.ExtensionDirectoryPath!,
            ["YANZI_EXTENSION_DATA_DIR"] = ExtensionStorageService.GetExtensionStorageDirectoryPath(command.ExtensionId),
            ["YANZI_LAUNCH_SOURCE"] = launchSource,
            ["YANZI_AGENT_API_BASE_URL"] = settings.EnableAgentApi
                ? $"http://127.0.0.1:{settings.AgentApiPort}"
                : string.Empty,
            ["YANZI_AGENT_API_TOKEN"] = settings.AgentApiToken ?? string.Empty,
            ["YANZI_HOST_LOG_PATH"] = HostAssets.HostLogPath
        };

        if (!string.IsNullOrWhiteSpace(readyPath))
        {
            environment["YANZI_READY_PATH"] = readyPath;
        }

        return environment;
    }

    private static async Task<ExtensionHostWorkerClient?> AcquireExtensionHostWorkerAsync(bool requireNativeWindowSlot, CancellationToken cancellationToken)
    {
        while (true)
        {
            ExtensionHostWorkerClient? acquired = null;
            ExtensionHostWorkerClient? created = null;
            List<ExtensionHostWorkerClient>? activeWorkersNeedingProbe = null;

            lock (ExtensionHostWorkerPoolGate)
            {
                PruneExtensionHostWorkers_NoLock();
                RefreshExtensionHostWorkerCompletion_NoLock();

                foreach (var worker in ExtensionHostWorkers)
                {
                    if (requireNativeWindowSlot && worker.NativeWindowActive)
                    {
                        activeWorkersNeedingProbe ??= [];
                        activeWorkersNeedingProbe.Add(worker);
                        continue;
                    }

                    if (worker.RequestLock.Wait(0))
                    {
                        acquired = worker;
                        break;
                    }
                }

                if (acquired == null && ExtensionHostWorkers.Count < MaxExtensionHostWorkerPoolSize)
                {
                    created = CreateExtensionHostWorker_NoLock();
                    if (created != null)
                    {
                        ExtensionHostWorkers.Add(created);
                        if (created.RequestLock.Wait(0))
                        {
                            acquired = created;
                        }
                    }
                }
            }

            if (acquired != null)
            {
                return acquired;
            }

            if (requireNativeWindowSlot && activeWorkersNeedingProbe is { Count: > 0 })
            {
                foreach (var worker in activeWorkersNeedingProbe)
                {
                    if (!worker.RequestLock.Wait(0))
                    {
                        continue;
                    }

                    var reusable = false;
                    try
                    {
                        reusable = await TryProbeAndReclaimWorkerAsync(worker, cancellationToken);
                        if (reusable)
                        {
                            return worker;
                        }
                    }
                    catch (Exception ex)
                    {
                        HostAssets.AppendLog($"ScriptRunner worker status probe failed: pid={worker.ProcessId}, error={ex.Message}");
                        ResetExtensionHostWorker(worker);
                    }
                    finally
                    {
                        if (!reusable)
                        {
                            worker.RequestLock.Release();
                        }
                    }
                }
            }

            if (!requireNativeWindowSlot)
            {
                return null;
            }

            await Task.Delay(120, cancellationToken);
        }
    }

    private static ExtensionHostWorkerClient? CreateExtensionHostWorker_NoLock()
    {
        var hostPath = GetExtensionHostProcessPath();
        if (string.IsNullOrWhiteSpace(hostPath) || !File.Exists(hostPath))
        {
            return null;
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = hostPath,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        startInfo.ArgumentList.Add("--server");
        startInfo.Environment["YANZI_HOST_LOG_PATH"] = HostAssets.HostLogPath;

        var process = new Process
        {
            StartInfo = startInfo,
            EnableRaisingEvents = true
        };
        process.Start();
        var client = new ExtensionHostWorkerClient(process, process.StandardInput, process.StandardOutput);
        _ = DrainExtensionHostWorkerErrorAsync(process, CancellationToken.None);
        HostAssets.AppendLog($"ScriptRunner worker host started: pid={process.Id}, path={hostPath}, poolSize={ExtensionHostWorkers.Count + 1}/{MaxExtensionHostWorkerPoolSize}");
        return client;
    }

    private static async Task DrainExtensionHostWorkerErrorAsync(Process process, CancellationToken cancellationToken)
    {
        try
        {
            while (!process.HasExited)
            {
                var line = await process.StandardError.ReadLineAsync(cancellationToken);
                if (line == null)
                {
                    break;
                }

                if (!string.IsNullOrWhiteSpace(line))
                {
                    HostAssets.AppendLog($"ExtensionHost worker stderr: {line}");
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"ExtensionHost worker stderr drain failed: {ex.Message}");
        }
    }

    private static void PruneExtensionHostWorkers_NoLock()
    {
        for (var index = ExtensionHostWorkers.Count - 1; index >= 0; index--)
        {
            if (!ExtensionHostWorkers[index].IsAlive)
            {
                DisposeExtensionHostWorker_NoLock(ExtensionHostWorkers[index], removeFromPool: false);
                ExtensionHostWorkers.RemoveAt(index);
            }
        }
    }

    private static void RefreshExtensionHostWorkerCompletion_NoLock()
    {
        foreach (var worker in ExtensionHostWorkers)
        {
            TryMarkWorkerReusableFromCompletion_NoLock(worker, "pool scan");
        }
    }

    private static async Task<bool> TryProbeAndReclaimWorkerAsync(ExtensionHostWorkerClient worker, CancellationToken cancellationToken)
    {
        if (!worker.IsAlive)
        {
            return false;
        }

        lock (ExtensionHostWorkerPoolGate)
        {
            if (TryMarkWorkerReusableFromCompletion_NoLock(worker, "status probe pre-check"))
            {
                return true;
            }
        }

        var request = new ExtensionHostWorkerRequest
        {
            Kind = "status"
        };

        await worker.Input.WriteLineAsync(JsonSerializer.Serialize(request));
        await worker.Input.FlushAsync();

        string? responseLine;
        try
        {
            responseLine = await worker.Output.ReadLineAsync(cancellationToken).AsTask().WaitAsync(TimeSpan.FromSeconds(1.5), cancellationToken);
        }
        catch (TimeoutException)
        {
            HostAssets.AppendLog($"ScriptRunner worker status probe timed out: pid={worker.ProcessId}");
            return false;
        }

        if (string.IsNullOrWhiteSpace(responseLine))
        {
            return false;
        }

        var response = JsonSerializer.Deserialize<ExtensionHostWorkerResponse>(responseLine, ExtensionHostWorkerJsonOptions);
        if (response == null)
        {
            return false;
        }

        if (!string.Equals(response.Status, "idle", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        lock (ExtensionHostWorkerPoolGate)
        {
            MarkWorkerReusable_NoLock(worker, "status probe");
        }

        return true;
    }

    private static void ResetExtensionHostWorker(ExtensionHostWorkerClient worker)
    {
        lock (ExtensionHostWorkerPoolGate)
        {
            if (ExtensionHostWorkers.Remove(worker))
            {
                DisposeExtensionHostWorker_NoLock(worker, removeFromPool: true);
            }
            else
            {
                DisposeExtensionHostWorker_NoLock(worker, removeFromPool: false);
            }
        }
    }

    private static void DisposeExtensionHostWorker_NoLock(ExtensionHostWorkerClient worker, bool removeFromPool)
    {
        worker.NativeWindowActive = false;
        if (!string.IsNullOrWhiteSpace(worker.CompletionPath))
        {
            TryDeleteTempFile(worker.CompletionPath);
            worker.CompletionPath = null;
        }
        try
        {
            worker.Input.Dispose();
        }
        catch
        {
        }

        try
        {
            worker.Output.Dispose();
        }
        catch
        {
        }

        try
        {
            if (worker.Process is { HasExited: false })
            {
                worker.Process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
        }
        finally
        {
            worker.Process.Dispose();
            HostAssets.AppendLog($"ScriptRunner worker host disposed: pid={worker.ProcessId}, removed={removeFromPool}");
        }
    }

    private static void MonitorWorkerNativeWindowCompletion(ExtensionHostWorkerClient worker, string? completionPath)
    {
        if (string.IsNullOrWhiteSpace(completionPath))
        {
            worker.NativeWindowActive = false;
            return;
        }

        worker.CompletionPath = completionPath;

        _ = Task.Run(async () =>
        {
            try
            {
                var deadline = DateTimeOffset.UtcNow + TimeSpan.FromHours(1);
                while (DateTimeOffset.UtcNow < deadline)
                {
                    if (File.Exists(completionPath))
                    {
                        break;
                    }

                    await Task.Delay(200).ConfigureAwait(false);
                }
            }
            catch
            {
            }
            finally
            {
                lock (ExtensionHostWorkerPoolGate)
                {
                    MarkWorkerReusable_NoLock(worker, "completion monitor");
                }
            }
        });
    }

    private static bool TryMarkWorkerReusableFromCompletion_NoLock(ExtensionHostWorkerClient worker, string source)
    {
        if (!worker.NativeWindowActive || string.IsNullOrWhiteSpace(worker.CompletionPath))
        {
            return false;
        }

        if (!File.Exists(worker.CompletionPath))
        {
            return false;
        }

        MarkWorkerReusable_NoLock(worker, source);
        return true;
    }

    private static void MarkWorkerReusable_NoLock(ExtensionHostWorkerClient worker, string source)
    {
        worker.NativeWindowActive = false;
        if (!string.IsNullOrWhiteSpace(worker.CompletionPath))
        {
            TryDeleteTempFile(worker.CompletionPath);
            worker.CompletionPath = null;
        }

        HostAssets.AppendLog($"ScriptRunner worker completion reaped via {source}: pid={worker.ProcessId}; worker can be reused.");
    }

    private static string Quote(string value)
    {
        return "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
    }

    private static string BuildPowerShellWrapperScript(string entryPath, string inputText, string contextPath)
    {
        var escapedEntryPath = EscapePowerShellSingleQuoted(entryPath);
        var escapedInputText = EscapePowerShellSingleQuoted(inputText);
        var escapedContextPath = EscapePowerShellSingleQuoted(contextPath);

        return
            "$utf8 = [System.Text.UTF8Encoding]::new($false)\r\n" +
            "[Console]::InputEncoding = $utf8\r\n" +
            "[Console]::OutputEncoding = $utf8\r\n" +
            "$OutputEncoding = $utf8\r\n" +
            $"& '{escapedEntryPath}' -InputText '{escapedInputText}' -ContextPath '{escapedContextPath}'\r\n";
    }

    private static string EscapePowerShellSingleQuoted(string value)
    {
        return (value ?? string.Empty).Replace("'", "''", StringComparison.Ordinal);
    }

    private static void TryDeleteTempFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Ignore temp cleanup failures.
        }
    }

    private sealed record ScriptExecutionContext(
        string ExtensionId,
        string Title,
        string ExtensionDirectory,
        string ExtensionDataDirectory,
        string InputText,
        string LaunchSource,
        DateTimeOffset Now,
        IReadOnlyList<string> Permissions,
        IReadOnlyDictionary<string, string> State,
        string AgentApiBaseUrl,
        string AgentApiToken);

    private static async Task<IReadOnlyDictionary<string, string>> TryReadStateUpdatesAsync(string? stateUpdatePath, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(stateUpdatePath) || !File.Exists(stateUpdatePath))
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            var json = await File.ReadAllTextAsync(stateUpdatePath, cancellationToken);
            var payload = JsonSerializer.Deserialize<Dictionary<string, string>>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            return payload != null
                ? new Dictionary<string, string>(payload, StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private sealed class ExtensionHostWorkerClient
    {
        public ExtensionHostWorkerClient(Process process, StreamWriter input, StreamReader output)
        {
            Process = process;
            Input = input;
            Output = output;
        }

        public Process Process { get; }
        public StreamWriter Input { get; }
        public StreamReader Output { get; }
        public SemaphoreSlim RequestLock { get; } = new(1, 1);
        public bool NativeWindowActive { get; set; }
        public string? CompletionPath { get; set; }
        public int ProcessId => TryGetWorkerProcessId(Process);
        public bool IsAlive
        {
            get
            {
                try
                {
                    return !Process.HasExited;
                }
                catch
                {
                    return false;
                }
            }
        }
    }

    private sealed class ExtensionHostWorkerRequest
    {
        public string Kind { get; init; } = "execute";
        public string AssemblyPath { get; init; } = string.Empty;
        public string ExtensionDirectory { get; init; } = string.Empty;
        public Dictionary<string, string> Environment { get; init; } = new(StringComparer.OrdinalIgnoreCase);
        public bool AllowEarlySuccess { get; init; }
        public string? ReadyPath { get; init; }
        public string? CompletionPath { get; init; }
    }

    private sealed class ExtensionHostWorkerResponse
    {
        public string Status { get; init; } = string.Empty;
        public int ExitCode { get; init; }
        public string? Output { get; init; }
        public string? Error { get; init; }
    }

    private const string CSharpProgramSource =
        """
        using System;
        using OpenQuickHost.CSharpRuntime;

        var context = await YanziActionContext.LoadFromEnvironmentAsync().ConfigureAwait(false);
        var readyPath = Environment.GetEnvironmentVariable("YANZI_READY_PATH");
        if (!string.IsNullOrWhiteSpace(readyPath))
        {
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(readyPath)!);
            System.IO.File.WriteAllText(readyPath, DateTimeOffset.Now.ToString("O"));
        }
        var result = await YanziAction.RunAsync(context).ConfigureAwait(false);
        if (!string.IsNullOrEmpty(result))
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            Console.WriteLine(result);
        }
        """;

    private const string CSharpRuntimeSource =
        """
        using System;
        using System.Collections.Generic;
        using System.IO;
        using System.Linq;
        using System.Net.Http;
        using System.Net.Http.Json;
        using System.Text;
        using System.Text.Json;
        using System.Threading.Tasks;

        namespace OpenQuickHost.CSharpRuntime;

        public sealed record YanziActionContext(
            string ExtensionId,
            string Title,
            string ExtensionDirectory,
            string ExtensionDataDirectory,
            string InputText,
            string LaunchSource,
            DateTimeOffset Now,
            IReadOnlyList<string> Permissions,
            IReadOnlyDictionary<string, string> State,
            string AgentApiBaseUrl,
            string AgentApiToken)
        {
            private YanziStorageClient? _storage;
            private readonly Dictionary<string, string> _pendingStateUpdates = new(StringComparer.OrdinalIgnoreCase);
            private HostedViewStateProxy? _viewState;

            public YanziStorageClient Storage => _storage ??= new YanziStorageClient(this);
            public HostedViewStateProxy ViewState => _viewState ??= new HostedViewStateProxy(this);

            public async Task SetStateAsync(object values)
            {
                if (values == null)
                {
                    return;
                }

                foreach (var property in values.GetType().GetProperties(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public))
                {
                    _pendingStateUpdates[property.Name] = property.GetValue(values)?.ToString() ?? string.Empty;
                }

                await FlushStateUpdatesAsync();
            }

            public async Task SetStateAsync(IReadOnlyDictionary<string, string> values)
            {
                if (values == null)
                {
                    return;
                }

                foreach (var pair in values)
                {
                    _pendingStateUpdates[pair.Key] = pair.Value ?? string.Empty;
                }

                await FlushStateUpdatesAsync();
            }

            public static async Task<YanziActionContext> LoadFromEnvironmentAsync()
            {
                var contextPath = Environment.GetEnvironmentVariable("YANZI_CONTEXT_PATH");
                if (string.IsNullOrWhiteSpace(contextPath) || !File.Exists(contextPath))
                {
                    throw new InvalidOperationException("YANZI_CONTEXT_PATH is missing.");
                }

                var json = await File.ReadAllTextAsync(contextPath);
                return JsonSerializer.Deserialize<YanziActionContext>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                }) ?? throw new InvalidOperationException("Failed to read Yanzi context.");
            }

            private async Task FlushStateUpdatesAsync()
            {
                var stateUpdatePath = Environment.GetEnvironmentVariable("YANZI_STATE_UPDATES_PATH");
                if (string.IsNullOrWhiteSpace(stateUpdatePath))
                {
                    return;
                }

                await File.WriteAllTextAsync(stateUpdatePath, JsonSerializer.Serialize(_pendingStateUpdates));
            }

            public Task UpdateView()
            {
                return FlushStateUpdatesAsync();
            }

            public sealed class HostedViewStateProxy
            {
                private readonly YanziActionContext _context;

                public HostedViewStateProxy(YanziActionContext context)
                {
                    _context = context;
                }

                public object? this[string key]
                {
                    get
                    {
                        if (_context._pendingStateUpdates.TryGetValue(key, out var pending))
                        {
                            return pending;
                        }

                        return _context.State.TryGetValue(key, out var value) ? value : null;
                    }
                    set
                    {
                        _context._pendingStateUpdates[key] = value?.ToString() ?? string.Empty;
                    }
                }

                public bool TryGetValue(string key, out object? value)
                {
                    if (_context._pendingStateUpdates.TryGetValue(key, out var pending))
                    {
                        value = pending;
                        return true;
                    }

                    if (_context.State.TryGetValue(key, out var existing))
                    {
                        value = existing;
                        return true;
                    }

                    value = null;
                    return false;
                }
            }
        }

        public sealed class YanziStorageClient
        {
            private readonly YanziActionContext _context;

            public YanziStorageClient(YanziActionContext context)
            {
                _context = context;
            }

            public async Task<string?> ReadTextAsync(string key, string scope = "local")
            {
                if (string.Equals(scope, "local", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(_context.AgentApiBaseUrl))
                {
                    var path = ResolveLocalPath(key);
                    return File.Exists(path) ? await File.ReadAllTextAsync(path) : null;
                }

                using var client = CreateClient();
                var response = await client.GetAsync($"/v1/storage/{Uri.EscapeDataString(_context.ExtensionId)}?key={Uri.EscapeDataString(key)}&scope={Uri.EscapeDataString(scope)}");
                response.EnsureSuccessStatusCode();
                var payload = await response.Content.ReadFromJsonAsync<StorageReadResponse>();
                return payload?.Content;
            }

            public async Task WriteTextAsync(string key, string content, string scope = "local")
            {
                if (string.Equals(scope, "local", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(_context.AgentApiBaseUrl))
                {
                    var path = ResolveLocalPath(key);
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    await File.WriteAllTextAsync(path, content ?? string.Empty, Encoding.UTF8);
                    return;
                }

                using var client = CreateClient();
                using var response = await client.PutAsJsonAsync(
                    $"/v1/storage/{Uri.EscapeDataString(_context.ExtensionId)}",
                    new StorageWriteRequest(key, content ?? string.Empty, scope));
                response.EnsureSuccessStatusCode();
            }

            public async Task<T?> ReadJsonAsync<T>(string key, string scope = "local")
            {
                var text = await ReadTextAsync(key, scope);
                return string.IsNullOrWhiteSpace(text) ? default : JsonSerializer.Deserialize<T>(text, SerializerOptions);
            }

            public Task WriteJsonAsync<T>(string key, T value, string scope = "local")
            {
                var json = JsonSerializer.Serialize(value, SerializerOptions);
                return WriteTextAsync(key, json, scope);
            }

            private string ResolveLocalPath(string key)
            {
                var normalized = NormalizeKey(key);
                return Path.Combine(_context.ExtensionDataDirectory, normalized.Replace('/', Path.DirectorySeparatorChar));
            }

            private HttpClient CreateClient()
            {
                var client = new HttpClient
                {
                    BaseAddress = new Uri(_context.AgentApiBaseUrl, UriKind.Absolute)
                };

                if (!string.IsNullOrWhiteSpace(_context.AgentApiToken))
                {
                    client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _context.AgentApiToken);
                }

                return client;
            }

            private static string NormalizeKey(string key)
            {
                var normalized = (key ?? string.Empty).Replace('\\', '/').Trim('/');
                if (string.IsNullOrWhiteSpace(normalized))
                {
                    throw new InvalidOperationException("Storage key is required.");
                }

                var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (segments.Any(static segment => segment is "." or ".."))
                {
                    throw new InvalidOperationException("Storage key cannot contain . or .. segments.");
                }

                return string.Join("/", segments);
            }

            private static readonly JsonSerializerOptions SerializerOptions = new()
            {
                PropertyNameCaseInsensitive = true,
                WriteIndented = true
            };

            private sealed record StorageReadResponse(bool Found, string? Content, string Source, string LocalPath);

            private sealed record StorageWriteRequest(string Key, string Content, string Scope);
        }
        """;
}

public sealed record ScriptExecutionResult(
    bool Success,
    string Output,
    string Error,
    int ExitCode,
    IReadOnlyDictionary<string, string>? StateUpdates = null);
