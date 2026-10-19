using System;
using System.Text;
using static ScreenWallpaper.NativeMethods;

namespace ScreenWallpaper;

/// <summary>Where wallpaper windows get parented so they draw behind the desktop icons.</summary>
internal readonly record struct DesktopTarget(IntPtr Parent, IntPtr InsertAfter, bool Layered);

internal static class DesktopHost
{
    /// <summary>
    /// Asks Explorer to split the desktop into an icon layer and a wallpaper layer,
    /// then returns the window our wallpaper should live in.
    /// </summary>
    public static DesktopTarget Find()
    {
        var progman = FindWindow("Progman", null);
        if (progman == IntPtr.Zero)
            throw new InvalidOperationException("找不到桌面視窗 (Progman)，Explorer 是否正在執行？");

        // Undocumented message that makes Progman spawn a WorkerW behind the desktop icons.
        SendMessageTimeout(progman, 0x052C, new IntPtr(0xD), new IntPtr(0x1), SMTO_NORMAL, 1000, out _);
        SendMessageTimeout(progman, 0x052C, IntPtr.Zero, IntPtr.Zero, SMTO_NORMAL, 1000, out _);

        // Windows 11 24H2 and later: the WorkerW is a child of Progman. The wallpaper goes
        // directly into Progman, z-ordered just below the icon view, and must be layered.
        var childWorker = FindWindowEx(progman, IntPtr.Zero, "WorkerW", null);
        if (childWorker != IntPtr.Zero)
        {
            var defView = FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);
            return new DesktopTarget(progman, defView, Layered: true);
        }

        // Older Windows: the WorkerW is a top-level window right after the one hosting the icons.
        var workerw = IntPtr.Zero;
        EnumWindows((top, _) =>
        {
            if (FindWindowEx(top, IntPtr.Zero, "SHELLDLL_DefView", null) != IntPtr.Zero)
                workerw = FindWindowEx(IntPtr.Zero, top, "WorkerW", null);
            return true;
        }, IntPtr.Zero);

        return new DesktopTarget(workerw != IntPtr.Zero ? workerw : progman, IntPtr.Zero, Layered: false);
    }

    /// <summary>Re-applies the current static wallpaper so the last video frame is wiped from the desktop.</summary>
    public static void RestoreStaticWallpaper()
    {
        var path = new StringBuilder(520);
        if (SystemParametersInfo(SPI_GETDESKWALLPAPER, (uint)path.Capacity, path, 0))
            SystemParametersInfo(SPI_SETDESKWALLPAPER, 0, path, 0);
    }
}
