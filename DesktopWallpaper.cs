using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace ChillWallpaper
{
    internal sealed class DesktopWallpaper
    {
        private readonly string statePath;
        private readonly int gamePid;
        private WindowState saved;
        private IntPtr worker;
        private Rectangle appliedBounds;

        internal DesktopWallpaper(int gamePid)
        {
            this.gamePid = gamePid;
            using (var process = Process.GetCurrentProcess())
                statePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "ChillWithYouWallpaper", "window-" + process.SessionId + ".bin");
            if (File.Exists(statePath))
                saved = WindowState.Read(statePath);
        }

        internal bool HasSavedWindow { get { return saved != null; } }
        internal IntPtr WallpaperLayer { get { return worker; } }
        internal IntPtr GameWindow { get { return saved == null ? IntPtr.Zero : saved.Window; } }
        internal IntPtr FindGameWindow() { return Native.FindGameWindow(gamePid); }

        internal void Attach()
        {
            if (HasSavedWindow)
                return;

            IntPtr game = FindGameWindow();
            if (game == IntPtr.Zero)
                throw new InvalidOperationException("请先打开《放松时光》，进入想显示的场景。");
            if (Native.GetParent(game) != IntPtr.Zero)
                throw new InvalidOperationException("游戏窗口已由另一个窗口托管。请先恢复游戏窗口。");

            IntPtr desktop = Native.FindWallpaperLayer();
            if (desktop == IntPtr.Zero)
                throw new InvalidOperationException("当前桌面没有可用的壁纸层。");
            if (Native.IsIconic(game))
                Native.Unminimize(game);

            WindowState original = WindowState.Capture(game);
            original.Write(statePath);
            saved = original;
            worker = desktop;

            try
            {
                int style = (saved.Style & ~(Native.Popup | Native.WindowFrame | Native.Maximized)) |
                    Native.Child | Native.ClipSiblings;
                int extended = (saved.ExtendedStyle & ~(Native.AppWindow | Native.Topmost)) |
                    Native.ToolWindow | Native.NoActivate;
                Native.WriteStyles(game, style, extended);
                Native.Reparent(game, worker);
                FitGameToPrimaryScreen();
                Native.EnableWindow(game, false);
            }
            catch (Exception error)
            {
                try { Restore(); }
                catch (Exception restoreError)
                {
                    throw new InvalidOperationException("进入壁纸模式失败。请从托盘菜单重试「恢复游戏窗口」。",
                        new AggregateException(error, restoreError));
                }
                throw;
            }
        }

        internal void Update()
        {
            if (saved == null)
                return;
            if (!saved.StillExists())
            {
                Forget();
                return;
            }
            if (worker == IntPtr.Zero)
                return;
            if (Native.GetParent(saved.Window) != worker || !Native.IsWindow(worker))
                throw new InvalidOperationException("Windows 桌面层已重建，游戏窗口已恢复。");

            if (appliedBounds != Screen.PrimaryScreen.Bounds)
                FitGameToPrimaryScreen();
        }

        internal void RestoreAndFocus()
        {
            IntPtr game = saved == null ? IntPtr.Zero : saved.Window;
            Restore();
            if (game != IntPtr.Zero && Native.IsWindow(game) && game == FindGameWindow())
                Native.FocusWindow(game);
        }

        internal void Restore()
        {
            if (saved == null)
                return;
            if (saved.StillExists())
            {
                IntPtr game = saved.Window;
                if (Native.GetParent(game) != IntPtr.Zero)
                    Native.Reparent(game, IntPtr.Zero);
                Native.WriteStyles(game, saved.Style, saved.ExtendedStyle);
                Native.EnableWindow(game, (saved.Style & Native.Disabled) == 0);
                IntPtr order = new IntPtr((saved.ExtendedStyle & Native.Topmost) != 0 ? -1 : -2);
                Native.RestorePlacement(game, saved.Placement);
                Native.Position(game, order, saved.Bounds);
            }
            Forget();
        }

        private void FitGameToPrimaryScreen()
        {
            Rectangle screen = Screen.PrimaryScreen.Bounds;
            Rectangle desktop = Native.GetClientBoundsOnScreen(worker);
            if (!desktop.Contains(screen))
                throw new InvalidOperationException("壁纸层目前没有覆盖主显示器。");
            var relativeBounds = new Rectangle(screen.X - desktop.X, screen.Y - desktop.Y,
                screen.Width, screen.Height);
            Native.Position(saved.Window, IntPtr.Zero, relativeBounds);
            appliedBounds = screen;
        }

        private void Forget()
        {
            File.Delete(statePath);
            saved = null;
            worker = IntPtr.Zero;
        }
    }

    internal sealed class WindowState
    {
        private const int FileVersion = 1;
        private readonly int pid;
        private readonly long startTime;
        internal readonly IntPtr Window;
        internal readonly int Style;
        internal readonly int ExtendedStyle;
        internal readonly Rectangle Bounds;
        internal readonly Native.Placement Placement;

        private WindowState(IntPtr window, int pid, long startTime, int style, int extendedStyle,
            Rectangle bounds, Native.Placement placement)
        {
            Window = window;
            this.pid = pid;
            this.startTime = startTime;
            Style = style;
            ExtendedStyle = extendedStyle;
            Bounds = bounds;
            Placement = placement;
        }

        internal static WindowState Capture(IntPtr game)
        {
            int pid = Native.GetPid(game);
            using (var process = Process.GetProcessById(pid))
            {
                return new WindowState(game, pid, process.StartTime.ToFileTimeUtc(),
                    Native.GetWindowLong(game, Native.StyleIndex), Native.GetWindowLong(game, Native.ExtendedStyleIndex),
                    Native.GetBounds(game), Native.ReadPlacement(game));
            }
        }

        internal bool StillExists()
        {
            if (!Native.IsWindow(Window) || Native.GetPid(Window) != pid || !Native.IsGameWindow(Window))
                return false;
            try
            {
                using (var process = Process.GetProcessById(pid))
                    return process.ProcessName.Equals(Native.GameProcessName, StringComparison.OrdinalIgnoreCase) &&
                        process.StartTime.ToFileTimeUtc() == startTime;
            }
            catch (ArgumentException) { return false; }
            catch (InvalidOperationException) { return false; }
        }

        internal static WindowState Read(string path)
        {
            using (var data = new BinaryReader(File.OpenRead(path)))
            {
                if (data.ReadInt32() != FileVersion)
                    throw new InvalidDataException("无法读取上次运行留下的窗口恢复记录。");
                var window = new IntPtr(data.ReadInt64());
                int pid = data.ReadInt32();
                long startTime = data.ReadInt64();
                int style = data.ReadInt32();
                int extendedStyle = data.ReadInt32();
                var bounds = new Rectangle(data.ReadInt32(), data.ReadInt32(), data.ReadInt32(), data.ReadInt32());
                return new WindowState(window, pid, startTime, style, extendedStyle, bounds, Native.Placement.Read(data));
            }
        }

        internal void Write(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temporary = path + ".tmp";
            using (var data = new BinaryWriter(File.Create(temporary)))
            {
                data.Write(FileVersion);
                data.Write(Window.ToInt64());
                data.Write(pid);
                data.Write(startTime);
                data.Write(Style);
                data.Write(ExtendedStyle);
                data.Write(Bounds.X);
                data.Write(Bounds.Y);
                data.Write(Bounds.Width);
                data.Write(Bounds.Height);
                Placement.Write(data);
            }
            File.Move(temporary, path);
        }
    }
}
