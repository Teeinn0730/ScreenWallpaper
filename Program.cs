using System;
using System.Linq;
using System.Threading;
using System.Windows;

namespace ScreenWallpaper;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        using var mutex = new Mutex(true, @"Local\ScreenWallpaper_SingleInstance", out var isFirstInstance);
        if (!isFirstInstance)
        {
            MessageBox.Show("ScreenWallpaper 已經在執行中，請從右下角系統匣圖示操作。", "ScreenWallpaper");
            return;
        }

        System.Windows.Forms.Application.EnableVisualStyles();

        // Log unexpected errors instead of showing the .NET crash dialog. Tray menu clicks run
        // through WinForms, everything else through the WPF dispatcher.
        System.Windows.Forms.Application.SetUnhandledExceptionMode(System.Windows.Forms.UnhandledExceptionMode.CatchException);
        System.Windows.Forms.Application.ThreadException += (_, e) => Log.Write("Unhandled error: " + e.Exception);

        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.DispatcherUnhandledException += (_, e) =>
        {
            Log.Write("Unhandled error: " + e.Exception);
            e.Handled = true;
        };
        WallpaperController? controller = null;
        app.Startup += (_, _) => controller = new WallpaperController(args.FirstOrDefault());
        app.Exit += (_, _) => controller?.Dispose();
        app.Run();
    }
}
