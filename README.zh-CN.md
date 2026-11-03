# PS5 DualSense Controller for FFXIV

[English](README.md) · [简体中文](README.zh-CN.md) · [日本語](README.ja.md)

一款基于 Dalamud 的 FFXIV 插件，为 **DualSense** 和 **DualSense Edge** 提供自定义灯光与音效驱动的振动反馈。

**公开测试中 · Windows x64 · 仅支持 USB 连接 · Dalamud API 15**

[项目源码](https://github.com/Jackoltrade/PS5-DualSense-Controller-for-FFXIV-) · [下载版本](https://github.com/Jackoltrade/PS5-DualSense-Controller-for-FFXIV-/releases) · [提交问题](https://github.com/Jackoltrade/PS5-DualSense-Controller-for-FFXIV-/issues)

![手柄预览示意图](src/DualSenseLighting/Assets/dualsense-preview.png)

## 可以做什么

- **RGB 灯带：** 常亮、彩虹、呼吸、双色渐变、极光、火焰、心跳、彗星、彩虹脉冲，以及两种音效响应模式。
- **下方 LED：** 设置触摸板下方五颗白灯的点亮数量和动画，也可以用来显示极限技状态。
- **战斗振动：** 将自己角色的技能和武器音效送入游戏原有的 DualSense 振动处理，让战斗更有手感。
- **方位反馈：** 结合左右握把的强弱与距离衰减，表现音效相对角色的位置；左右方向跟随镜头朝向。
- **原生脚步增强：** 可单独放大游戏已有的脚步振动，坐骑振动保持原有强度。
- **实时预览与强度条：** 预览和手柄使用同一套灯光计算，灵敏度设置下方还可以直接观察音效响应。
- **中、英、日三语：** 在标签页上方切换，语言选择会自动保存。

切换到其他应用时，自定义灯光会熄灭一次，随后释放灯光控制；额外添加的战斗和脚步振动也会暂停。手柄扬声器和自适应扳机等原生效果保留。回到游戏或重新连接手柄后，设置会恢复。

## 运行要求与兼容性

- Windows x64、运行 **Dalamud API 15** 的 FFXIV，以及通过 USB 连接的索尼 DualSense 或 DualSense Edge。
- 使用振动功能前，请在游戏中开启手柄振动。手柄扬声器等功能仍需按照游戏原有的 DualSense 设置启用。
- 当前不支持蓝牙，也不提供虚拟手柄或按键映射。

这是面向自定义仓库的**公开测试版**，并未经过 Dalamud 官方插件仓库审核。原生音频和镜头绑定曾在 XIVLauncherCN 的 `2026.10.06.0000.0000` 客户端上验证；其他地区客户端及后续补丁仍需测试。不兼容的音频布局会暂停相关功能，并在「诊断」中显示原因。

## 安装公开测试版

维护者发布首个版本后，GitHub Releases 才会提供下载。如果发布页还是空的，可以先按照下方的[源码构建](#从源码构建)说明操作。

### 通过 Dalamud 插件安装器

1. 打开 `/xlsettings` →「实验性」→「自定义插件仓库」。
2. 首个公开版本发布后，添加以下仓库地址：
   ```text
   https://github.com/Jackoltrade/PS5-DualSense-Controller-for-FFXIV-/releases/download/plugin-repository/pluginmaster.json
   ```
3. 在 Dalamud 中启用测试插件，然后在 `/xlplugins` 搜索 **PS5 DualSense Controller for FFXIV**，安装测试版。
4. 用 USB 连接手柄，输入 `/ds5`。

此仓库只提供**测试版本**，不会把当前构建标记为稳定版。

### 手动测试

1. 从[版本发布页](https://github.com/Jackoltrade/PS5-DualSense-Controller-for-FFXIV-/releases)下载 `latest.zip`。
2. 将整个 ZIP 解压到一个独立文件夹。
3. 打开 `/xlsettings` →「实验性」→「开发插件位置」，添加该文件夹中的 `DualSenseLighting.dll`。
4. 在 `/xlplugins` → Dev Tools 中启用本插件。

加载前请先停用旧版 **Steam Lighting Guard**。同一时间只保留一种安装方式；改用正式安装器时，请移除原来的开发插件条目。

插件改名后，DLL 和内部标识仍为 `DualSenseLighting`，以兼容已有配置与开发插件路径。

## 设置说明

输入 **`/ds5`** 打开设置。将鼠标悬停在控件或旁边的 **(?)** 上即可查看说明。灰色控件表示需要先启用相应的上级功能，原来的数值仍会保留。

首次打开设置时，会显示一份简短的使用须知，介绍支持的手柄、USB 连接要求和切换应用后的行为。之后可随时点击左上角的**使用说明**再次查看，也可以在说明中切换语言。关闭后不会在重新加载或重置标签页设置时重复弹出。

### 聊天命令

`/ds5` 打开设置，`/ds5 help` 查看命令，`/ds5 info` 打开使用说明，`/ds5 trace` 记录 15 秒诊断数据。

使用 `/ds5 <功能名> on`、`off` 或 `toggle`，可直接开启、关闭或切换功能，不会打开设置窗口。例如：`/ds5 combathaptics on`。

| 功能名 | 控制内容 |
| --- | --- |
| `lighting` | 自定义灯光 |
| `combathaptics` | 额外的角色战斗振动 |
| `footsteps` | 原生脚步振动增强 |
| `spatialhaptics` | 方位振动 |
| `distancefade` | 距离衰减 |
| `followimpact` | 跟随技能命中方向 |

命令不区分大小写，修改会自动保存，也可用于游戏宏。功能已开启时，再次输入 `on` 不会重复播放提示振动。子功能仍受上级开关控制，需要开启上级功能时会在聊天栏提示。`footsteps off` 只恢复原生脚步振动强度，不会将其静音。

### 灯光

可以选择灯带效果、颜色、亮度和循环时间。左右灯带共享同一种 RGB 颜色；手柄没有提供可独立寻址的灯珠接口，因此无法实现沿灯带移动的空间波浪效果。

| 音效模式 | 效果 |
| --- | --- |
| 音量响应 | 音效越响，所选颜色越亮；安静时逐渐熄灭。 |
| 音效双色渐变 | 随音量从「低音量颜色」渐变到「高音量颜色」，安静时保留低音量颜色。 |

「仅自己的角色」跟随自己的动作和武器音效；「所有战斗音效」也会响应附近玩家和敌人的声音。此选项只影响灯光，额外战斗振动始终以自己的角色为来源。

使用技能，观察**音效灵敏度**下方的强度条来调节数值。经常满格就调低，较轻的技能几乎没有反应就调高。自动过滤会随灵敏度调整，忽略过小的声音；也可以在高级设置中自行设定阈值。这些选项都不会改变游戏音量。

**下方 LED** 可以选择点亮数量和动画。「追逐」「闪烁」「加载」只使用所选灯位；「往返」「随机闪光」可以在五个灯位间变化，数量决定同时点亮几颗；「心跳」让所选数量的灯一起跳动。极限技模式在 PvE 显示队伍已蓄满的 LB 格数，在 PvP 显示自己极限技的充能，使用 LB 时会快速闪烁。

### 振动

开启战斗振动时，手柄会短振两下作为提示，即使角色没有使用技能也能感受到。调整强度或切回游戏不会重复提示。

首次安装采用以下默认设置：

| 设置 | 默认值 |
| --- | --- |
| 角色战斗振动 | 开启，强度 80% |
| 方位振动 | 开启 |
| 跟随技能命中方向 | 开启，偏向程度 100% |
| 距离计算起点 | 角色 |
| 距离衰减 | 推荐范围：3 米内保持全强度，25 米处降为零 |
| 原生脚步增强 | 关闭，预存倍率 150% |

**方位振动**以角色位置和镜头朝向区分左右。**跟随技能命中方向**会将角色身边的声音稍微偏向自己技能最近的命中位置。偏向程度只改变方向，不会把实际音源移走，也不会改变其距离。

以角色为起点时，距离衰减不受镜头缩放影响。也可以单独选择镜头距离；推荐范围会加上镜头到角色的距离。手动范围位于距离高级设置中。不同职业与技能的音效混音不同，振动手感也会有所差异。

**原生脚步增强**只放大游戏本来就送入脚步振动的声音，独立于战斗振动运行。骑乘或切换到其他应用时暂停，倍率范围为 100–600%；100% 保持原生强度。它不会放大扬声器音量或坐骑振动。

点击**恢复默认设置**后，需要再次确认。只恢复当前的「灯光」或「振动」标签页；取消、关闭、按 Escape 或点击弹窗外都不会修改设置。语言选择与另一个标签页保持不变，升级插件也会保留已有设置。

### 诊断与反馈

「诊断」集中显示手柄连接与前后台状态、输出错误、音效归属计数、左右振动强度，以及极限技检测结果。

需要记录某个难以判断的音效时：

1. 先启用音效灯光或战斗振动。
2. 输入 **`/ds5 trace`**，或在「诊断」点击「记录音效诊断（15 秒）」。
3. 在倒计时内使用对应技能，提示记录完成后即可停止。

记录会覆盖 DLL 旁的 `sfx-trace.json`，包含音效名称、归属信息、位置、路由判断和本地进程地址，**不会录制音频**，也不会自动上传。公开提交记录前，请先确认其中的信息是否适合分享。

可以[发送反馈邮件](mailto:jackzhou.work@protonmail.com?subject=%5BPS5%20DualSense%20Controller%20for%20FFXIV%5D%20Feedback)，或在 [GitHub 提交问题](https://github.com/Jackoltrade/PS5-DualSense-Controller-for-FFXIV-/issues)。请附上插件版本、游戏版本及地区、手柄型号、连接方式和复现步骤。「关于」中的邮件按钮会自动填写带有插件名称的固定主题，由你自行发送邮件。

## 常见问题

**切出游戏后灯光熄灭了。** 这是预期的控制权交接。其他软件下次发送灯光指令时就可以接管；插件无法强制 Steam 等软件重新应用它们保存的设置。

**原生振动正常，但没有战斗振动。** 请确认 USB 连接、角色战斗振动已开启、强度不为零，再查看「诊断」。远处的命中音效可能超出衰减范围；开启方位距离衰减时，位置未知的声音会跳过。原生振动与额外技能振动是两条独立路径。

**动画看起来不动。** 确认自定义灯光已启用，且 FFXIV 在前台。「追逐」至少需要两颗 LED；「往返」选择五颗时会铺满灯条，因此看不出移动。

**能和 Steam、DS4Windows 等软件同时使用吗？** 插件使用共享句柄，切到后台后会释放灯光控制。游戏在前台时，其他工具仍可能发出自己的指令。最简单的使用方式是让本插件负责游戏前台的灯光，并保留游戏原生 DualSense 支持。

## 关于与支持

最右侧的「关于」标签页提供版本、作者、许可、兼容信息和致谢，文字居中，按钮带有对应图标。两个支持按钮并排显示，邮件与 GitHub 反馈按钮也并排放在同一行。GitHub 反馈会打开新建议题页面，由你填写并提交。喜欢这个插件？请我喝杯奶茶吧。可以通过 [Ko-fi 支持项目](https://ko-fi.com/K7C122MEWU)。**爱发电按钮将在维护者提供链接后开放。** 此插件完全免费。

## 从源码构建

准备 **.NET 10 SDK** 和兼容的 Dalamud API 15 库，在仓库根目录运行：

```powershell
./tools/build.ps1 -DalamudLibPath 'C:\path\to\Dalamud'
```

脚本可以自动查找本机 XIVLauncher / XIVLauncherCN 的库，也支持 `DALAMUD_HOME`。本项目的 WSL 开发环境还可以使用已有 Windows 运行时和本地 Roslyn 编译器：

```bash
python3 tools/build-wsl.py
```

两种方式都会生成优化的 x64 构建，运行控制器、核心逻辑和翻译检查，校验 DLL 名称、版本及 API，再发布到 `artifacts/plugin/`。离线编译器仅用于构建，不会随插件分发。`-Probe` / `--probe` 可只读检查 USB 手柄能力，不发送控制指令。

```bash
python3 -m unittest discover -s tests -p 'test_*.py'
python3 tools/package-release.py
```

打包结果位于 `artifacts/release/`，包括 `latest.zip`、仅供测试的 `pluginmaster.json`、校验和与发布摘要。用户配置、诊断记录、Dalamud 库和编译器依赖不会进入发布包。

[维护者发布说明](docs/RELEASING.md) · [QA 报告](docs/QA-REPORT.md) · [技术开发记录](docs/DEVELOPMENT-NOTES.md)

## 许可与致谢

本项目采用 **GPL-3.0-only**，见 [LICENSE.txt](LICENSE.txt)。手柄实现参考了 [DS4Windows](https://github.com/hbashton/DS4Windows)，原生音频与镜头绑定参考了 [FFXIVClientStructs](https://github.com/aers/FFXIVClientStructs)。完整署名见 [THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt)，相关 MIT 许可也随包提供。插件运行于 [Dalamud](https://github.com/goatcorp/Dalamud)。

本项目由社区独立开发，与索尼或 Square Enix 无隶属关系。
