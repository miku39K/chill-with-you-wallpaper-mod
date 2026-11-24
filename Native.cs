using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace ChillWallpaper
{
    internal static class Native
    {
        internal const string GameProcessName = "Chill With You";
        internal const int StyleIndex = -16;
        internal const int ExtendedStyleIndex = -20;
        internal const int Popup = unchecked((int)0x80000000);
        internal const int Child = 0x40000000;
        internal const int ClipSiblings = 0x04000000;
        internal const int Disabled = 0x08000000;
        internal const int Maximized = 0x01000000;
        internal const int WindowFrame = 0x00CF0000;
        internal const int AppWindow = 0x00040000;
        internal const int Topmost = 0x00000008;
        internal const int ToolWindow = 0x00000080;
        internal const int NoActivate = 0x08000000;
        private const uint FrameChanged = 0x0020;
        private const uint ShowWindowFlag = 0x0040;
        private const uint NoActivateFlag = 0x0010;
        private const uint CreateWorkerWindowMessage = 0x052C;

        internal static IntPtr FindGameWindow(int gamePid)
        {
            int session;
            using (var current = Process.GetCurrentProcess())
                session = current.SessionId;
            var processes = Process.GetProcessesByName(GameProcessName);
            try
            {
                foreach (var process in processes)
                {
                    try
                    {
                        if ((gamePid != 0 && process.Id != gamePid) || process.SessionId != session || process.HasExited)
                            continue;
                        IntPtr window = FindWindowEx(IntPtr.Zero, IntPtr.Zero, "UnityWndClass", null);
                        while (window != IntPtr.Zero)
                        {
                            if (GetPid(window) == process.Id && IsWindowVisible(window))
                                return window;
                            window = FindWindowEx(IntPtr.Zero, window, "UnityWndClass", null);
                        }
                    }
                    catch (InvalidOperationException) { }
                }
                return IntPtr.Zero;
            }
            finally
            {
                foreach (var process in processes)
                    process.Dispose();
            }
        }

        internal static bool IsGameWindow(IntPtr window)
        {
            var className = new StringBuilder(128);
            GetClassName(window, className, className.Capacity);
            return className.ToString() == "UnityWndClass";
        }

        internal static IntPtr FindWallpaperLayer()
        {
            IntPtr shell = GetShellWindow();
            if (shell == IntPtr.Zero)
                return IntPtr.Zero;
            int shellPid = GetPid(shell);
            IntPtr iconView;
            IntPtr worker = FindWorkerBelowIcons(shellPid, out iconView);
            if (worker != IntPtr.Zero)
                return worker;

            IntPtr ignored;
            SendMessageTimeout(shell, CreateWorkerWindowMessage, IntPtr.Zero, IntPtr.Zero, 2, 1000, out ignored);
            return FindWorkerBelowIcons(shellPid, out iconView);
        }

        internal static IntPtr FindDesktopIconView(IntPtr wallpaperLayer)
        {
            IntPtr iconView;
            IntPtr layer = FindWorkerBelowIcons(GetPid(wallpaperLayer), out iconView);
            return layer == wallpaperLayer ? iconView : IntPtr.Zero;
        }

        private static IntPtr FindWorkerBelowIcons(int shellPid, out IntPtr iconView)
        {
            IntPtr worker = IntPtr.Zero;
            IntPtr desktopView = IntPtr.Zero;
            EnumWindows((window, ignored) =>
            {
                if (GetPid(window) != shellPid)
                    return true;
                IntPtr view = FindWindowEx(window, IntPtr.Zero, "SHELLDLL_DefView", null);
                if (view == IntPtr.Zero)
                    return true;

                IntPtr candidate = FindWindowEx(IntPtr.Zero, window, "WorkerW", null);
                if (candidate == IntPtr.Zero || GetPid(candidate) != shellPid || !IsWindowVisible(candidate))
                    return true;

                worker = candidate;
                desktopView = view;
                return false;
            }, IntPtr.Zero);
            iconView = desktopView;
            return worker;
        }

        internal static int GetPid(IntPtr window)
        {
            int pid;
            GetWindowThreadProcessId(window, out pid);
            return pid;
        }

        internal static Rectangle GetBounds(IntPtr window)
        {
            Rect rect;
            Check(GetWindowRect(window, out rect), "无法读取游戏窗口尺寸");
            return Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
        }

        internal static Rectangle GetClientBoundsOnScreen(IntPtr window)
        {
            Rect rect;
            Check(GetClientRect(window, out rect), "无法读取桌面壁纸层尺寸");
            var origin = new Point();
            Check(ClientToScreen(window, ref origin), "无法定位桌面壁纸层");
            return new Rectangle(origin.X, origin.Y, rect.Right - rect.Left, rect.Bottom - rect.Top);
        }

        internal static void WriteStyles(IntPtr window, int style, int extendedStyle)
        {
            WriteStyle(window, StyleIndex, style);
            WriteStyle(window, ExtendedStyleIndex, extendedStyle);
        }

        private static void WriteStyle(IntPtr window, int index, int value)
        {
            SetLastError(0);
            int previous = SetWindowLong(window, index, value);
            if (previous == 0)
                Check(Marshal.GetLastWin32Error() == 0, "无法设置游戏窗口样式");
        }

        internal static void Reparent(IntPtr window, IntPtr parent)
        {
            SetLastError(0);
            IntPtr previous = SetParent(window, parent);
            if (previous == IntPtr.Zero)
                Check(Marshal.GetLastWin32Error() == 0, "无法切换游戏窗口所属层");
            IntPtr actual = GetParent(window);
            if (actual != parent && (parent != IntPtr.Zero || actual != GetDesktopWindow()))
                throw new InvalidOperationException("Windows 未能切换游戏窗口所属层。");
        }

        internal static void Position(IntPtr window, IntPtr order, Rectangle bounds)
        {
            Check(SetWindowPos(window, order, bounds.X, bounds.Y, bounds.Width, bounds.Height,
                FrameChanged | NoActivateFlag | ShowWindowFlag), "无法调整游戏窗口位置");
        }

        internal static Placement ReadPlacement(IntPtr window)
        {
            var state = new Placement { Length = Marshal.SizeOf(typeof(Placement)) };
            Check(GetWindowPlacement(window, ref state), "无法读取游戏窗口显示状态");
            return state;
        }

        internal static void RestorePlacement(IntPtr window, Placement state)
        {
            Check(SetWindowPlacement(window, ref state), "无法恢复游戏窗口显示状态");
        }

        internal static void FocusWindow(IntPtr window)
        {
            if (IsIconic(window))
                Unminimize(window);
            SetForegroundWindow(window);
        }

        internal static void Unminimize(IntPtr window)
        {
            ShowWindow(window, 9);
            if (IsIconic(window))
                throw new InvalidOperationException("请先从任务栏打开游戏窗口。");
        }

        private static void Check(bool result, string operation)
        {
            if (result)
                return;
            int code = Marshal.GetLastWin32Error();
            throw new Win32Exception(code, operation + "（Windows 错误 " + code + "）。");
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct Point
        {
            internal int X, Y;

            internal static Point Read(BinaryReader data)
            {
                return new Point { X = data.ReadInt32(), Y = data.ReadInt32() };
            }

            internal void Write(BinaryWriter data)
            {
                data.Write(X);
                data.Write(Y);
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct Rect
        {
            internal int Left, Top, Right, Bottom;

            internal static Rect Read(BinaryReader data)
            {
                return new Rect
                {
                    Left = data.ReadInt32(), Top = data.ReadInt32(),
                    Right = data.ReadInt32(), Bottom = data.ReadInt32()
                };
            }

            internal void Write(BinaryWriter data)
            {
                data.Write(Left);
                data.Write(Top);
                data.Write(Right);
                data.Write(Bottom);
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct Placement
        {
            internal int Length, Flags, ShowCommand;
            internal Point MinPosition, MaxPosition;
            internal Rect NormalPosition;

            internal static Placement Read(BinaryReader data)
            {
                return new Placement
                {
                    Length = Marshal.SizeOf(typeof(Placement)),
                    Flags = data.ReadInt32(),
                    ShowCommand = data.ReadInt32(),
                    MinPosition = Point.Read(data),
                    MaxPosition = Point.Read(data),
                    NormalPosition = Rect.Read(data)
                };
            }

            internal void Write(BinaryWriter data)
            {
                data.Write(Flags);
                data.Write(ShowCommand);
                MinPosition.Write(data);
                MaxPosition.Write(data);
                NormalPosition.Write(data);
            }
        }

        private delegate bool EnumWindowCallback(IntPtr window, IntPtr parameter);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowCallback callback, IntPtr parameter);
        [DllImport("user32.dll")]
        private static extern IntPtr GetShellWindow();
        [DllImport("user32.dll")]
        private static extern IntPtr GetDesktopWindow();
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        internal static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string className, string title);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr window, StringBuilder className, int capacity);
        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr window, out int pid);
        [DllImport("user32.dll")]
        internal static extern bool IsWindow(IntPtr window);
        [DllImport("user32.dll")]
        internal static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll")]
        internal static extern bool IsIconic(IntPtr window);
        [DllImport("user32.dll")]
        internal static extern IntPtr GetParent(IntPtr window);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetParent(IntPtr window, IntPtr parent);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        internal static extern int GetWindowLong(IntPtr window, int index);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int SetWindowLong(IntPtr window, int index, int value);
        [DllImport("kernel32.dll")]
        private static extern void SetLastError(uint code);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool GetWindowRect(IntPtr window, out Rect rectangle);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool GetClientRect(IntPtr window, out Rect rectangle);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool ClientToScreen(IntPtr window, ref Point point);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr window, IntPtr order, int x, int y, int width, int height, uint flags);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool GetWindowPlacement(IntPtr window, ref Placement placement);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPlacement(IntPtr window, ref Placement placement);
        [DllImport("user32.dll")]
        internal static extern bool EnableWindow(IntPtr window, bool enabled);
        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr window, int command);
        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr window);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessageTimeout(IntPtr window, uint message, IntPtr wParam, IntPtr lParam,
            uint flags, uint timeout, out IntPtr result);
    }
}
