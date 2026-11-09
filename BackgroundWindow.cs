using System;
using System.Linq;
using System.Runtime.InteropServices;
using UnityEngine;

namespace ChillPortrait
{
    internal sealed class BackgroundWindow
    {
        private readonly Func<Native.MonitorInfo?> findTarget;
        private readonly Action<string> log;
        private IntPtr window;
        private Native.Placement placement;
        private uint style, extendedStyle;
        private bool runInBackground;
        private string device;
        private Native.Rect lastBounds;

        internal bool Active => window != IntPtr.Zero;

        internal BackgroundWindow(Func<Native.MonitorInfo?> findTarget, Action<string> log)
        {
            this.findTarget = findTarget;
            this.log = log;
        }

        private static IntPtr SetMonitorDpi() => Native.SetThreadDpiAwarenessContext(new IntPtr(-4));
        private static void RestoreDpi(IntPtr context)
        {
            if (context != IntPtr.Zero) Native.SetThreadDpiAwarenessContext(context);
        }

        internal void Enter()
        {
            if (Active)
                return;
            var dpi = SetMonitorDpi();
            try
            {
                var target = findTarget();
                var game = Native.FindGameWindow();
                if (!target.HasValue || game == IntPtr.Zero || Screen.fullScreenMode == FullScreenMode.ExclusiveFullScreen)
                {
                    log("Background mode requires the target display and a windowed or borderless game.");
                    return;
                }
                placement = Native.Placement.Create();
                if (!Native.GetWindowPlacement(game, ref placement))
                    return;
                style = Native.GetWindowLong(game, Native.Style);
                extendedStyle = Native.GetWindowLong(game, Native.ExtendedStyle);
                if ((extendedStyle & Native.Layered) != 0)
                {
                    log("Window already uses layered rendering. Background mode skipped.");
                    return;
                }
                device = target.Value.Device;
                runInBackground = Application.runInBackground;
                window = game;

                if (Native.IsZoomed(game) || Native.IsIconic(game))
                    Native.ShowWindow(game, 4);
                uint backgroundStyle = (style & ~(Native.WindowFrame | Native.MaximizeMinimize)) | Native.Popup;
                uint backgroundExStyle = (extendedStyle & ~Native.Topmost) | Native.Layered | Native.Transparent | Native.NoActivate;
                Native.SetWindowLong(game, Native.Style, backgroundStyle);
                Native.SetWindowLong(game, Native.ExtendedStyle, backgroundExStyle);
                const uint required = Native.Layered | Native.Transparent | Native.NoActivate;
                if ((Native.GetWindowLong(game, Native.ExtendedStyle) & required) != required ||
                    !Native.SetLayeredWindowAttributes(game, 0, 255, 2) ||
                    !Place(game, Native.Bottom, target.Value.Bounds, Native.FrameChanged | Native.Show | Native.NoFocus))
                {
                    int error = Marshal.GetLastWin32Error();
                    Restore(false);
                    log("Could not enable background mode. Restored the window. Win32 error " + error);
                    return;
                }

                Application.runInBackground = true;
                lastBounds = target.Value.Bounds;
                log($"Background on {device}: {lastBounds.Width}x{lastBounds.Height}");
            }
            finally { RestoreDpi(dpi); }
        }

