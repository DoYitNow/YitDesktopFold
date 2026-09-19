# YitDesktopFold

YitDesktopFold 是一个面向 Windows 10/11 的轻量原生桌面整理工具。它把桌面项目分类显示在可移动、可缩放的紧凑整理块中，同时保留 Explorer 原生桌面、框选、右键菜单和未归类图标的摆放方式。

> 当前版本：`0.1.1`（早期公开版本）

## 核心能力

- 多个独立整理块，位置、尺寸、名称、内容和布局跨启动保存；每台显示器保留独立的比例布局。
- 显示器临时断开时安全回退到可用屏幕，不覆盖原显示器布局；重新连接后自动恢复。
- 大、中、小、列表四种固定图标布局；调整窗口只重排，不连续缩放图标。
- 桌面与整理块双向拖放，块间移动仅改变分类元数据。
- 普通文件、文件夹、快捷方式及回收站等 Shell 虚拟图标均可归类；虚拟图标归类后会定向刷新 Explorer 桌面并立即隐藏原生副本。
- Explorer 原生桌面继续工作：未归类图标、壁纸框选、桌面右键菜单和 `Win+D` 共存。
- Windows 11 风格的紧凑菜单，并可进入完整 Shell 菜单；回收站和“此电脑”有专门操作。
- 全局透明度、轻量玻璃、黑/白底色、磁吸开关；每块可独立设置名称、图标名称、纯图标模式和布局。
- 设置窗口可逐个恢复或删除隐藏整理块；删除只取消分类并恢复原生桌面显示。
- 设置中的“登录 Windows 时启动”采用保存/取消事务，不在右键菜单中产生即时副作用。
- 原生 .NET 8 WPF 单进程实现，不包含 Electron、Chromium、WebView、广告、遥测或网络服务。

## 数据安全模型

用户桌面与公共桌面目录始终是真实数据源。归类、排序和块间拖动不会复制或移动真实文件；应用只保存项目身份、所属整理块和顺序。为避免一个项目同时出现在 Explorer 与整理块中，已归类项目会使用可逆的 Windows 可见性状态隐藏，且应用会先保存原始状态，只撤销自己拥有的改动。

配置保存在 `%LOCALAPPDATA%\YitDesktopFold`，采用临时文件、原子替换和备份恢复。卸载程序默认保留这些个人配置，方便后续重装；如需彻底清理，可在退出应用并确认桌面图标均已恢复后手动删除该目录。

更完整的边界与故障恢复说明见 [架构文档](docs/ARCHITECTURE.md)。

## 安装

从 GitHub Releases 下载 `YitDesktopFold-Setup-0.1.1-win-x64.exe` 并运行。安装器按当前用户安装到 `%LOCALAPPDATA%\Programs\YitDesktopFold`，不需要管理员权限。

当前公开包尚未代码签名，Windows SmartScreen 可能显示“未知发布者”。请只从本仓库 Releases 下载，并对照发布页提供的 SHA-256。

系统要求：Windows 10 1809 或更高版本、Windows 11，x64 处理器。

## 使用

- 托盘菜单可新建整理块、显示/隐藏全部、打开设置或退出。
- 隐藏的整理块可在设置窗口中单独显示或删除；至少保留一个整理块。
- 把桌面项目拖入整理块即可归类；把块内项目拖回 Explorer 桌面即可取消归类。
- 在图标间空白处拖动整理块；拖动右下角的轻量斜线改变尺寸。
- `Ctrl+Alt+D` 进入键盘操作模式，`Escape` 退出并恢复之前的前台窗口。
- 右键整理块打开设置；“登录 Windows 时启动”位于设置的“应用”区域，保存后才生效。
- 整理块和快捷方式右键菜单在点击菜单外任意位置时立即关闭，不会因桌面窗口不激活而滞留。

## 开发与验证

需要 [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) 和 Node.js 22.13+（CI 使用 Node.js 24）。原生应用位于 `native/`；`src/` 是保留的设计原型及 Sites 交付面。

```powershell
dotnet build .\native\YitDesktopFold.Native.csproj -c Release
dotnet run --project .\native.tests\YitDesktopFold.Native.Tests.csproj -c Release
pnpm install --frozen-lockfile
pnpm run build
pnpm run test:sites
```

安装包使用 Inno Setup 6 构建：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\build-installer.ps1
```

产物输出到 `release/`，中间发布文件位于已忽略的 `artifacts/`。详细发布步骤见 [发布文档](docs/RELEASE.md)。

## 产品边界

本项目专注桌面收纳，不计划加入资讯、云盘、便签、壁纸中心或常驻全局搜索。自动按文件类型接管整个桌面也不在当前范围内：新发现的桌面项目默认保持原生、未归类，必须由用户明确加入整理块。

候选增强项包括“锁定整理块位置/尺寸”和对已有目录的显式文件夹映射；二者都不得改变桌面文件的真实位置或增加常驻后台负担。与腾讯桌面整理的功能取舍见 [产品边界说明](docs/PRODUCT_SCOPE.md)。

## 参与贡献

请先阅读 [CONTRIBUTING.md](CONTRIBUTING.md)。涉及桌面文件、Shell 可见性、注册表或 Explorer 层级的改动必须带回归验证，并保持可逆和最小作用域。

## 许可证

[MIT License](LICENSE)
