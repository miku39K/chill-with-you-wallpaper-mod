using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Accessibility;

namespace ChillWallpaper
{
    internal sealed class DesktopInteraction : NativeWindow, IDisposable
    {
        private const int LowLevelMouseHook = 14;
        private const int MouseMove = 0x0200;
        private const int LeftDown = 0x0201;
        private const int LeftUp = 0x0202;
        private const int Activate = 0x0006;
        private const int SetFocus = 0x0007;
        private const int KillFocus = 0x0008;
        private const int DesktopLeftDown = 0x8001;
        private const int DesktopLeftUp = 0x8002;
        private const int ListRole = 0x21;
        private const int ClickDelayMs = 75;
        private const int FocusDelayMs = 100;

        private readonly MouseHook callback;
        private readonly Timer clickTimer = new Timer { Interval = ClickDelayMs };
        private readonly Queue<Point> clicks = new Queue<Point>();
        private IntPtr hook, game, worker, iconView, iconHost, iconList;
        private Point? mouseDown;
        private Point clickPoint;
        private IntPtr gameClick;
        private long focusTime;
        private bool gameFocused, gameButtonDown, waitingToPress;

        internal DesktopInteraction()
        {
            callback = ObserveMouse;
            clickTimer.Tick += AdvanceClick;
        }

        internal void Start(IntPtr gameWindow, IntPtr wallpaperLayer)
        {
            if (hook != IntPtr.Zero)
                return;
            if (gameWindow == IntPtr.Zero || wallpaperLayer == IntPtr.Zero)
                return;

            iconView = Native.FindDesktopIconView(wallpaperLayer);
            if (iconView == IntPtr.Zero)
                throw new InvalidOperationException("未找到桌面图标层，游戏点击暂不可用。");
            iconHost = Native.GetParent(iconView);
            iconList = Native.FindWindowEx(iconView, IntPtr.Zero, "SysListView32", null);
            game = gameWindow;
            worker = wallpaperLayer;
            CreateHandle(new CreateParams { Parent = new IntPtr(-3) });
            hook = SetWindowsHookEx(LowLevelMouseHook, callback, GetModuleHandle(null), 0);
            if (hook == IntPtr.Zero)
            {
                int code = Marshal.GetLastWin32Error();
                Stop();
                throw new Win32Exception(code, "无法开启桌面游戏点击（Windows 错误 " + code + "）。");
            }
        }

        internal void Stop()
        {
            if (hook != IntPtr.Zero)
            {
                UnhookWindowsHookEx(hook);
                hook = IntPtr.Zero;
            }
            clickTimer.Stop();
            clicks.Clear();
            mouseDown = null;
            waitingToPress = false;
            FinishClick();
            EndGameFocus();
            game = IntPtr.Zero;
            if (Handle != IntPtr.Zero)
                DestroyHandle();
        }

        public void Dispose()
        {
            Stop();
            clickTimer.Dispose();
        }

        private IntPtr ObserveMouse(int code, IntPtr message, IntPtr details)
        {
            // Queue only coordinates for left-button events. The hook never consumes desktop input.
            if (code == 0 && (message.ToInt64() == LeftDown || message.ToInt64() == LeftUp))
            {
                int x = Marshal.ReadInt32(details, 0);
                int y = Marshal.ReadInt32(details, 4);
                int notice = message.ToInt64() == LeftDown ? DesktopLeftDown : DesktopLeftUp;
                PostMessage(Handle, notice, new IntPtr(x), new IntPtr(y));
            }
            return CallNextHookEx(IntPtr.Zero, code, message, details);
        }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg != DesktopLeftDown && message.Msg != DesktopLeftUp)
            {
                base.WndProc(ref message);
                return;
            }

            var point = new Point(message.WParam.ToInt32(), message.LParam.ToInt32());
            if (message.Msg == DesktopLeftDown)
            {
                mouseDown = Control.ModifierKeys == Keys.None && IsEmptyDesktopAt(point) ? (Point?)point : null;
                if (mouseDown.HasValue && !gameFocused)
                {
                    FocusGame();
                    PostGameMove(point);
                    Schedule(350);
                }
                return;
            }

            Point? down = mouseDown;
            mouseDown = null;
            if (!down.HasValue || HasMoved(down.Value, point) || clicks.Count >= 4 || !IsEmptyDesktopAt(point))
                return;

