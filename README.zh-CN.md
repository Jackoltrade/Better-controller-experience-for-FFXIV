# Better controller experience for FFXIV

[English](README.md) · [简体中文](README.zh-CN.md) · [日本語](README.ja.md)

给 FFXIV 加上自定义灯光和振动，让手柄体验更爽。自己的技能、敌方音效和镜头震动都能带来更丰富的手感，同时保留游戏原生手柄效果。

**Windows x64 · Dalamud API 15**

## 功能

- **灯光：** RGB 动画、音效联动、下方白灯动画和极限技显示，支持实时预览。
- **战斗振动：** 玩家技能音效联动、Boss 技能、小怪技能和挨打时的振动可分别开关，共用方位与距离设置。
- **画面特效：** 让手柄跟着镜头震动等画面效果一起振动。
- **脚步增强：** 可增强 USB DualSense 的原生脚步振动。

界面支持简体中文、英语和日语，设置自动保存。

## 安装

使用 XIVLauncher 启动游戏，并开启 Dalamud。

1. 输入 `/xlsettings`，打开「实验性（Experimental）」页。
2. 在「自定义插件仓库（Custom Plugin Repositories）」中粘贴以下地址，点击 **+**，勾选启用，再「保存并关闭」：

   ```text
   https://raw.githubusercontent.com/Jackoltrade/Better-controller-experience-for-FFXIV/main/repo.json
   ```

3. 输入 `/xlplugins`，搜索 **Better controller experience for FFXIV**，点击「安装」。
4. 输入 `/bcef` 打开插件设置。

后续更新只需打开 `/xlplugins`，点击「更新插件」。如果之前通过开发插件流程加载过 DLL，请先停用开发版，再从仓库安装。

所有界面语言共用这个仓库。之前使用的 release 仓库地址仍然有效。维护者可查看 [Raw 仓库的设置与更新步骤](docs/RELEASING.md#raw-feed-setup-and-updates)。

## 振动曲线

玩家和敌方音效按声音强弱调整振动，画面特效则会适当放大轻微变化。**画面特效曲线体现了对较弱效果的刻意增强。** 曲线展示的是数字信号目标值，不是手柄实际振动力度的测量结果。

![默认设置下的振动响应曲线](docs/assets/haptics-curves.svg)

[**打开交互曲线 →**](https://jackoltrade.github.io/Better-controller-experience-for-FFXIV/)

选一条曲线，拖动下方滑块，就能看到设置如何影响振动；悬停或点击图表可查看具体数值。页面为英文，直接在浏览器中使用，无需下载，也不会改动插件设置。

## 支持的手柄

| 连接方式 | 灯光 | 额外振动 |
| --- | --- | --- |
| DualSense／DualSense Edge，USB | RGB 与下方白灯 | 触觉反馈、战斗、画面特效和脚步增强 |
| DualSense／DualSense Edge，蓝牙 | RGB 与下方白灯 | 战斗与画面特效的普通振动 |
| 兼容 Xbox 的 XInput 手柄 | — | 战斗与画面特效的普通振动 |

DualSense 触觉反馈和手柄扬声器效果需要 **USB** 连接，并在 FFXIV 中开启手柄振动。仅支持 DirectInput 的设备需要 XInput 映射软件。

## 命令与宏

| 命令 | 功能 |
| --- | --- |
| `/bcef` | 打开设置 |
| `/bcef help` | 查看命令 |
| `/bcef info` | 打开使用说明 |
| `/bcef trace` | 开始或停止诊断记录（仅开发插件加载） |

聊天或宏中可使用 `/bcef <功能名> on`、`off` 或 `toggle`（开启、关闭、切换）：

```text
/bcef haptics on
/bcef combathaptics toggle
/bcef lighting off
```

功能名：`lighting`、`haptics`、`combathaptics`、`playersfx`、`footsteps`、`spatialhaptics`、`distancefade`、`followimpact`。

**请将旧宏中的 `/ds5` 改为 `/bcef`。** 命令修改会自动保存。

## 帮助与反馈

普通安装包不包含诊断模块。诊断标签页和 `/bcef trace` 需要使用[开发版](docs/RELEASING.md#development-builds)，并通过 Dalamud 的开发者入口加载。普通安装遇到问题，可以从「关于 → GitHub」反馈；开发安装可先查看「诊断」。排查漏反应或乱振时，在效果出现前输入 `/bcef trace`，复现后再输一次停止记录。记录只包含诊断信息，不会录制音频或自动上传，分享前记得检查内容。诊断页可以查看容量、文件位置，并在确认后删除记录。

兼容性可能随客户端、补丁和手柄而变化。详见[使用指南](docs/USER-GUIDE.zh-CN.md)、[覆盖范围](docs/ENEMY-HAPTICS-COVERAGE.md)和 [QA 报告](docs/QA-REPORT.md)。

[提交问题](https://github.com/Jackoltrade/Better-controller-experience-for-FFXIV/issues/new)时，请附插件／客户端版本、手柄型号、连接方式和复现步骤。音效归类问题可附一段简短记录。

插件完全免费，放心用。[Ko-fi](https://ko-fi.com/K7C122MEWU) · [爱发电](https://afdian.com/a/jackoltrade)

## 开发与许可

[构建与发布](docs/RELEASING.md) · [官方上架](docs/OFFICIAL-LISTING.md) · [开发说明](docs/DEVELOPMENT-NOTES.md) · [更新记录](CHANGELOG.md)

采用 **GPL-3.0-only** 许可，基于 Dalamud，并参考 DS4Windows、FFXIVClientStructs 和 LuminaSupplemental。详见[许可证](LICENSE.txt)、[第三方声明](THIRD-PARTY-NOTICES.txt)和[数据来源](data/README.md)。这是独立社区项目，与 Sony 或 Square Enix 无隶属关系。
