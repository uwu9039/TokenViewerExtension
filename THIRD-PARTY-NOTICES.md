# 第三方依赖声明（Third-Party Notices）

本项目源代码为原创实现，不包含第三方代码。以下 NuGet 依赖以二进制包形式引用，
许可证信息如下（均为宽松开源许可，可自由商用）：

| 依赖 | 用途 | 许可证 |
|------|------|--------|
| [Microsoft.CommandPalette.Extensions](https://www.nuget.org/packages/Microsoft.CommandPalette.Extensions) | 命令面板扩展 API（WinRT 投影） | MIT |
| [Shmuelie.WinRTServer](https://www.nuget.org/packages/Shmuelie.WinRTServer) | WinRT COM 服务器托管 | MIT |
| [Microsoft.Windows.CsWinRT](https://www.nuget.org/packages/Microsoft.Windows.CsWinRT) | C#/WinRT 投影生成 | MIT |
| [Microsoft.WindowsAppSDK](https://www.nuget.org/packages/Microsoft.WindowsAppSDK) | Windows 应用 SDK 运行时 | MIT |
| [Microsoft.Windows.SDK.BuildTools](https://www.nuget.org/packages/Microsoft.Windows.SDK.BuildTools) | Windows SDK 构建工具 | MIT |
| [Microsoft.Windows.SDK.BuildTools.MSIX](https://www.nuget.org/packages/Microsoft.Windows.SDK.BuildTools.MSIX) | MSIX 打包工具 | MIT |

依赖的完整许可证文本随 NuGet 包分发，或见各项目仓库。

## 与 PowerToys 的关系

本项目是 PowerToys 命令面板（Command Palette）的第三方扩展：
- 仅调用 PowerToys 公开的扩展 API（`Microsoft.CommandPalette.Extensions`），不包含、不修改 PowerToys 代码；
- 项目由官方「Create a new extension」模板引导搭建，但**模板代码已全部重写为原创实现**，项目内不保留模板源码；
- 本项目与微软及 PowerToys 无隶属、背书或赞助关系；
- 扩展包安装后以独立 MSIX 应用存在，卸载不影响 PowerToys 本身。

## 使用 DeepSeek 服务的说明

- 本扩展通过 DeepSeek 官方 OpenAI 兼容 API（`api.deepseek.com`）与平台用量页数据（`platform.deepseek.com` 内部接口）读取账户数据，需要用户自行提供 API Key / 平台 Token；
- 平台内部接口非官方公开接口，使用与否由用户自行决定，风险自负；
- 本扩展与 DeepSeek 无隶属或背书关系。