            clicks.Enqueue(point);
            if (!gameButtonDown && !waitingToPress)
                StartClick();
        }

        private static bool HasMoved(Point start, Point end)
        {
            return Math.Abs(start.X - end.X) > SystemInformation.DragSize.Width / 2 ||
                Math.Abs(start.Y - end.Y) > SystemInformation.DragSize.Height / 2;
        }

        private bool IsEmptyDesktopAt(Point point)
        {
            if (game == IntPtr.Zero || Native.GetParent(game) != worker || !Native.IsWindowVisible(game) ||
                !Screen.PrimaryScreen.Bounds.Contains(point))
                return false;

            IntPtr hit = WindowFromPoint(point);
            if (hit != IntPtr.Zero && (hit == iconView || hit == iconHost || hit == worker))
                return true;
            if (hit == IntPtr.Zero || hit != iconList)
                return false;

            IAccessible accessible;
            object child;
            if (AccessibleObjectFromPoint(point, out accessible, out child) != 0 || accessible == null)
                return false;
            try
            {
                // The desktop list itself is a blank spot. Icon image and label return list items.
                return child is int && (int)child == 0 && object.Equals(accessible.get_accRole(child), ListRole);
            }
            catch (COMException) { return false; }
            finally { Marshal.ReleaseComObject(accessible); }
        }

        private void StartClick()
        {
            while (clicks.Count != 0)
            {
                Point point = clicks.Dequeue();
                if (HasMoved(point, Cursor.Position) || !IsEmptyDesktopAt(point))
                    continue;
                clickPoint = point;
                if (!ScreenToClient(game, ref point))
                    continue;
                gameClick = PackPoint(point);
                FocusGame();
                int elapsed = (int)((Stopwatch.GetTimestamp() - focusTime) * 1000 / Stopwatch.Frequency);
                if (elapsed < FocusDelayMs)
                {
                    PostMessage(game, MouseMove, IntPtr.Zero, gameClick);
                    waitingToPress = true;
                    Schedule(FocusDelayMs - elapsed);
                }
                else
                    Press();
                return;
            }
        }

        private void FocusGame()
        {
            if (gameFocused)
                return;
            // Unity needs focus notifications to process button events while embedded.
            // The game stays disabled for hit-testing and never takes Windows foreground focus.
            PostMessage(game, Activate, new IntPtr(1), IntPtr.Zero);
            PostMessage(game, SetFocus, IntPtr.Zero, IntPtr.Zero);
            gameFocused = true;
            focusTime = Stopwatch.GetTimestamp();
        }

        private void PostGameMove(Point point)
        {
            if (ScreenToClient(game, ref point))
                PostMessage(game, MouseMove, IntPtr.Zero, PackPoint(point));
        }

        private static IntPtr PackPoint(Point point)
        {
            return new IntPtr((point.X & 0xffff) | ((point.Y & 0xffff) << 16));
        }

        private void AdvanceClick(object sender, EventArgs args)
        {
            clickTimer.Stop();
            if (waitingToPress)
            {
                if (!HasMoved(clickPoint, Cursor.Position) && IsEmptyDesktopAt(clickPoint))
                    Press();
                else
                {
                    waitingToPress = false;
                    EndGameFocus();
                    StartClick();
                }
            }
            else if (gameButtonDown)
            {
                FinishClick();
                StartClick();
                // Give Unity a frame to process the release before reporting lost focus.
                if (!clickTimer.Enabled)
                    Schedule(ClickDelayMs);
            }
            else
                EndGameFocus();
        }

        private void Press()
        {
            waitingToPress = false;
            PostMessage(game, MouseMove, IntPtr.Zero, gameClick);
            PostMessage(game, LeftDown, new IntPtr(1), gameClick);
            gameButtonDown = true;
            Schedule(ClickDelayMs);
        }

        private void Schedule(int milliseconds)
        {
            clickTimer.Stop();
            clickTimer.Interval = milliseconds;
            clickTimer.Start();
        }

        private void FinishClick()
        {
            if (!gameButtonDown)
                return;
            PostMessage(game, LeftUp, IntPtr.Zero, gameClick);
            gameButtonDown = false;
        }

        private void EndGameFocus()
        {
            if (!gameFocused)
                return;
            PostMessage(game, KillFocus, IntPtr.Zero, IntPtr.Zero);
            PostMessage(game, Activate, IntPtr.Zero, IntPtr.Zero);
            gameFocused = false;
        }

        private delegate IntPtr MouseHook(int code, IntPtr message, IntPtr details);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int type, MouseHook callback, IntPtr module, uint thread);
        [DllImport("user32.dll")]
        private static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr details);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr GetModuleHandle(string name);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool PostMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")]
        private static extern IntPtr WindowFromPoint(Point point);
        [DllImport("user32.dll")]
        private static extern bool ScreenToClient(IntPtr window, ref Point point);
        [DllImport("oleacc.dll")]
        private static extern int AccessibleObjectFromPoint(Point point,
            [MarshalAs(UnmanagedType.Interface)] out IAccessible accessible, [MarshalAs(UnmanagedType.Struct)] out object child);
    }
}
