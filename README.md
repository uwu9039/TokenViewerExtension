# Token 用量监控（TokenViewerExtension）

基于 PowerToys 命令面板的 DeepSeek Token 消耗监控扩展。配置一次 API Key 和平台 Token 后，即可在命令面板中查看 token 用量、请求次数、消费金额与官方图表。

扩展包含三个模块：

- Token 用量监控（📊）：今日总计、用量详情、官方用量、图表
- 账户监控（💳）：账户余额、今日用量与请求数、官方图表
- 用量诊断（🔍）：检查配置与连接，定位为什么没有数据

## 安装

### 方式一：从 GitHub Release 下载

1. 到本仓库 Releases 页下载对应架构的安装包（x64 / arm64）
2. 打开 Windows 设置 → 系统 → 开发者选项 → 开启开发人员模式（未签名安装包需要）
3. 双击安装包安装，或在 PowerShell 中执行：

   ```
   Add-AppxPackage -Path .\TokenViewerExtension_0.0.3.0_x64.msix
   ```

4. 打开命令面板（默认 Alt+Space），输入 Reload 回车，选择「Reload Command Palette Extension」

### 方式二：WinGet（上线后可用）

```
winget install TokenViewerExtension
```

也可在命令面板中直接搜索 WinGet 安装。

### 方式三：Microsoft Store（上线后可用）

在 Microsoft Store 搜索 TokenViewerExtension 安装，自动更新。

## 快速开始

1. 打开命令面板 → 扩展设置（齿轮图标）→ Token 用量监控
2. 填入 DeepSeek API Key（查询余额用，必填）
3. 填入 DeepSeek 官方平台 Token（查看用量用，获取方法见下一节）
4. 打开「用量诊断」→ 点击「测试 DeepSeek 官方平台 官方接口」→ 显示"拉取成功：N 天数据"即完成配置

## 获取 DeepSeek 官方平台 Token

1. 浏览器登录 platform.deepseek.com
2. 按 F12 打开开发者工具，切换到 Console（控制台）标签
3. 输入以下命令后回车：

   ```
   JSON.parse(localStorage.getItem('userToken')).value
   ```

4. 复制返回的 eyJ... 开头的字符串
5. 粘贴到扩展设置中的「DeepSeek 官方平台 Token」

Token 有效期为数天，过期后按同样步骤重新获取即可。Token 相当于账户凭证，请勿泄露给他人。

## 使用说明

- token 消耗来自 DeepSeek 官方平台（约 5 分钟延迟），配置平台 Token 后自动同步
- 官方数据包含：按天/按模型用量、输入/输出/缓存 token、请求次数、消费金额（可用时）、账户余额
- 图表页展示每日用量柱状图与模型占比；按天数据官方尚未更新时，先显示月度汇总
- 诊断页的「测试官方接口」可验证 Token 是否有效

## 常见问题

**为什么显示 0？**

token 消耗来自官方平台数据，需要配置「DeepSeek 官方平台 Token」（与 API Key 是两回事）。未配置或数据未拉取时用量显示为 0。打开「用量诊断」页即可看到各数据源的配置状态。

**数据保存在哪里？**

配置保存在本机 %LOCALAPPDATA%\TokenViewerExtension\ 目录下（settings.json），不会上传到任何服务器。

**官方接口会失效吗？**

平台内部接口可能随时变更，失效时诊断页会显示平台返回的原始响应；Token 过期时重新获取即可。

## 已知限制

- 官方数据约有 5 分钟延迟；按天明细可能滞后，此时显示月度汇总
- 消费金额与费用为估算值，仅供参考
- 扩展与微软、PowerToys、DeepSeek 无隶属关系，使用平台内部接口为实验性功能，风险自负

## 许可

本项目以 MIT 许可证开源（见 LICENSE）。
