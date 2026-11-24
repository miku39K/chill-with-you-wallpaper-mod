using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading;
using System.Windows.Forms;

namespace ChillWallpaper
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            const string title = "放松时光 · 桌面壁纸";
            bool managed = Array.IndexOf(args, "--game-pid") >= 0;
            bool firstInstance;
            using (var instance = new Mutex(true, @"Local\ChillWithYou.DesktopWallpaper", out firstInstance))
            {
                if (!firstInstance)
                {
                    if (!managed)
                        MessageBox.Show("工具已运行，请在任务栏通知区域打开菜单。", title);
                    return;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                try
                {
                    using (var game = managed ? OpenGameProcess(args) : null)
                    {
                        if (managed && game == null)
                            return;
                        using (var tray = new WallpaperTray(title, game))
                        {
                            if (Array.IndexOf(args, "--wallpaper") >= 0)
                                tray.ApplyOnStart();
                            Application.Run(tray);
                        }
                    }
                }
                catch (Exception error)
                {
                    MessageBox.Show(error.Message, title, MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private static Process OpenGameProcess(string[] args)
        {
            int index = Array.IndexOf(args, "--game-pid") + 1;
            int pid;
            if (index >= args.Length || !int.TryParse(args[index], out pid) || pid <= 0)
                return null;

            Process game;
            try { game = Process.GetProcessById(pid); }
            catch (ArgumentException) { return null; }

            try
            {
                using (var current = Process.GetCurrentProcess())
                {
                    if (!game.HasExited && game.SessionId == current.SessionId &&
                        string.Equals(game.ProcessName, Native.GameProcessName, StringComparison.OrdinalIgnoreCase))
                        return game;
                }
            }
            catch (InvalidOperationException) { }
            game.Dispose();
            return null;
        }
    }

    internal sealed class WallpaperTray : ApplicationContext
    {
        private readonly string title;
        private readonly DesktopWallpaper wallpaper;
        private readonly Process game;
        private readonly DesktopInteraction interaction = new DesktopInteraction();
        private readonly NotifyIcon tray;
        private readonly ContextMenuStrip menu = new ContextMenuStrip();
        private readonly ToolStripMenuItem apply;
        private readonly ToolStripMenuItem restore;
        private readonly ToolStripMenuItem desktopClicks;
        private readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = 1000 };
        private int wallpaperStartupTicks;

        internal WallpaperTray(string title, Process game)
        {
            this.title = title;
            this.game = game;
            wallpaper = new DesktopWallpaper(game == null ? 0 : game.Id);
            menu.Items.Add("打开 Steam 游戏", null, (sender, args) => Run(OpenGame));
            apply = new ToolStripMenuItem("设为桌面壁纸", null, (sender, args) => Run(AttachWallpaper));
            restore = new ToolStripMenuItem("恢复游戏窗口", null, (sender, args) => Run(RestoreGame));
            desktopClicks = new ToolStripMenuItem("在桌面点击游戏") { Checked = true, CheckOnClick = true };
            desktopClicks.Click += (sender, args) => Run(UpdateInteraction);
            menu.Items.AddRange(new ToolStripItem[] { apply, restore, desktopClicks, new ToolStripSeparator() });
            menu.Items.Add("退出工具", null, (sender, args) => Run(CloseTray));

            tray = new NotifyIcon
            {
                Icon = System.Drawing.SystemIcons.Application,
                Text = title,
                ContextMenuStrip = menu,
                Visible = true
            };
            tray.MouseDoubleClick += (sender, args) =>
            {
                if (args.Button != MouseButtons.Left)
                    return;
                if (wallpaper.HasSavedWindow)
                    Run(RestoreGame);
                else if (wallpaper.FindGameWindow() == IntPtr.Zero)
                    Run(OpenGame);
                else
                    Run(AttachWallpaper);
            };
            menu.Opening += (sender, args) => UpdateMenu();
            timer.Tick += (sender, args) =>
            {
                if (game != null && game.HasExited)
                {
                    Run(CloseTray);
                    return;
                }
                try { wallpaper.Update(); }
                catch (Exception error)
                {
                    interaction.Stop();
                    try { wallpaper.Restore(); }
                    catch (Exception restoreError)
                    {
                        ShowTip(restoreError.Message, ToolTipIcon.Error);
                        timer.Stop();
                        UpdateMenu();
                        return;
                    }
                    ShowTip(error.Message, ToolTipIcon.Warning);
                }
                if (wallpaperStartupTicks > 0 && --wallpaperStartupTicks <= 55 &&
                    wallpaper.FindGameWindow() != IntPtr.Zero)
                    Run(AttachWallpaper);
                UpdateMenu();
            };

            if (wallpaper.HasSavedWindow)
                Run(wallpaper.Restore);
            timer.Start();
            ShowTip("右键通知区域图标打开菜单。进入游戏场景后，选择「设为桌面壁纸」。");
            UpdateMenu();
        }

        private void AttachWallpaper()
        {
            wallpaperStartupTicks = 0;
            wallpaper.Attach();
            UpdateInteraction();
        }

        internal void ApplyOnStart()
        {
            if (game == null)
                Run(AttachWallpaper);
            else
                wallpaperStartupTicks = 60;
        }

        private void RestoreGame()
        {
            interaction.Stop();
            wallpaper.RestoreAndFocus();
        }

        private void CloseTray()
        {
            interaction.Stop();
            wallpaper.Restore();
            ExitThread();
        }

        private void UpdateInteraction()
        {
            if (!desktopClicks.Checked)
            {
                interaction.Stop();
                return;
            }
            try { interaction.Start(wallpaper.GameWindow, wallpaper.WallpaperLayer); }
            catch
            {
                desktopClicks.Checked = false;
                interaction.Stop();
                throw;
            }
        }

        private void OpenGame()
        {
            if (wallpaper.HasSavedWindow)
            {
                RestoreGame();
                return;
            }

            IntPtr window = wallpaper.FindGameWindow();
            if (window != IntPtr.Zero)
            {
                Native.FocusWindow(window);
                return;
            }
            if (game != null)
                return;

            try
            {
                using (Process.Start(new ProcessStartInfo("steam://run/3548580") { UseShellExecute = true })) { }
                ShowTip("游戏打开后，进入想显示的场景，再选择「设为桌面壁纸」。");
            }
            catch (Win32Exception error)
            {
                throw new InvalidOperationException("无法通过 Steam 打开游戏。请从 Steam 库启动游戏。", error);
            }
        }

        private void Run(Action action)
        {
            try { action(); }
            catch (Exception error)
            {
                MessageBox.Show(error.Message, title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            UpdateMenu();
        }

        private void UpdateMenu()
        {
            if (!wallpaper.HasSavedWindow)
                interaction.Stop();
            apply.Enabled = !wallpaper.HasSavedWindow;
            restore.Enabled = wallpaper.HasSavedWindow;
            tray.Text = wallpaper.HasSavedWindow ? title + "（已启用）" : title;
        }

        private void ShowTip(string message, ToolTipIcon icon = ToolTipIcon.Info)
        {
            tray.ShowBalloonTip(2500, title, message, icon);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                timer.Dispose();
                interaction.Dispose();
                tray.Dispose();
                menu.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
