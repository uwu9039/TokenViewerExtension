# TokenViewerExtension — EXE 安装器构建脚本（WinGet 分发用）
#
# 用法：
#   .\build-exe.ps1                              # 构建 x64 + arm64
#   .\build-exe.ps1 -Platforms @("x64")          # 只构建 x64
#
# 流程：dotnet publish（unpackaged 模式）→ Inno Setup 编译安装器
# 产物：bin\Release\installer\TokenViewerExtension-Setup-<版本>-<平台>.exe

param(
    [string[]]$Platforms = @("x64", "arm64"),
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$ProjectDir = Split-Path -Parent $MyInvocation.MyCommand.Path

# 从 Package.appxmanifest 读取版本号，避免手工维护
$manifest = [xml](Get-Content (Join-Path $ProjectDir "Package.appxmanifest"))
$version = $manifest.Package.Identity.Version
Write-Host "版本：$version"

# 定位 Inno Setup
$iscc = @(
    "${env:ProgramFiles(x86)}\Inno Setup 6\iscc.exe",
    "${env:ProgramFiles}\Inno Setup 6\iscc.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) {
    throw "未找到 Inno Setup 6（iscc.exe）。请先安装：winget install -e --id JRSoftware.InnoSetup"
}

foreach ($platform in $Platforms) {
    Write-Host "`n=== 构建 $platform ===" -ForegroundColor Cyan

    # 1. 发布（WindowsPackageType=None：以未打包方式运行，供注册表注册扩展）
    dotnet publish (Join-Path $ProjectDir "TokenViewerExtension.csproj") `
        --configuration $Configuration `
        --runtime "win-$platform" `
        --self-contained true `
        --output (Join-Path $ProjectDir "bin\$Configuration\win-$platform\publish") `
        "-p:WindowsPackageType=None" "-p:PublishProfile="
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish 失败（$platform）" }

    # 2. 由模板生成平台专属安装脚本
    $iss = Get-Content (Join-Path $ProjectDir "setup-template.iss") -Raw
    $iss = $iss -replace '#define AppVersion ".*"', "#define AppVersion `"$version`""
    $iss = $iss.Replace('win-x64\publish', "win-$platform\publish")
    $iss = $iss -replace 'OutputBaseFilename=(.*?)\{#AppVersion\}', "`$1{#AppVersion}-$platform"
    if ($platform -eq "arm64") {
        $iss = $iss -replace 'ArchitecturesAllowed=x64compatible', 'ArchitecturesAllowed=arm64'
        $iss = $iss -replace 'ArchitecturesInstallIn64BitMode=x64compatible', 'ArchitecturesInstallIn64BitMode=arm64'
    }
    $issPath = Join-Path $ProjectDir "setup-$platform.iss"
    $iss | Out-File $issPath -Encoding UTF8

    # 3. 编译安装器
    & $iscc $issPath
    if ($LASTEXITCODE -ne 0) { throw "Inno Setup 编译失败（$platform）" }

    $installer = Get-ChildItem (Join-Path $ProjectDir "bin\$Configuration\installer\*-$platform.exe") -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($installer) {
        Write-Host "完成：$($installer.Name)（$([math]::Round($installer.Length / 1MB, 2)) MB）" -ForegroundColor Green
    }
}

Write-Host "`n全部完成，安装器位于 bin\$Configuration\installer\" -ForegroundColor Green
