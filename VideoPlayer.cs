using System;
using System.Windows.Threading;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.UI.Composition;

namespace ScreenWallpaper;

/// <summary>
/// One Media Foundation decoder shared by every monitor. Unlike WPF's MediaElement (the old
/// Windows Media Player engine), this uses the installed codec extensions, so WebM/VP9/AV1/HEVC work.
/// </summary>
internal sealed class VideoPlayer : IDisposable
{
    private readonly MediaPlayer _player;
    private readonly MediaPlayerSurface _surface;

    public event Action<string>? Failed;

    public VideoPlayer(string file, bool muted)
    {
        var dispatcher = Dispatcher.CurrentDispatcher;
        _player = new MediaPlayer
        {
            AutoPlay = false,
            IsLoopingEnabled = true,
            IsMuted = muted,
        };
        _player.CommandManager.IsEnabled = false; // keep the wallpaper out of the system media controls

        // Player events arrive on worker threads.
        _player.MediaOpened += (sender, _) =>
        {
            var session = sender.PlaybackSession;
            var size = new Windows.Foundation.Size(session.NaturalVideoWidth, session.NaturalVideoHeight);
            if (size.Width > 0 && size.Height > 0)
                dispatcher.BeginInvoke(() => _player.SetSurfaceSize(size));
        };
        _player.MediaFailed += (_, e) =>
        {
            var message = string.IsNullOrWhiteSpace(e.ErrorMessage)
                ? $"{e.Error} (0x{e.ExtendedErrorCode?.HResult:X8})"
                : e.ErrorMessage;
            dispatcher.BeginInvoke(() => Failed?.Invoke(message));
        };

        _surface = _player.GetSurface(CompositionHost.Compositor);
        _player.Source = MediaSource.CreateFromUri(new Uri(file));
    }

    public ICompositionSurface Surface => _surface.CompositionSurface;

    public void Play() => _player.Play();

    public void Pause() => _player.Pause();

    public bool IsMuted
    {
        set => _player.IsMuted = value;
    }

    public void Dispose()
    {
        _player.Pause();
        _surface.Dispose();
        _player.Dispose();
    }
}
