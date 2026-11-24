using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using BepInEx;

namespace ChillWallpaper.Mod
{
    [BepInPlugin("chillwithyou.desktopwallpaper", "Chill With You Desktop Wallpaper", "1.0.0")]
    [BepInProcess("Chill With You.exe")]
    public sealed class WallpaperPlugin : BaseUnityPlugin
    {
        private void Awake()
        {
            bool apply = Config.Bind("Wallpaper", "ApplyOnGameStart", false,
                "Automatically put the game window on the desktop when it is ready.").Value;
            string folder = Path.GetDirectoryName(typeof(WallpaperPlugin).Assembly.Location);
            string executable = Path.Combine(folder, "ChillWithYouWallpaper.exe");
            if (!File.Exists(executable))
            {
                Logger.LogError("ChillWithYouWallpaper.exe must be in the same folder as the mod DLL.");
                return;
            }

            try
            {
                using (var game = Process.GetCurrentProcess())
                {
                    string arguments = "--game-pid " + game.Id.ToString(CultureInfo.InvariantCulture);
                    if (apply)
                        arguments += " --wallpaper";
                    using (var helper = Process.Start(new ProcessStartInfo(executable, arguments)
                    {
                        WorkingDirectory = folder,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }))
                    {
                        if (helper == null)
                        {
                            Logger.LogError("Windows did not start the wallpaper tray.");
                            return;
                        }
                        Logger.LogInfo("Started wallpaper tray for game PID " + game.Id +
                            " (tray PID " + helper.Id + ", apply on launch: " + apply + ").");
                    }
                }
            }
            catch (Exception error)
            {
                Logger.LogError("Could not start the wallpaper tray: " + error);
            }
        }
    }
}
