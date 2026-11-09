using System;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;

namespace ChillPortrait
{
    internal enum Command { Toggle, Restore, Quit, Portrait, Inspect }

    internal sealed class TrayControls : IDisposable
    {
        private const uint TrayMessage = 0x8000;
        private const uint StopMessage = 0x8001;
        private const uint HotkeyMessage = 0x0312;
        private const int ShortcutId = 1;

        private readonly ConcurrentQueue<Command> commands;
        private readonly Action<string> log;
        private readonly bool diagnostics;
        private readonly string shortcut;
        private readonly WindowProcedure procedure;
        private readonly string className = "ChillPortraitControls_" + Guid.NewGuid().ToString("N");
        private readonly uint taskbarCreated = RegisterWindowMessage("TaskbarCreated");
        private NotifyIconData icon;
        private long handle;
        private volatile bool stopping;
        private volatile bool ready;

        internal bool Ready => ready && !stopping;

        internal TrayControls(ConcurrentQueue<Command> commands, string shortcut, bool diagnostics, Action<string> log)
        {
            this.commands = commands;
            this.shortcut = shortcut;
            this.diagnostics = diagnostics;
            this.log = log;
            procedure = ProcessMessage;
            new Thread(Run) { IsBackground = true, Name = "Chill Portrait tray" }.Start();
        }

        private void Run()
        {
            IntPtr instance = GetModuleHandle(null);
            var windowClass = new WindowClass
            {
                Size = (uint)Marshal.SizeOf(typeof(WindowClass)),
                Instance = instance,
                Name = className,
                Procedure = procedure
            };
            IntPtr window = IntPtr.Zero;
            bool registeredClass = false;
            try
            {
                registeredClass = RegisterClassEx(ref windowClass) != 0;
                if (!registeredClass)
                    throw new Win32Exception();
                window = CreateWindowEx(0, className, "Chill Portrait controls", 0, 0, 0, 0, 0,
                    IntPtr.Zero, IntPtr.Zero, instance, IntPtr.Zero);
                if (window == IntPtr.Zero)
                    throw new Win32Exception();
                Interlocked.Exchange(ref handle, window.ToInt64());

                icon = new NotifyIconData
                {
                    Size = (uint)Marshal.SizeOf(typeof(NotifyIconData)),
                    Window = window,
                    Id = 1,
                    Flags = 7,
                    Callback = TrayMessage,
                    Icon = LoadIcon(IntPtr.Zero, new IntPtr(32512)),
                    Tooltip = "Chill With You · 竖屏背景",
                    Info = "",
                    InfoTitle = ""
                };
                if (!ShellNotifyIcon(0, ref icon))
                    throw new InvalidOperationException("Windows rejected the tray icon.");
                bool registered = TryParseShortcut(shortcut, out uint modifiers, out uint key) &&
                    Native.RegisterHotKey(window, ShortcutId, modifiers | 0x4000, key);
                ready = true;
                log("Tray ready. " + (registered ? "Toggle shortcut: " : "Shortcut unavailable: ") + shortcut +
                    (diagnostics ? " hwnd=" + window : ""));
                while (!stopping && GetMessage(out NativeMessage message, IntPtr.Zero, 0, 0) > 0)
                {
                    TranslateMessage(ref message);
                    DispatchMessage(ref message);
                }
                if (registered)
                    Native.UnregisterHotKey(window, ShortcutId);
            }
            catch (Exception error)
            {
                log("Tray initialization failed: " + error);
            }
            finally
            {
                ready = false;
                Interlocked.Exchange(ref handle, 0);
                if (window != IntPtr.Zero)
                {
                    if (icon.Window != IntPtr.Zero)
                        ShellNotifyIcon(2, ref icon);
                    DestroyWindow(window);
                }
                if (registeredClass)
                    UnregisterClass(className, instance);
            }
        }

        private IntPtr ProcessMessage(IntPtr window, uint message, IntPtr wparam, IntPtr lparam)
        {
            if (message == StopMessage)
                PostQuitMessage(0);
            else if (message == HotkeyMessage && wparam.ToInt64() == ShortcutId)
                commands.Enqueue(Command.Toggle);
            else if (message == TrayMessage)
            {
                switch (lparam.ToInt32())
                {
                    case 0x0203: commands.Enqueue(Command.Restore); break;
                    case 0x0205: ShowMenu(window); break;
                }
            }
            else if (message == taskbarCreated && ready)
            {
                ready = ShellNotifyIcon(0, ref icon);
                if (!ready) commands.Enqueue(Command.Restore);
            }
            else
                return DefWindowProc(window, message, wparam, lparam);
            return IntPtr.Zero;
        }

