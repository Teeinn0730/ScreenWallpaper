using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Threading;
using Microsoft.Win32;
using static ScreenWallpaper.NativeMethods;
using Drawing = System.Drawing;
using WinForms = System.Windows.Forms;

namespace ScreenWallpaper;

/// <summary>Owns the tray icon, the settings and the wallpaper windows on every monitor.</summary>
internal sealed class WallpaperController : IDisposable
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValue = "ScreenWallpaper";

    private readonly Settings _settings;
    private readonly List<WallpaperSurface> _surfaces = new();
    private VideoPlayer? _video; // one decoder shared by every monitor
    private readonly WinForms.NotifyIcon _tray;
    private readonly DispatcherTimer _watchdog;
    private DesktopTarget _target;
    private string _screenLayout = "";
    private bool _userPaused;
    private bool _autoPaused;

    private WinForms.ToolStripMenuItem _pauseItem = null!;
    private WinForms.ToolStripMenuItem _muteItem = null!;
    private WinForms.ToolStripMenuItem _fullscreenItem = null!;
    private WinForms.ToolStripMenuItem _startupItem = null!;
    private readonly Dictionary<FitMode, WinForms.ToolStripMenuItem> _modeItems = new();

    public WallpaperController(string? initialFile)
    {
        _settings = Settings.Load();
        if (!string.IsNullOrEmpty(initialFile) && File.Exists(initialFile))
        {
            _settings.FilePath = Path.GetFullPath(initialFile);
            _settings.Save();
        }

        _tray = new WinForms.NotifyIcon
        {
            Icon = CreateTrayIcon(),
            Text = "ScreenWallpaper 動態桌布",
            Visible = true,
            ContextMenuStrip = BuildMenu(),
        };
        _tray.DoubleClick += (_, _) => ChooseFile();

        _watchdog = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _watchdog.Tick += (_, _) => Watch();
        _watchdog.Start();

        SystemEvents.DisplaySettingsChanged += OnDisplayChanged;

        if (_settings.FilePath is not null && File.Exists(_settings.FilePath))
            Apply();
        else
            ChooseFile();
    }

    private WinForms.ContextMenuStrip BuildMenu()
    {
        var menu = new WinForms.ContextMenuStrip();
        menu.Items.Add("選擇桌布檔案…", null, (_, _) => ChooseFile());

        _pauseItem = new WinForms.ToolStripMenuItem("暫停", null, (_, _) =>
        {
            _userPaused = !_userPaused;
            UpdatePlayState();
        });
        menu.Items.Add(_pauseItem);

        _muteItem = new WinForms.ToolStripMenuItem("靜音", null, (_, _) =>
        {
            _settings.Muted = !_settings.Muted;
            _settings.Save();
            _muteItem.Checked = _settings.Muted;
            if (_video is not null) _video.IsMuted = _settings.Muted;
        }) { Checked = _settings.Muted };
        menu.Items.Add(_muteItem);

        var modeMenu = new WinForms.ToolStripMenuItem("顯示模式");
        foreach (var (mode, label) in new[]
                 {
                     (FitMode.Fill, "填滿 (裁切邊緣)"),
                     (FitMode.Fit, "符合 (保留黑邊)"),
                     (FitMode.Stretch, "延展"),
                 })
        {
            var item = new WinForms.ToolStripMenuItem(label, null, (_, _) =>
            {
                _settings.Mode = mode;
                _settings.Save();
                RefreshModeChecks();
                Apply();
            });
            _modeItems[mode] = item;
            modeMenu.DropDownItems.Add(item);
        }
        RefreshModeChecks();
        menu.Items.Add(modeMenu);

        menu.Items.Add(new WinForms.ToolStripSeparator());

        _fullscreenItem = new WinForms.ToolStripMenuItem("全螢幕程式執行時自動暫停", null, (_, _) =>
        {
            _settings.PauseOnFullscreen = !_settings.PauseOnFullscreen;
            _settings.Save();
            _fullscreenItem.Checked = _settings.PauseOnFullscreen;
        }) { Checked = _settings.PauseOnFullscreen };
        menu.Items.Add(_fullscreenItem);

        _startupItem = new WinForms.ToolStripMenuItem("開機時自動啟動", null, (_, _) =>
        {
            SetStartup(!IsStartupEnabled());
            _startupItem.Checked = IsStartupEnabled();
        }) { Checked = IsStartupEnabled() };
        menu.Items.Add(_startupItem);

        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add("移除動態桌布", null, (_, _) =>
        {
            _settings.FilePath = null;
            _settings.Save();
            Clear();
        });
        menu.Items.Add("結束", null, (_, _) => System.Windows.Application.Current.Shutdown());
        return menu;
    }

    private void RefreshModeChecks()
    {
        foreach (var (mode, item) in _modeItems) item.Checked = mode == _settings.Mode;
    }

    private void ChooseFile()
    {
        var all = WallpaperSurface.VideoExtensions.Concat(WallpaperSurface.ImageExtensions).Select(e => "*" + e);
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "選擇動態桌布 (影片或 GIF)",
            Filter = $"影片或圖片|{string.Join(";", all)}|" +
                     $"影片|{string.Join(";", WallpaperSurface.VideoExtensions.Select(e => "*" + e))}|" +
                     "GIF 動畫|*.gif|所有檔案|*.*",
        };
        if (_settings.FilePath is not null)
            dialog.InitialDirectory = Path.GetDirectoryName(_settings.FilePath);

        if (dialog.ShowDialog() != true) return;
        _settings.FilePath = dialog.FileName;
        _settings.Save();
        _userPaused = false;
        Apply();
    }

    private void Apply()
    {
        DisposeSurfaces();
        var file = _settings.FilePath;
        if (file is null || !File.Exists(file)) return;

        try
        {
            _target = DesktopHost.Find();
            if (WallpaperSurface.IsVideo(file))
            {
                _video = new VideoPlayer(file, _settings.Muted);
                _video.Failed += msg =>
                {
                    Log.Write($"Playback failed for {file}: {msg}");
                    ShowError($"無法播放「{Path.GetFileName(file)}」：{msg}");
                };
            }
            _screenLayout = ScreenLayout();
            Log.Write($"Apply {file} screens={_screenLayout} parent=0x{_target.Parent:X} insertAfter=0x{_target.InsertAfter:X} layered={_target.Layered}");
            foreach (var screen in WinForms.Screen.AllScreens)
            {
                var b = screen.Bounds;
                var rect = new RECT { Left = b.Left, Top = b.Top, Right = b.Right, Bottom = b.Bottom };
                MapWindowPoints(IntPtr.Zero, _target.Parent, ref rect, 2); // screen -> parent client coordinates
                _surfaces.Add(new WallpaperSurface(_target, rect, file, _settings, _video));
            }
            _tray.Text = Truncate("動態桌布：" + Path.GetFileName(file), 63);
            UpdatePlayState();
        }
        catch (Exception ex)
        {
            Log.Write("Apply failed: " + ex);
            DisposeSurfaces();
            ShowError(ex.Message);
        }
    }

    private void Clear()
    {
        DisposeSurfaces();
        DesktopHost.RestoreStaticWallpaper();
        _tray.Text = "ScreenWallpaper 動態桌布";
    }

    private void DisposeSurfaces()
    {
        // Clean up as much as possible; a failure here must not leave stale surfaces behind.
        foreach (var s in _surfaces) TryDispose(s);
        _surfaces.Clear();
        if (_video is not null) TryDispose(_video);
        _video = null;
    }

    private static void TryDispose(IDisposable item)
    {
        try
        {
            item.Dispose();
        }
        catch (Exception ex)
        {
            Log.Write("Cleanup failed: " + ex);
        }
    }

    private void UpdatePlayState()
    {
        var paused = _userPaused || _autoPaused;
        if (paused) _video?.Pause();
        else _video?.Play();
        foreach (var s in _surfaces)
        {
            if (paused) s.Pause();
            else s.Play();
        }
        _pauseItem.Text = _userPaused ? "繼續播放" : "暫停";
    }

    private void Watch()
    {
        if (_surfaces.Count == 0) return;

        // Explorer restarted: our windows died with the old desktop, so rebuild them.
        if (!IsWindow(_target.Parent) || !IsWindow(_surfaces[0].Handle))
        {
            Log.Write("Desktop window lost (Explorer restarted?), rebuilding");
            Apply();
            return;
        }

        var autoPaused = _settings.PauseOnFullscreen && IsFullscreenAppActive();
        if (autoPaused != _autoPaused)
        {
            _autoPaused = autoPaused;
            UpdatePlayState();
        }
    }

    private static bool IsFullscreenAppActive()
    {
        var fg = GetForegroundWindow();
        if (fg == IntPtr.Zero) return false;
        var cls = NativeMethods.GetClassName(fg);
        if (cls is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd") return false;

        if (!GetWindowRect(fg, out var r)) return false;
        var info = new MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(MonitorFromWindow(fg, MONITOR_DEFAULTTONEAREST), ref info)) return false;
        var m = info.rcMonitor;
        return r.Left <= m.Left && r.Top <= m.Top && r.Right >= m.Right && r.Bottom >= m.Bottom;
    }

    private static string ScreenLayout() =>
        string.Join(";", WinForms.Screen.AllScreens.Select(s => s.Bounds.ToString()));

    private void OnDisplayChanged(object? sender, EventArgs e)
    {
        // Explorer broadcasts display changes when the desktop is split, so only rebuild
        // when monitors were actually added, removed, moved or resized.
        System.Windows.Application.Current.Dispatcher.BeginInvoke(() =>
        {
            if (_surfaces.Count == 0 || ScreenLayout() == _screenLayout) return;
            Log.Write("Monitor layout changed, rebuilding");
            Apply();
        });
    }

    private static bool IsStartupEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(RunValue) is not null;
    }

    private static void SetStartup(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled) key.SetValue(RunValue, StartupCommand());
        else key.DeleteValue(RunValue, false);
    }

    private static string StartupCommand()
    {
        var host = Environment.ProcessPath!;
        if (!Path.GetFileNameWithoutExtension(host).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            return $"\"{host}\"";

        // Started as "dotnet ScreenWallpaper.dll" (e.g. Smart App Control blocks the .exe):
        // go through the VBS launcher so no console window pops up at sign-in.
        var dir = AppContext.BaseDirectory;
        var launcher = Path.Combine(dir, "ScreenWallpaper.vbs");
        return File.Exists(launcher)
            ? $"wscript.exe \"{launcher}\""
            : $"\"{host}\" \"{Path.Combine(dir, "ScreenWallpaper.dll")}\"";
    }

    private void ShowError(string message) =>
        _tray.ShowBalloonTip(5000, "ScreenWallpaper", message, WinForms.ToolTipIcon.Error);

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";

    private static Drawing.Icon CreateTrayIcon()
    {
        using var bmp = new Drawing.Bitmap(32, 32);
        using (var g = Drawing.Graphics.FromImage(bmp))
        {
            g.SmoothingMode = Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var bg = new Drawing.Drawing2D.LinearGradientBrush(
                new Drawing.Rectangle(0, 0, 32, 32),
                Drawing.Color.FromArgb(0x4F, 0x8C, 0xFF), Drawing.Color.FromArgb(0x9B, 0x5C, 0xFF), 45f);
            g.FillRectangle(bg, 2, 4, 28, 24);
            g.FillPolygon(Drawing.Brushes.White, new[] { new Drawing.Point(12, 10), new Drawing.Point(23, 16), new Drawing.Point(12, 22) });
        }
        return Drawing.Icon.FromHandle(bmp.GetHicon());
    }

    public void Dispose()
    {
        SystemEvents.DisplaySettingsChanged -= OnDisplayChanged;
        _watchdog.Stop();
        Clear();
        _tray.Visible = false;
        _tray.Dispose();
    }
}
