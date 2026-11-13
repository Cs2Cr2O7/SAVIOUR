# SAVIOUR

SiNiSistar 2 的鼠标操作插件。游戏没有公开的 Mod 接口，这个插件通过 BepInEx 的 IL2CPP 加载器接入。

当前版本 **1.2.3**。

- 左键：普通攻击，并按鼠标所在的左右让角色转身。
- 侧键：近战魔法剑。
- 右键：魔法弓。箭的飞行方向朝向鼠标。拉弓时原版的扇形瞄准动画仍会播放。
- 移动、跳跃、奔跑仍用游戏里的按键设置。
- 游戏中按 **F8** 可以开关这套鼠标操作。

## 安装

在 Windows 上安装正版 SiNiSistar 2。下面的路径按默认安装位置来写，装在别的盘时换成你的游戏目录。

1. 下载 BepInEx **6.0.0-be.793** 的 IL2CPP 64 位包，不要用页面上标成 Latest 的 5.4，也不要用 6.0.0-pre.2。这款游戏的 IL2CPP 元数据是版本 31，pre.2 读不了。

   https://builds.bepinex.dev/projects/bepinex_be/793/BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.793%2Bdbc82f0.zip

2. 把压缩包解压到游戏目录，让 `winhttp.dll` 和 `SiNiSistar2.exe` 在同一层。默认是：

   `D:\Steam\steamapps\common\SiNiSistar 2`

3. 从 Steam 启动一次游戏，等控制台跑完第一次的互操作程序集生成，进入标题画面后退出。完成后游戏目录里会有 `BepInEx\interop`。

4. 从本仓库的 [Releases](https://github.com/Cs2Cr2O7/SAVIOUR/releases) 下载 `Sini2MouseInput.dll`，放到：

   `游戏目录\BepInEx\plugins\Sini2MouseInput.dll`

5. 再启动游戏。`BepInEx\LogOutput.log` 里出现 `Loading [Sini2 Mouse Input 1.2.3]` 和 `Chainloader startup complete` 就是加载成功。

## 卸载

删掉 `BepInEx\plugins\Sini2MouseInput.dll`。如果不再使用任何 BepInEx 插件，再删掉游戏目录里的 `winhttp.dll`、`doorstop_config.ini`、`.doorstop_version`、`dotnet` 和 `BepInEx`。

## 从源码编译

需要能编译 `net6.0` 的 .NET SDK。游戏至少按上面的步骤成功启动过一次，这样 `BepInEx\interop` 才存在。

```powershell
dotnet build .\MouseInput\MouseInput.csproj -c Release
```

编译结果会直接写到游戏目录的 `BepInEx\plugins`。游戏正在运行时先退出再编译。
