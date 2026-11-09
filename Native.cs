using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ChillPortrait
{
    internal static class Native
    {
        internal const int Style = -16;
        internal const int ExtendedStyle = -20;
        internal const uint Popup = 0x80000000;
        internal const uint WindowFrame = 0x00cf0000;
        internal const uint MaximizeMinimize = 0x21000000;
        internal const uint Topmost = 0x00000008;
        internal const uint Transparent = 0x00000020;
        internal const uint Layered = 0x00080000;
        internal const uint NoActivate = 0x08000000;
        internal const uint NoSize = 0x0001;
        internal const uint NoMove = 0x0002;
        internal const uint NoZOrder = 0x0004;
        internal const uint NoFocus = 0x0010;
        internal const uint FrameChanged = 0x0020;
        internal const uint Show = 0x0040;
        internal static readonly IntPtr Bottom = new IntPtr(1);
        internal static readonly IntPtr Top = IntPtr.Zero;
        internal static readonly IntPtr TopMost = new IntPtr(-1);
        internal static readonly IntPtr NotTopMost = new IntPtr(-2);

        [StructLayout(LayoutKind.Sequential)]
        internal struct Point
        {
            internal int X, Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct Rect
        {
            internal int Left, Top, Right, Bottom;
            internal int Width => Right - Left;
            internal int Height => Bottom - Top;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct Placement
        {
            internal uint Length, Flags, ShowCommand;
            internal Point Min, Max;
            internal Rect Normal;

            internal static Placement Create() =>
                new Placement { Length = (uint)Marshal.SizeOf(typeof(Placement)) };
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        internal struct MonitorInfo
        {
            internal int Size;
            internal Rect Bounds, Work;
            internal uint Flags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            internal string Device;

            internal bool Primary => (Flags & 1) != 0;
        }

        internal delegate bool MonitorCallback(IntPtr monitor, IntPtr dc, IntPtr rect, IntPtr data);
        internal delegate bool WindowCallback(IntPtr window, IntPtr data);

        [DllImport("user32.dll")] internal static extern bool EnumWindows(WindowCallback callback, IntPtr data);
        [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int GetClassName(IntPtr window, System.Text.StringBuilder value, int max);
        [DllImport("user32.dll")] internal static extern bool IsWindow(IntPtr window);
        [DllImport("user32.dll")] internal static extern bool IsIconic(IntPtr window);
        [DllImport("user32.dll")] internal static extern bool IsZoomed(IntPtr window);
        [DllImport("user32.dll")] internal static extern bool ShowWindow(IntPtr window, int command);
        [DllImport("user32.dll")] internal static extern bool SetForegroundWindow(IntPtr window);
        [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] internal static extern bool GetWindowRect(IntPtr window, out Rect rect);
        [DllImport("user32.dll")] internal static extern bool GetWindowPlacement(IntPtr window, ref Placement placement);
        [DllImport("user32.dll")] internal static extern bool SetWindowPlacement(IntPtr window, ref Placement placement);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
        internal static extern uint GetWindowLong(IntPtr window, int index);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
        internal static extern uint SetWindowLong(IntPtr window, int index, uint value);
        [DllImport("user32.dll", SetLastError = true)]
        internal static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
        [DllImport("user32.dll", SetLastError = true)]
        internal static extern bool SetLayeredWindowAttributes(IntPtr window, uint key, byte opacity, uint flags);
        [DllImport("user32.dll")] internal static extern bool PostMessage(IntPtr window, uint message, IntPtr wparam, IntPtr lparam);
        [DllImport("user32.dll")] internal static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
        [DllImport("user32.dll")] internal static extern bool UnregisterHotKey(IntPtr window, int id);
        [DllImport("user32.dll")] internal static extern bool EnumDisplayMonitors(IntPtr dc, IntPtr rect, MonitorCallback callback, IntPtr data);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
        [DllImport("user32.dll")] internal static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);

        internal static IntPtr FindGameWindow()
        {
            uint owner;
            using (var process = Process.GetCurrentProcess())
                owner = (uint)process.Id;
            var result = IntPtr.Zero;
            EnumWindows((window, data) =>
            {
                GetWindowThreadProcessId(window, out uint processId);
                if (processId != owner)
                    return true;
                var name = new System.Text.StringBuilder(64);
                GetClassName(window, name, name.Capacity);
                if (name.ToString() != "UnityWndClass")
                    return true;
                result = window;
                return false;
            }, IntPtr.Zero);
            return result;
        }

        internal static List<MonitorInfo> Monitors()
        {
            var result = new List<MonitorInfo>();
            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (monitor, dc, rect, data) =>
            {
                var info = new MonitorInfo { Size = Marshal.SizeOf(typeof(MonitorInfo)) };
                if (GetMonitorInfo(monitor, ref info))
                    result.Add(info);
                return true;
            }, IntPtr.Zero);
            return result;
        }
    }
}
