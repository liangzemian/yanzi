using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace OpenQuickHost.Sync;

public static class ExtensionPackageService
{
    public static string ExtensionsRootPath => HostAssets.ExtensionsPath;

    public static byte[] BuildPackage(CommandItem command, string version, string? iconOverride = null)
    {
        if (!string.IsNullOrWhiteSpace(command.ExtensionDirectoryPath) &&
            Directory.Exists(command.ExtensionDirectoryPath))
        {
            return BuildDirectoryPackage(command.ExtensionDirectoryPath, iconOverride);
        }

        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteJsonEntry(
                archive,
                "manifest.json",
                new
                {
                    id = command.ExtensionId,
                    name = command.Title,
                    version,
                    category = command.Category,
                    description = command.Subtitle,
                    keywords = command.Keywords,
                    source = command.Source.ToString(),
                    icon = string.IsNullOrWhiteSpace(iconOverride) ? command.IconReference : iconOverride
                });

            WriteJsonEntry(
                archive,
                "command.json",
                new
                {
                    command.Title,
                    command.Subtitle,
                    command.Category,
                    command.OpenTarget,
                    command.Keywords
                });
        }

        return stream.ToArray();
    }

    public static async Task<string> SavePackageAsync(string extensionId, string version, byte[] packageBytes, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(ExtensionsRootPath);
        var targetDirectory = Path.Combine(ExtensionsRootPath, extensionId);
        Directory.CreateDirectory(targetDirectory);
        var targetPath = Path.Combine(targetDirectory, $"{version}.zip");
        await File.WriteAllBytesAsync(targetPath, packageBytes, cancellationToken);
        return targetPath;
    }

    private static void WriteJsonEntry(ZipArchive archive, string entryName, object data)
    {
        var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
        writer.Write(JsonSerializer.Serialize(data, JsonOptions));
    }

    private static byte[] BuildDirectoryPackage(string directoryPath, string? iconOverride)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteManifestEntry(archive, directoryPath, iconOverride);
            foreach (var filePath in Directory.EnumerateFiles(directoryPath, "*", SearchOption.AllDirectories)
                         .Where(path => ShouldIncludeInPackage(directoryPath, path)))
            {
                var relativePath = Path.GetRelativePath(directoryPath, filePath);
                if (string.Equals(relativePath, "manifest.json", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                archive.CreateEntryFromFile(filePath, relativePath, CompressionLevel.Optimal);
            }
        }

        return stream.ToArray();
    }

    private static void WriteManifestEntry(ZipArchive archive, string directoryPath, string? iconOverride)
    {
        var manifestPath = Path.Combine(directoryPath, "manifest.json");
        if (!File.Exists(manifestPath))
        {
            throw new FileNotFoundException("扩展目录里缺少 manifest.json。", manifestPath);
        }

        if (string.IsNullOrWhiteSpace(iconOverride))
        {
            archive.CreateEntryFromFile(manifestPath, "manifest.json", CompressionLevel.Optimal);
            return;
        }

        var manifestJson = File.ReadAllText(manifestPath);
        var manifest = JsonSerializer.Deserialize<LocalExtensionManifest>(manifestJson, JsonOptions)
            ?? throw new InvalidOperationException("扩展目录中的 manifest.json 无效。");
        WriteJsonEntry(archive, "manifest.json", manifest with { Icon = iconOverride });
    }

    private static bool ShouldIncludeInPackage(string rootDirectory, string filePath)
    {
        var relativePath = Path.GetRelativePath(rootDirectory, filePath);
        var normalizedRelativePath = relativePath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        var segments = normalizedRelativePath.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);

        if (normalizedRelativePath.StartsWith(".yanzi-csharp-cache" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            return normalizedRelativePath.EndsWith(
                Path.Combine("bin", "Release", "net9.0", "YanziExtension.dll"),
                StringComparison.OrdinalIgnoreCase);
        }

        if (segments.Any(static segment =>
                segment.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
                segment.Equals("obj", StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        return !(segments.Length == 1 &&
                 Path.GetExtension(filePath).Equals(".zip", StringComparison.OrdinalIgnoreCase));
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };
}
