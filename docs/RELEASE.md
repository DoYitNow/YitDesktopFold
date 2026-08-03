# 发布流程

## 版本与前置条件

- .NET 8 SDK
- Node.js 22.13+（推荐 Node.js 24）
- Inno Setup 6（`ISCC.exe` 在 `PATH` 或 `%LOCALAPPDATA%\Programs\Inno Setup 6`）
- GitHub CLI（发布 Release 时使用）

版本号需要同步更新：

- `native/YitDesktopFold.Native.csproj`
- `installer/YitDesktopFold.iss`
- `package.json` 与 `pnpm-lock.yaml`
- 发布说明文件名与内容

## 发布检查

```powershell
dotnet run --project .\native.tests\YitDesktopFold.Native.Tests.csproj -c Release
pnpm install --frozen-lockfile
pnpm run build
pnpm run test:sites
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\build-installer.ps1
```

对安装包执行安装/卸载冒烟测试，并记录：

- 安装程序退出码
- 主程序 `FileVersion`
- 安装包大小
- SHA-256
- Authenticode 状态

## 签名

`scripts/build-installer.ps1` 不注入证书，也不读取仓库中的密钥。正式签名应在受保护的 CI 或独立签名环境执行；证书文件与密码不得提交到仓库。未签名构建必须在发布说明中明确标注。

## GitHub Release

安装包不提交到 Git 历史，上传到 GitHub Releases：

```powershell
gh release create v0.1.0 `
  .\release\YitDesktopFold-Setup-0.1.0-win-x64.exe `
  --title "YitDesktopFold 0.1.0" `
  --notes-file .\docs\releases\v0.1.0.md
```

发布后从 Release 页面重新下载并校验 SHA-256，确认资源可公开访问。
