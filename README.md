# 放松时光 · 交互壁纸 Mod

把 Steam 版《放松时光：与你共享Lo-Fi故事》的实时画面放到 Windows 主显示器的桌面图标后面，并直接在桌面上点击游戏按钮。Mod 随游戏加载，托盘组件随游戏退出。

[下载可安装的 Mod](https://github.com/miku39K/chill-with-you-wallpaper-mod/releases/latest)

## 安装

1. 在 Steam 游戏库中右键游戏，选择「管理 → 浏览本地文件」，找到 `Chill With You.exe` 所在目录。
2. 首次安装时，将 BepInEx 5 的 Windows x64 版本解压到游戏目录。 [BepInEx 5.4.23.6 官方下载](https://github.com/BepInEx/BepInEx/releases/tag/v5.4.23.6)
3. 解压 Release 里的 `ChillWithYouWallpaper-Mod-v1.0.0.zip`，将包内的 `BepInEx` 文件夹合并到游戏目录，照常从 Steam 启动游戏。

## 使用

- 进入想显示的游戏场景，从 Windows 任务栏通知区域找到「放松时光 · 桌面壁纸」，右键选择「设为桌面壁纸」。
- 回到桌面，用左键点击露出来的游戏按钮。桌面图标与游戏按钮重叠时，点击优先交给桌面图标。
- 想继续操作普通窗口，在托盘菜单选择「恢复游戏窗口」。
- 想让游戏启动后自动进入壁纸模式，在首次启动生成的 `BepInEx/config/chillwithyou.desktopwallpaper.cfg` 中设置 `ApplyOnGameStart = true`。默认由玩家手动开启。

卸载时，退出游戏并移除 `BepInEx/plugins/ChillWithYouWallpaper`。

## 构建

在 Windows PowerShell 中运行下面的命令，把 `$gameRoot` 指向已安装 BepInEx 5 的游戏目录。

```powershell
.\build-mod.ps1 -GameRoot $gameRoot -BepInExRoot $gameRoot
```

默认产物在 `mod-build`。将 `ChillWithYouWallpaper.Plugin.dll` 和 `ChillWithYouWallpaper.exe` 放到一起，安装在 `BepInEx/plugins/ChillWithYouWallpaper` 下。

`Mod/WallpaperPlugin.cs` 是游戏启动时由 BepInEx 加载的入口。托盘组件用 Win32 API 将游戏窗口放到桌面壁纸层，并把桌面空白处的左键点击传给游戏。

已验证环境为 Windows 10 22H2 x64、Steam 游戏构建 26586283 和 BepInEx 5.4.23.6。