        private void ShowMenu(IntPtr window)
        {
            var menu = CreatePopupMenu();
            if (menu == IntPtr.Zero)
                return;
            try
            {
                Add(menu, Command.Toggle, "切换背景模式");
                Add(menu, Command.Restore, "恢复游戏窗口");
                Add(menu, Command.Portrait, "移到右侧竖屏");
                if (diagnostics)
                    Add(menu, Command.Inspect, "记录镜头和界面");
                AppendMenu(menu, 0x0800, UIntPtr.Zero, null);
                Add(menu, Command.Quit, "退出游戏");
                GetCursorPos(out Native.Point point);
                Native.SetForegroundWindow(window);
                int result = TrackPopupMenu(menu, 0x0102, point.X, point.Y, 0, window, IntPtr.Zero);
                if (result > 0)
                    commands.Enqueue((Command)(result - 1));
                Native.PostMessage(window, 0, IntPtr.Zero, IntPtr.Zero);
            }
            finally { DestroyMenu(menu); }
        }

        private static void Add(IntPtr menu, Command command, string text) =>
            AppendMenu(menu, 0, new UIntPtr((uint)command + 1), text);

        private static bool TryParseShortcut(string value, out uint modifiers, out uint key)
        {
            modifiers = 0;
            key = 0;
            foreach (string part in value.Split('+'))
            {
                string token = part.Trim().ToUpperInvariant();
                switch (token)
                {
                    case "ALT": modifiers |= 1; break;
                    case "CTRL":
                    case "CONTROL": modifiers |= 2; break;
                    case "SHIFT": modifiers |= 4; break;
                    case "WIN": modifiers |= 8; break;
                    default:
                        if (key != 0)
                            return false;
                        if (token.Length == 1 && ((token[0] >= 'A' && token[0] <= 'Z') || (token[0] >= '0' && token[0] <= '9')))
                            key = token[0];
                        else if (token.StartsWith("F", StringComparison.Ordinal) && uint.TryParse(token.Substring(1), out uint number) &&
                            number >= 1 && number <= 24)
                            key = 0x6f + number;
                        else
                            return false;
                        break;
                }
            }
            return modifiers != 0 && key != 0;
        }

        public void Dispose()
        {
            stopping = true;
            var window = new IntPtr(Interlocked.Read(ref handle));
            if (window != IntPtr.Zero)
                Native.PostMessage(window, StopMessage, IntPtr.Zero, IntPtr.Zero);
        }

        private delegate IntPtr WindowProcedure(IntPtr window, uint message, IntPtr wparam, IntPtr lparam);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WindowClass
        {
            internal uint Size, Style;
            internal WindowProcedure Procedure;
            internal int ClassExtra, WindowExtra;
            internal IntPtr Instance, Icon, Cursor, Background;
            internal string Menu, Name;
            internal IntPtr SmallIcon;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeMessage
        {
            internal IntPtr Window;
            internal uint Message;
            internal IntPtr WParam, LParam;
            internal uint Time;
            internal Native.Point Point;
            internal uint Private;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct NotifyIconData
        {
            internal uint Size;
            internal IntPtr Window;
            internal uint Id, Flags, Callback;
            internal IntPtr Icon;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] internal string Tooltip;
            internal uint State, StateMask;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] internal string Info;
            internal uint TimeoutOrVersion;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] internal string InfoTitle;
            internal uint InfoFlags;
            internal Guid Guid;
            internal IntPtr BalloonIcon;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string module);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern ushort RegisterClassEx(ref WindowClass value);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool UnregisterClass(string name, IntPtr instance);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateWindowEx(uint exStyle, string className, string title, uint style, int x, int y, int width, int height,
            IntPtr parent, IntPtr menu, IntPtr instance, IntPtr parameter);
        [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr window);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterWindowMessage(string message);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr DefWindowProc(IntPtr window, uint message, IntPtr wparam, IntPtr lparam);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetMessage(out NativeMessage message, IntPtr window, uint min, uint max);
        [DllImport("user32.dll")] private static extern bool TranslateMessage(ref NativeMessage message);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr DispatchMessage(ref NativeMessage message);
        [DllImport("user32.dll")] private static extern void PostQuitMessage(int code);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr LoadIcon(IntPtr instance, IntPtr resource);
        [DllImport("shell32.dll", EntryPoint = "Shell_NotifyIconW")] private static extern bool ShellNotifyIcon(uint message, ref NotifyIconData data);
        [DllImport("user32.dll")] private static extern IntPtr CreatePopupMenu();
        [DllImport("user32.dll")] private static extern bool DestroyMenu(IntPtr menu);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool AppendMenu(IntPtr menu, uint flags, UIntPtr id, string text);
        [DllImport("user32.dll")] private static extern bool GetCursorPos(out Native.Point point);
        [DllImport("user32.dll")] private static extern int TrackPopupMenu(IntPtr menu, uint flags, int x, int y, int reserved, IntPtr window, IntPtr rect);
    }
}
