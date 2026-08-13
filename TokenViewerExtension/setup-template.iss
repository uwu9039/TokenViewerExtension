; TokenViewerExtension 安装器脚本（Inno Setup 6）
; 由 build-exe.ps1 根据平台生成 setup-x64.iss / setup-arm64.iss 后编译
;
; 所需信息：
;   CLSID = TokenViewerExtension.cs 中 [Guid("24e5fecd-2dc1-4f0a-9b02-350545898591")]
;   AppId = 安装器唯一标识（见下方 AppId）

#define AppVersion "0.0.2.0"

[Setup]
AppId={{47E8B9EF-2B4F-4C17-9643-BE01481FDCB8}
AppName=Token 用量监控
AppVersion={#AppVersion}
AppPublisher=uwu9039
AppPublisherURL=https://github.com/uwu9039/TokenViewerExtension
DefaultDirName={autopf}\TokenViewerExtension
OutputDir=bin\Release\installer
OutputBaseFilename=TokenViewerExtension-Setup-{#AppVersion}
Compression=lzma
SolidCompression=yes
MinVersion=10.0.19041
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
Source: "bin\Release\win-x64\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs

[Icons]
Name: "{group}\Token 用量监控"; Filename: "{app}\TokenViewerExtension.exe"

; 以未打包（unpackaged）方式注册扩展，供命令面板宿主发现
[Registry]
Root: HKCU; Subkey: "SOFTWARE\Classes\CLSID\{{24E5FECD-2DC1-4F0A-9B02-350545898591}}"; ValueData: "TokenViewerExtension"; ValueType: string
Root: HKCU; Subkey: "SOFTWARE\Classes\CLSID\{{24E5FECD-2DC1-4F0A-9B02-350545898591}}\LocalServer32"; ValueData: "{app}\TokenViewerExtension.exe -RegisterProcessAsComServer"; ValueType: string
