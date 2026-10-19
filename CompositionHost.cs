using System;
using System.Runtime.InteropServices;
using Windows.System;
using Windows.UI.Composition;
using Windows.UI.Composition.Desktop;
using WinRT;

namespace ScreenWallpaper;

/// <summary>Windows.UI.Composition for plain Win32 windows: one Compositor on the UI thread.</summary>
internal static class CompositionHost
{
    [StructLayout(LayoutKind.Sequential)]
    private struct DispatcherQueueOptions
    {
        public int dwSize;
        public int threadType;    // DQTYPE_THREAD_CURRENT = 2
        public int apartmentType; // DQTAT_COM_STA = 2
    }

    [DllImport("CoreMessaging.dll")]
    private static extern int CreateDispatcherQueueController(DispatcherQueueOptions options, out IntPtr controller);

    private static readonly Guid IID_ICompositorDesktopInterop = new("29E691FA-4567-4DCA-B319-D0F207EB6807");

    private static IntPtr _queueController; // kept alive for the lifetime of the process
    private static Compositor? _compositor;

    public static Compositor Compositor
    {
        get
        {
            if (_compositor is not null) return _compositor;
            if (DispatcherQueue.GetForCurrentThread() is null)
            {
                var options = new DispatcherQueueOptions
                {
                    dwSize = Marshal.SizeOf<DispatcherQueueOptions>(),
                    threadType = 2,
                    apartmentType = 2,
                };
                Marshal.ThrowExceptionForHR(CreateDispatcherQueueController(options, out _queueController));
            }
            return _compositor = new Compositor();
        }
    }

    /// <summary>Lets composition visuals draw into an HWND (above its GDI content).</summary>
    public static unsafe DesktopWindowTarget CreateTarget(IntPtr hwnd)
    {
        var unknown = ((IWinRTObject)Compositor).NativeObject.ThisPtr;
        var iid = IID_ICompositorDesktopInterop;
        Marshal.ThrowExceptionForHR(Marshal.QueryInterface(unknown, in iid, out var interop));
        try
        {
            // ICompositorDesktopInterop::CreateDesktopWindowTarget is the first method after IUnknown.
            var vtable = *(IntPtr**)interop;
            var create = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int, IntPtr*, int>)vtable[3];
            IntPtr raw;
            Marshal.ThrowExceptionForHR(create(interop, hwnd, 1, &raw));
            var target = DesktopWindowTarget.FromAbi(raw);
            Marshal.Release(raw);
            return target;
        }
        finally
        {
            Marshal.Release(interop);
        }
    }
}
