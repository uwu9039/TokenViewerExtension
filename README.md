# Token 用量监控（TokenViewerExtension）

基于 PowerToys 命令面板（Command Palette）的 **DeepSeek Token 消耗监控**扩展（已精简为仅支持 DeepSeek）。

三个模块：
- **Token 用量监控**（📊）：总览 —— 今日总计（官方数据优先）、用量详情、官方用量、账户、诊断入口
- **账户监控**（💳）：官方余额 + 今日用量 + 官方图表，仅需 API Key、无需代理
- **用量诊断**（🔍）：一键检查配置与连接，定位"为什么没有数据"

## 数据来源（重要）

token 消耗只有两个来源：

### ① 官方平台数据（推荐，无需代理）
DeepSeek **API Key 查不到用量**，必须配置**官方平台 Token**（网页登录凭证）：

1. 浏览器登录 [platform.deepseek.com](https://platform.deepseek.com)
2. 按 **F12** 打开开发者工具 → 切到 **Console（控制台）**
3. 输入 `JSON.parse(localStorage.getItem('userToken')).value` 后回车
4. 复制返回的 `eyJ...` 开头的字符串（JWT）
5. 粘贴到扩展设置 →「DeepSeek 官方平台 Token」

配置后，总览/详情/账户监控自动并入官方数据（按天/按模型/消费金额/图表），约 5 分钟延迟。图表页与诊断页内置同样指引。
> ⚠️ Token 有效期数天，过期后需重新获取；请勿泄露。

### ② 本地代理（实时，仅适用于前台 GUI 客户端）
扩展在 `127.0.0.1:{端口}` 监听代理，客户端 Base URL 指向它即可实时记账：
```
http://127.0.0.1:8788/v1
```
**不适用于后台 agent**（如 ZCode）：扩展进程由命令面板托管，面板未打开时代理不运行，agent 请求会被拒绝。

## 快速开始

1. 用 Visual Studio 打开 `TokenViewerExtension.sln`，选 **Debug** + `(Package)` 配置；
2. `生成` → `部署 TokenViewerExtension`（**必须部署**）；
3. 命令面板运行 `Reload` →「Reload Command Palette Extension」；
4. 扩展设置填入：DeepSeek API Key（余额用，必填）、DeepSeek 官方平台 Token（官方用量，必填）、代理端口（默认 8788，可选）；
5. 打开「用量诊断」→ 点「测试 DeepSeek 官方平台 官方接口」→ 看到「✅ 拉取成功：N 天数据」即完成。

## 验证

诊断页「测试转发 DeepSeek」可验证代理链路；「测试官方接口」可验证 Token 并显示**原始响应**（接口异常时可见平台原文，便于排查）。

数据文件：`%LOCALAPPDATA%\TokenViewerExtension\usage.json`（代理统计，按天持久化）。

## 工程结构

```
TokenViewerExtension/
├── Proxy/          # 本地代理：端口监听、请求转发、SSE 透传与用量解析
├── Services/       # 用量存储/汇总、价格表、余额、DeepSeek 平台直连、图表生成
├── Settings/       # 设置管理器（API Key / 平台 Token / 端口 / 刷新间隔）
└── Pages/          # 总览、详情、官方用量、图表、账户监控、诊断
```

## 数据与配置存储

- 配置（API Key / 平台 Token / 端口）：`%LOCALAPPDATA%\TokenViewerExtension\settings.json`，**开机自动恢复**（不依赖宿主设置）
- 代理统计：`%LOCALAPPDATA%\TokenViewerExtension\usage.json`（仅代理模式记账后生成）
- 官方平台数据：实时拉取，不落盘

## 用户安装（发布后）

### 方式一：GitHub Release 下载 MSIX（未签名包）
1. 到 Releases 页下载对应架构的 `.msix`（x64 / arm64）
2. Windows 设置 → 系统 → 开发者选项 → 开启**开发人员模式**（未签名 MSIX 必需）
3. 双击安装，或 PowerShell 执行：
   ```powershell
   Add-AppxPackage -Path .\TokenViewerExtension_0.0.1.0_x64.msix
   ```
4. 打开命令面板 → `Reload` → 选择「Reload Command Palette Extension」

### 方式二：WinGet（上线后）
```powershell
winget install TokenViewerExtension
```
命令面板中也可直接 `Search WinGet` 搜索安装。

### 方式三：Microsoft Store（上线后）
Store 搜索 "TokenViewerExtension" 安装（自动更新）。

## 发布渠道


详见 [PUBLISHING.md](PUBLISHING.md)，四渠道一览：

| 渠道 | 成本 | 自动更新 | 说明 |
|------|------|---------|------|
| GitHub Releases | 免费 | ❌ | 首发最快，MSIX 直装 |
| WinGet | 免费 | ✅ | 官方推荐，命令面板 `Search WinGet` 可发现 |
| Microsoft Store | 个人免费 | ✅ | 覆盖最广，需审核与隐私政策 |
| 扩展图库 (CmdPal-Extensions) | 免费 | ✅ | 命令面板内置图库展示（PR 审核） |

## 开源与许可


- 本项目基于 **MIT 许可证** 开源（见 `LICENSE`），代码为原创实现；
- 依赖的第三方库均为 MIT 等宽松许可，声明见 `THIRD-PARTY-NOTICES.md`；
- 本项目是 PowerToys 命令面板的第三方扩展，与微软、PowerToys、DeepSeek 无隶属或背书关系；
- 使用 DeepSeek 平台内部接口为可选实验性功能，风险自负。

## 发布注意事项（隐私与安全）


发布前请检查：

1. **构建产物不入库**：项目已含 `.gitignore`（排除 `bin/`、`obj/`、`.vs/` 及所有本地 `*.json`）——`obj/` 内含本机绝对路径（如 `C:\Users\<用户名>\.nuget\...`），切勿提交
2. **代码无个人信息**：源码不含本机路径/IP（仅 127.0.0.1 回环地址）；诊断页只显示 `%LOCALAPPDATA%` 占位形式，不显示绝对路径
3. **签名与发布者**：`Package.appxmanifest` 中的 `Publisher`/`PublisherDisplayName` 目前是模板默认值（"A Lone Developer"），正式发布需替换为**你自己的代码签名证书**（MSIX 要求签名）：
   ```
   Publisher="CN=你的名称, O=组织, C=CN"
   PublisherDisplayName=你的名称
   ```
   并在 `launchSettings.json` 的发布配置中配置证书
4. **密钥安全**：API Key / 平台 Token 保存在本机 `settings.json`，属于敏感信息——请勿把该文件或 `usage.json` 提交/分享
5. **原始响应**：诊断页「复制原始响应」可能包含用量数据，分享排查问题时请自行判断是否敏感

## 已知限制

- **进程生命周期**：代理随命令面板会话运行；官方数据路径不受影响
- **流式估算**：代理模式下流式响应无 usage 时按字符估算（官方数据无此问题）
- **官方接口为内部接口**：可能随时变更，诊断页会显示原始响应便于适配；Token 定期失效需更新
- **费用为估算**：单价表可能过时，仅供参考
