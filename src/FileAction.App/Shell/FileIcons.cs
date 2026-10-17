using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FileAction.App.Native;

namespace FileAction.App.Shell;

internal static class FileIcons
{
    /// <summary>The Explorer icon for a file or folder.</summary>
    public static ImageSource? Get(string path, bool large = true)
    {
        var info = new Win32.SHFILEINFO();
        var flags = Win32.SHGFI_ICON | (large ? Win32.SHGFI_LARGEICON : Win32.SHGFI_SMALLICON);
        var result = Win32.SHGetFileInfo(path, 0, ref info, (uint)Marshal.SizeOf<Win32.SHFILEINFO>(), flags);
        if (result == IntPtr.Zero || info.hIcon == IntPtr.Zero)
            return null;
        try
        {
            var source = Imaging.CreateBitmapSourceFromHIcon(info.hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        finally
        {
            Win32.DestroyIcon(info.hIcon);
        }
    }
}
