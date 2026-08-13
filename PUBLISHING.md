# 发布指南（Publishing Guide）

TokenViewerExtension 的完整发布渠道与步骤。官方依据：
[微软文档 · 发布命令面板扩展](https://learn.microsoft.com/zh-cn/windows/powertoys/command-palette/publish-extension)

## 渠道总览

| 渠道 | 成本 | 自动更新 | 用户发现方式 | 门槛 | 建议 |
|------|------|---------|-------------|------|------|
| **A. GitHub Releases** | 免费 | ❌（手动） | 你的仓库/README | 最低 | ⭐ 首发首选 |
| **B. WinGet** | 免费 | ✅ | 命令面板 `Search WinGet` | 中（需 EXE 安装器） | ⭐ 推荐 |
| **C. Microsoft Store** | 个人免费 | ✅ | Store 应用商店 | 高（审核/签名/隐私政策） | 覆盖最广 |
| **D. 扩展图库** | 免费 | ✅（随渠道） | 命令面板内置图库 | 低（PR 审核） | 必做（锦上添花） |

> 官方建议：WinGet 是命令面板扩展的推荐分发方式；Store 覆盖最广；图库是"精选目录"，只链接到 WinGet/Store 的安装源，不托管包本身。

---

## 0. 发布前共同准备

1. **版本号**：`TokenViewerExtension.csproj` → `Package.appxmanifest` 中 `Version="0.0.1.0"`（发布新版本时递增）
2. **签名证书**（所有 MSIX 分发必需）：
   - 个人测试：Visual Studio → 项目属性 → 签名 → 创建测试证书
   - 正式发布：商业代码签名证书（如 Sectigo、DigiCert），或 Store 提交（Store 自动签名）
3. **图标资产**：`Assets/` 已备齐（含 Store 需要的 SmallTile 71×71、LargeTile 310×310、StoreLogo 50×50 等）
4. **隐私政策**（Store 强制要求）：扩展会读取用户提供的 API Key / 平台 Token 与账户用量数据（仅本机使用），需准备隐私政策页面（如 GitHub Pages）
5. **确认无个人信息**：`.gitignore` 已排除 bin/obj/.vs/本地 json；发布前执行
   ```powershell
   git status   # 确认无 settings.json / usage.json / bin / obj
   ```

---

## 方案 A：GitHub Releases（最快，首选首发）

1. 代码推送到 GitHub 仓库
2. 本地生成 MSIX 包：
   ```powershell
   dotnet build --configuration Release -p:GenerateAppxPackageOnBuild=true -p:Platform=x64 -p:AppxPackageDir="AppPackages\x64\"
   dotnet build --configuration Release -p:GenerateAppxPackageOnBuild=true -p:Platform=ARM64 -p:AppxPackageDir="AppPackages\ARM64\"
   dir AppPackages -Recurse -Filter "*.msix"
   ```
3. 在 GitHub 创建 Release，上传 x64/arm64 的 .msix 作为资产
4. README 提供安装说明：
   ```powershell
   # 开发者模式 + 信任证书后（或双击 msix）
   Add-AppxPackage -Path .\TokenViewerExtension_0.0.1.0_x64.msix
   # 或 winget 安装（见方案 B）
   ```

---

## 方案 B：WinGet（官方推荐）

要求：GitHub CLI、[wingetcreate](https://github.com/microsoft/wingetcreate)（`winget install Microsoft.WingetCreate`）

### 1. 生成 EXE 安装器（官方模板路线）
微软文档提供了 Inno Setup 模板（`setup-template.iss` + `build-exe.ps1`）：
- [发布到 WinGet 完整指南](https://learn.microsoft.com/zh-cn/windows/powertoys/command-palette/publish-extension-winget)
- 核心：`dotnet publish --runtime win-x64 --self-contained true` → Inno Setup 打包 → 注册表写入
  `HKCU\SOFTWARE\Classes\CLSID\{你的CLSID}\LocalServer32` → `exe -RegisterProcessAsComServer`
- CLSID 即 `TokenViewerExtension.cs` 中 `[Guid("24e5fecd-...")]` 的值

### 2. 提交清单（首次手动）
```powershell
wingetcreate new "<GitHub Release 的 x64.exe 地址>" "<arm64.exe 地址>"
# 按提示回车；最后选择 Yes 提交到 microsoft/winget-pkgs（自动开 PR）
```
清单必须包含（命令面板发现机制）：
```yaml
# .locale.*.yaml
Tags:
- windows-commandpalette-extension
```
```yaml
# .installer.yaml
Dependencies:
  PackageDependencies:
  - PackageIdentifier: Microsoft.WindowsAppRuntime.#.#
```

### 3. 后续版本自动更新
参考官方 `update-winget.yml` GitHub Actions 工作流（release 触发 → wingetcreate update --submit）。

---

## 方案 C：Microsoft Store（覆盖最广）

官方指南：[发布到 Microsoft Store](https://learn.microsoft.com/zh-cn/windows/powertoys/command-palette/publish-extension-store)

1. 注册 [Microsoft 合作伙伴中心](https://partner.microsoft.com/dashboard/home)（个人免费）→ 应用和游戏 → + 新产品 → MSIX 或 PWA 应用 → 保留产品名称
2. 从"产品标识"页复制：Package/Identity/Name、Publisher、PublisherDisplayName
3. 用这些值更新 `Package.appxmanifest` 的 `<Identity>` 与 `<Properties>`；在 csproj 添加：
   ```xml
   <AppxPackageIdentityName>…</AppxPackageIdentityName>
   <AppxPackagePublisher>…</AppxPackagePublisher>
   <AppxPackageVersion>0.0.1.0</AppxPackageVersion>
   ```
4. 生成 x64 + ARM64 的 MSIX 并合并为 bundle：
   ```powershell
   makeappx bundle /f bundle_mapping.txt /p TokenViewerExtension_0.0.1.0_Bundle.msixbundle
   ```
   （bundle_mapping.txt 列出两个 msix 的相对路径；makeappx 位于 Windows SDK 中）
5. 上传 bundle，完成提交：
   - 描述中注明 `TokenViewerExtension integrates with the Windows Command Palette to…`
   - 补充信息 → 其他测试信息：写明**需要先安装 PowerToys 与命令面板**
   - **隐私政策**：必填（说明 API Key/Token 仅存本机）
6. 提交后等待认证（数小时~数天）

> ⚠️ **本项目特有风险**：扩展的"DeepSeek 官方平台直连"功能调用的是**未公开内部接口**，微软认证可能质询。
> 建议：Store 版本默认关闭该功能（或将设置项标注为实验性），代理模式与余额查询均为官方接口，不受影响。

---

## 方案 D：扩展图库（CmdPal-Extensions）

官方指南：[扩展图库](https://learn.microsoft.com/zh-cn/windows/powertoys/command-palette/extension-gallery)
仓库：[microsoft/CmdPal-Extensions](https://github.com/microsoft/CmdPal-Extensions)

1. 确保扩展已在 WinGet 或 Store 上线（图库只做链接，不托管包）
2. Fork 仓库 → 添加 `extension.json`（名称、图标、安装源标识）
3. 发起 PR → CI 自动验证 + 团队审核
4. 合并后，PowerToys 0.100+ 用户可在命令面板内置图库中直接浏览安装

---

## 推荐路径（时间线）

```
第 1 周：GitHub 仓库 + Releases v0.0.1（方案 A）→ 获取首批用户反馈
第 2 周：WinGet 上线（方案 B）+ 图库 PR（方案 D）→ 命令面板内可发现
第 3 周+：Microsoft Store（方案 C，需先定隐私政策与内部接口处理策略）
```

## 从零发布到 GitHub（实操步骤）

### 第 1 步：安装 GitHub CLI 并登录（一次性）

```powershell
winget install --id GitHub.cli          # 安装 gh
gh auth login                            # 选 GitHub.com → HTTPS → 浏览器登录
```

### 第 2 步：配置 git 身份（一次性）

```powershell
git config --global user.name "你的名字"
git config --global user.email "你的邮箱"
```

### 第 3 步：本地初始化并提交

```powershell
cd D:\TokenViewerExtension\TokenViewerExtension
git init -b main
git add -A
git status          # 合规检查：应只有 49 个左右文件，无 bin/obj/.vs/settings.json/usage.json
git commit -m "Initial release: DeepSeek token usage monitor for PowerToys Command Palette"
```

### 第 4 步：创建 GitHub 仓库并推送

```powershell
# 公开仓库（开源）：
gh repo create TokenViewerExtension --public --source=. --push
# 或私有仓库（先内测）：
gh repo create TokenViewerExtension --private --source=. --push
```

### 第 5 步：打标签触发自动构建发布

```powershell
git tag v0.0.1
git push origin v0.0.1
```

推送标签后，仓库自带的 `.github/workflows/release.yml` 会在 GitHub Actions 上自动：
1. 构建 x64 / arm64 两个 MSIX 包
2. 创建 GitHub Release（标题 v0.0.1）并附带两个包

也可以在 Actions 页手动运行该工作流（只构建不上传 Release）。

### 第 6 步：验证发布

- Actions 页：两个构建任务绿色通过
- Releases 页：v0.0.1 含两个 .msix 资产
- 按 README「用户安装」验证：开发者模式下 `Add-AppxPackage` 安装 → 命令面板 Reload 生效

### 合规检查清单（发布前）

- [ ] `git status` 无 bin/obj/.vs/本地 settings.json/usage.json
- [ ] LICENSE（MIT）已在仓库根目录
- [ ] THIRD-PARTY-NOTICES.md 已声明全部依赖
- [ ] README 注明与微软/DeepSeek 无隶属关系
- [ ] 仓库 Settings 中确认无真实密钥泄露（可安装 secret scanning 提醒）

---
## 版本发布清单（每次发版）


- [ ] 递增 csproj / appxmanifest 版本号
- [ ] `git status` 无本地敏感文件
- [ ] Debug + Release 构建通过
- [ ] GitHub Release 上传 x64 + ARM64 包
- [ ] wingetcreate update（若已上线 WinGet）
- [ ] 更新图库 extension.json（若版本信息变化）