        internal void Maintain()
        {
            if (!Active)
                return;
            if (!Native.IsWindow(window))
            {
                window = IntPtr.Zero;
                Application.runInBackground = runInBackground;
                return;
            }

            var dpi = SetMonitorDpi();
            try
            {
                var target = Native.Monitors().FirstOrDefault(monitor => monitor.Device == device);
                if (target.Device == null)
                {
                    Restore(false);
                    return;
                }
                const uint required = Native.Layered | Native.Transparent | Native.NoActivate;
                if ((Native.GetWindowLong(window, Native.ExtendedStyle) & required) != required || Native.IsIconic(window))
                {
                    Restore(false);
                    return;
                }
                if (!Native.GetWindowRect(window, out Native.Rect actual) || !actual.Equals(target.Bounds) ||
                    !lastBounds.Equals(target.Bounds))
                {
                    if (!Place(window, Native.Bottom, target.Bounds, Native.NoFocus | Native.Show))
                        Restore(false);
                    lastBounds = target.Bounds;
                }
                else if (Native.GetForegroundWindow() == window ||
                    (Native.GetWindowLong(window, Native.ExtendedStyle) & Native.Topmost) != 0)
                {
                    Native.SetWindowPos(window, Native.Bottom, 0, 0, 0, 0, Native.NoFocus | Native.NoMove | Native.NoSize);
                }
            }
            finally { RestoreDpi(dpi); }
        }

        internal void Restore(bool focus)
        {
            if (!Active)
                return;
            var game = window;
            window = IntPtr.Zero;
            Application.runInBackground = runInBackground;
            if (!Native.IsWindow(game))
                return;
            var dpi = SetMonitorDpi();
            try
            {
                Native.SetWindowLong(game, Native.Style, style);
                Native.SetWindowLong(game, Native.ExtendedStyle, extendedStyle & ~Native.Topmost);
                Native.SetWindowPos(game, (extendedStyle & Native.Topmost) != 0 ? Native.TopMost : Native.NotTopMost,
                    0, 0, 0, 0, Native.NoSize | Native.NoMove | Native.FrameChanged | Native.NoFocus);
                if (placement.ShowCommand == 2 || placement.ShowCommand == 6 || placement.ShowCommand == 7)
                    placement.ShowCommand = 1;
                Native.SetWindowPlacement(game, ref placement);

                var monitors = Native.Monitors();
                if (monitors.All(monitor => monitor.Device != device))
                {
                    var primary = monitors.FirstOrDefault(monitor => monitor.Primary);
                    if (primary.Device != null)
                        PlaceInteractive(game, primary.Work);
                }
                if (focus)
                    BringForward(game);
                log("Interactive window restored.");
            }
            finally { RestoreDpi(dpi); }
        }

        internal void Focus()
        {
            var game = Native.FindGameWindow();
            if (game != IntPtr.Zero)
                BringForward(game);
        }

        internal bool PlaceOnTarget(bool startup)
        {
            var dpi = SetMonitorDpi();
            try
            {
                var game = Native.FindGameWindow();
                if (game == IntPtr.Zero || Screen.fullScreenMode != FullScreenMode.Windowed)
                    return false;
                var target = findTarget();
                if (!target.HasValue)
                    return false;
                if (startup && Native.GetWindowRect(game, out Native.Rect rect) &&
                    ContainsCenter(target.Value.Bounds, rect))
                    return true;
                return PlaceInteractive(game, target.Value.Work);
            }
            finally { RestoreDpi(dpi); }
        }

        private static bool ContainsCenter(Native.Rect bounds, Native.Rect window)
        {
            int x = window.Left + window.Width / 2;
            int y = window.Top + window.Height / 2;
            return x >= bounds.Left && x < bounds.Right && y >= bounds.Top && y < bounds.Bottom;
        }

        private static bool PlaceInteractive(IntPtr game, Native.Rect work)
        {
            if (Native.IsZoomed(game) || Native.IsIconic(game))
                Native.ShowWindow(game, 4);
            return Place(game, Native.Top, work, Native.NoZOrder | Native.NoFocus | Native.Show);
        }

        private static bool Place(IntPtr game, IntPtr after, Native.Rect rect, uint flags) =>
            Native.SetWindowPos(game, after, rect.Left, rect.Top, rect.Width, rect.Height, flags);

        private static void BringForward(IntPtr game)
        {
            if (Native.IsIconic(game))
                Native.ShowWindow(game, 9);
            Native.SetWindowPos(game, Native.Top, 0, 0, 0, 0, Native.NoMove | Native.NoSize | Native.Show);
            Native.SetForegroundWindow(game);
        }
    }
}
