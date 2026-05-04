using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Media;

namespace OpenQuickHost;

public static class NativeFileIconService
{
    private const uint ShgfiIcon = 0x000000100;
    private const uint ShgfiLargeIcon = 0x000000000;
    private const uint ShgfiUseFileAttributes = 0x000000010;
    private const uint FileAttributeNormal = 0x00000080;
    private const uint FileAttributeDirectory = 0x00000010;
    private static readonly ConcurrentDictionary<string, ImageSource?> IconCache = new(StringComparer.OrdinalIgnoreCase);

    public static ImageSource? GetIcon(string path, bool isFolder)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var cacheKey = BuildCacheKey(path, isFolder);
        return IconCache.GetOrAdd(cacheKey, _ => LoadSmallIcon(path, isFolder));
    }

    private static string BuildCacheKey(string path, bool isFolder)
    {
        if (isFolder)
        {
            return "__folder__";
        }

        var extension = Path.GetExtension(path);
        if (UsesPathSpecificIcon(extension))
        {
            return path;
        }

        return string.IsNullOrWhiteSpace(extension) ? path : extension;
    }

    private static bool UsesPathSpecificIcon(string? extension)
    {
        return extension is not null &&
               (extension.Equals(".exe", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".url", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".ico", StringComparison.OrdinalIgnoreCase));
    }

    private static ImageSource? LoadSmallIcon(string path, bool isFolder)
    {
        var attributes = isFolder ? FileAttributeDirectory : FileAttributeNormal;
        var flags = ShgfiIcon | ShgfiLargeIcon;
        var targetPath = path;
        if (!File.Exists(path) && !Directory.Exists(path))
        {
            flags |= ShgfiUseFileAttributes;
            if (isFolder)
            {
                targetPath = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            }
        }

        var shinfo = new Shfileinfo();
        var handle = SHGetFileInfo(targetPath, attributes, ref shinfo, (uint)Marshal.SizeOf<Shfileinfo>(), flags);
        if (handle == IntPtr.Zero || shinfo.hIcon == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            var image = Imaging.CreateBitmapSourceFromHIcon(
                shinfo.hIcon,
                System.Windows.Int32Rect.Empty,
                System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());
            image.Freeze();
            return image;
        }
        finally
        {
            DestroyIcon(shinfo.hIcon);
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(
        string pszPath,
        uint dwFileAttributes,
        ref Shfileinfo psfi,
        uint cbFileInfo,
        uint uFlags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Shfileinfo
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }
}
