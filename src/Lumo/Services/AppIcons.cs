using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Lumo.Services;

/// <summary>
/// v1.4.1 — extracts real shell icons (exe / .lnk target / .url / folder / file
/// type association) for the launcher's result rows, replacing the letter glyphs.
/// Returns frozen BitmapSources so background search threads can hand them
/// straight to the UI. Every path is resolved once, then cached.
/// </summary>
public static class AppIcons
{
    private const uint SHGFI_ICON = 0x000000100;
    private const uint SHGFI_LARGEICON = 0x000000000;
    private const uint FILE_ATTRIBUTE_DIRECTORY = 0x00000010;
    private const uint FILE_ATTRIBUTE_NORMAL = 0x00000080;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes,
        ref SHFILEINFO psfi, uint cbSizeFileInfo, uint uFlags);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

    private static readonly ConcurrentDictionary<string, ImageSource?> Cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Icon for a file / folder / shortcut path, or null when nothing sensible
    /// exists (the row then falls back to its letter glyph). Safe from any thread.
    /// </summary>
    public static ImageSource? ForPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        string key = path.Trim();
        if (Cache.TryGetValue(key, out var cached)) return cached;

        ImageSource? icon = Extract(key);
        Cache[key] = icon;
        if (Cache.Count > 4096) Cache.Clear(); // safety valve for very long sessions
        return icon;
    }

    private static ImageSource? Extract(string path)
    {
        try
        {
            // Only real, existing targets — keeps lookups fast and avoids
            // blocking on dead network shortcuts. .lnk files exist on disk and
            // SHGetFileInfo resolves their target icon without touching it.
            bool isDir = Directory.Exists(path);
            if (!isDir && !File.Exists(path)) return null;

            var sfi = new SHFILEINFO();
            IntPtr res = SHGetFileInfo(
                path,
                isDir ? FILE_ATTRIBUTE_DIRECTORY : FILE_ATTRIBUTE_NORMAL,
                ref sfi, (uint)Marshal.SizeOf<SHFILEINFO>(),
                SHGFI_ICON | SHGFI_LARGEICON);

            if (res == IntPtr.Zero || sfi.hIcon == IntPtr.Zero) return null;
            try
            {
                var src = Imaging.CreateBitmapSourceFromHIcon(
                    sfi.hIcon,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());
                src.Freeze(); // required for cross-thread display
                return src;
            }
            finally
            {
                DestroyIcon(sfi.hIcon);
            }
        }
        catch (Exception ex)
        {
            DiagnosticLogger.LogException("AppIcons", ex);
            return null;
        }
    }
}
