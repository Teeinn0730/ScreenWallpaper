using System;
using System.IO;
using System.Linq;
using System.Numerics;
using Windows.UI.Composition;
using Windows.UI.Composition.Desktop;
using static ScreenWallpaper.NativeMethods;
using Drawing = System.Drawing;
using WinForms = System.Windows.Forms;

namespace ScreenWallpaper;

/// <summary>One wallpaper window covering a single monitor.</summary>
internal sealed class WallpaperSurface : IDisposable
{
    public static readonly string[] VideoExtensions = [".mp4", ".m4v", ".mov", ".wmv", ".avi", ".mkv", ".webm"];
    public static readonly string[] ImageExtensions = [".gif", ".png", ".jpg", ".jpeg", ".bmp"];

    public static bool IsVideo(string path) =>
        VideoExtensions.Contains(Path.GetExtension(path).ToLowerInvariant());

    // Every surface is a layered WinForms window parented into the desktop (Windows 11 24H2+ only
    // draws layered windows there). It shows either the shared video through a composition visual,
    // or a WinForms PictureBox for GIF and still images.
    private DesktopChild? _host;
    private WinForms.Control? _videoView;
    private DesktopWindowTarget? _videoTarget;
    private CompositionSurfaceBrush? _videoBrush;
    private SpriteVisual? _videoVisual;
    private WinForms.PictureBox? _picture;
    private MemoryStream? _imageStream;
    private Drawing.Image? _image;

    public WallpaperSurface(DesktopTarget target, RECT bounds, string file, Settings settings, VideoPlayer? video)
    {
        try
        {
            _host = new DesktopChild(target, bounds);
            _host.CreateControl();
            if (target.Layered)
                SetLayeredWindowAttributes(Handle, 0, 255, LWA_ALPHA);

            if (video is not null) CreateVideo(bounds, video, settings.Mode);
            else CreateImage(file, settings);

            if (target.InsertAfter != IntPtr.Zero)
                SetWindowPos(Handle, target.InsertAfter, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public IntPtr Handle => _host?.Handle ?? IntPtr.Zero;

    private void CreateVideo(RECT bounds, VideoPlayer video, FitMode mode)
    {
        // Composition draws on top of a window's GDI content, so it gets a plain child window of its own.
        _videoView = new WinForms.Control { BackColor = Drawing.Color.Black, Bounds = new(0, 0, bounds.Width, bounds.Height) };
        _host!.Controls.Add(_videoView);

        var compositor = CompositionHost.Compositor;
        _videoBrush = compositor.CreateSurfaceBrush(video.Surface);
        _videoBrush.Stretch = mode switch
        {
            FitMode.Fit => CompositionStretch.Uniform,
            FitMode.Stretch => CompositionStretch.Fill,
            _ => CompositionStretch.UniformToFill,
        };
        _videoVisual = compositor.CreateSpriteVisual();
        _videoVisual.Brush = _videoBrush;
        _videoVisual.Size = new Vector2(bounds.Width, bounds.Height);

        _videoTarget = CompositionHost.CreateTarget(_videoView.Handle);
        _videoTarget.Root = _videoVisual;
    }

    private void CreateImage(string file, Settings settings)
    {
        // PictureBox plays GIF frames with their own timing. An animated image needs its stream kept open.
        _imageStream = new MemoryStream(File.ReadAllBytes(file));
        _image = Drawing.Image.FromStream(_imageStream);

        _picture = new WinForms.PictureBox
        {
            Image = _image,
            BackColor = Drawing.Color.Black,
            SizeMode = settings.Mode == FitMode.Fit
                ? WinForms.PictureBoxSizeMode.Zoom
                : WinForms.PictureBoxSizeMode.StretchImage,
        };
        _host!.Controls.Add(_picture);
        LayoutPicture(settings.Mode);
        _host.Resize += (_, _) => LayoutPicture(settings.Mode);
    }

    private void LayoutPicture(FitMode mode)
    {
        if (_host is null || _picture is null || _image is null) return;
        var area = _host.ClientSize;
        if (mode != FitMode.Fill || _image.Width == 0 || _image.Height == 0)
        {
            _picture.Bounds = new Drawing.Rectangle(Drawing.Point.Empty, area);
            return;
        }
        // "Fill": scale to cover the whole screen and crop the overflow, like UniformToFill.
        var scale = Math.Max((double)area.Width / _image.Width, (double)area.Height / _image.Height);
        var w = (int)Math.Ceiling(_image.Width * scale);
        var h = (int)Math.Ceiling(_image.Height * scale);
        _picture.Bounds = new Drawing.Rectangle((area.Width - w) / 2, (area.Height - h) / 2, w, h);
    }

    // The shared VideoPlayer is played, paused and muted by its owner; only GIFs are controlled here.
    public void Play()
    {
        if (_picture is not null) _picture.Enabled = true; // PictureBox only animates while enabled
    }

    public void Pause()
    {
        if (_picture is not null) _picture.Enabled = false;
    }

    public void Dispose()
    {
        // Children before parents: disposing the target also closes its root visual.
        _videoBrush?.Dispose();
        _videoVisual?.Dispose();
        _videoTarget?.Dispose();
        _host?.Dispose();
        _image?.Dispose();
        _imageStream?.Dispose();
    }

    /// <summary>A WinForms control whose native window is a child of Explorer's desktop window.</summary>
    private sealed class DesktopChild : WinForms.Control
    {
        private readonly DesktopTarget _target;

        public DesktopChild(DesktopTarget target, RECT bounds)
        {
            _target = target;
            BackColor = Drawing.Color.Black;
            SetBounds(bounds.Left, bounds.Top, bounds.Width, bounds.Height);
        }

        protected override WinForms.CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.Parent = _target.Parent;
                cp.Style = WS_CHILD | WS_VISIBLE | WS_CLIPCHILDREN | WS_CLIPSIBLINGS;
                cp.ExStyle = WS_EX_NOACTIVATE | (_target.Layered ? WS_EX_LAYERED : 0);
                return cp;
            }
        }
    }
}
