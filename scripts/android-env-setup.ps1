<#
.SYNOPSIS
    一键搭建 ReadStorm Android 开发环境（JDK + Android SDK + AVD + .NET workload）。

.DESCRIPTION
    自动完成以下步骤：
      1. 安装 Microsoft OpenJDK 17（通过 winget）
      2. 安装 .NET Android workload
      3. 下载并安装 Android SDK 命令行工具
      4. 接受所有 SDK 许可证
      5. 安装 SDK 组件（platform-tools、build-tools、platforms、emulator、系统镜像）
      6. 创建 AVD（默认 ReadStorm_API34）
      7. 设置持久化用户级环境变量

.PARAMETER AvdName
    要创建的 AVD 名称，默认 ReadStorm_API34。

.PARAMETER AvdDevice
    AVD 使用的设备型号，默认 pixel_6。

.PARAMETER ApiLevel
    模拟器使用的 API 级别，默认 34。

.PARAMETER SkipJdk
    跳过 JDK 安装（已有 JDK 17+ 时使用）。

.PARAMETER SkipWorkload
    跳过 .NET Android workload 安装。

.PARAMETER SkipAvd
    跳过 AVD 创建。

.PARAMETER Force
    强制重新安装所有组件（即使已存在）。

.EXAMPLE
    .\android-env-setup.ps1
    # 完整安装

.EXAMPLE
    .\android-env-setup.ps1 -SkipJdk -SkipWorkload
    # 仅安装 SDK 和创建 AVD
#>

param(
    [string]$AvdName = "ReadStorm_API34",
    [string]$AvdDevice = "pixel_6",
    [int]$ApiLevel = 34,
    [switch]$SkipJdk,
    [switch]$SkipWorkload,
    [switch]$SkipAvd,
    [switch]$Force
)

$ErrorActionPreference = "Stop"

# ─────────────────────────── 辅助函数 ───────────────────────────

function Write-Step([string]$msg)  { Write-Host "[STEP] $msg" -ForegroundColor Cyan }
function Write-Ok([string]$msg)    { Write-Host "[ OK ] $msg" -ForegroundColor Green }
function Write-Warn([string]$msg)  { Write-Host "[WARN] $msg" -ForegroundColor Yellow }
function Write-Err([string]$msg)   { Write-Host "[ERR ] $msg" -ForegroundColor Red }

function Test-CommandExists([string]$cmd) {
    $null -ne (Get-Command $cmd -ErrorAction SilentlyContinue)
}

# ─────────────────────────── 常量 ───────────────────────────────

$SdkRoot       = Join-Path $env:LocalAppData "Android\Sdk"
$CmdlineZipUrl = "https://dl.google.com/android/repository/commandlinetools-win-11076708_latest.zip"
$JdkId         = "Microsoft.OpenJDK.17"

# .NET Android workload 36.x 需要 android-36 平台才能编译
# 模拟器使用 $ApiLevel（默认 34）的系统镜像
$SdkPackages = @(
    "platform-tools",
    "build-tools;34.0.0",
    "platforms;android-$ApiLevel",
    "platforms;android-36",
    "emulator",
    "system-images;android-$ApiLevel;google_apis;x86_64"
)

# ─────────────────────────── 1. JDK ────────────────────────────

if (-not $SkipJdk) {
    Write-Step "检查 JDK 17"

    $jdkInstalled = $false
    if (Test-CommandExists "java") {
        $javaVer = (java -version 2>&1 | Select-Object -First 1) -replace '[^0-9.]', ''
        $major = ($javaVer -split '\.')[0]
        if ([int]$major -ge 17) {
            $jdkInstalled = $true
            Write-Ok "JDK $major 已安装，跳过"
        }
    }

    # 也检查常见安装路径
    if (-not $jdkInstalled) {
        $jdkPaths = Get-ChildItem "C:\Program Files\Microsoft" -Directory -Filter "jdk-17*" -ErrorAction SilentlyContinue
        if ($jdkPaths -and -not $Force) {
            $jdkHome = $jdkPaths | Select-Object -First 1 -ExpandProperty FullName
            Write-Ok "检测到 JDK: $jdkHome，跳过安装"
            $jdkInstalled = $true
        }
    }

    if (-not $jdkInstalled -or $Force) {
        if (-not (Test-CommandExists "winget")) {
            throw "未找到 winget，请先安装 App Installer 或手动安装 JDK 17+: https://adoptium.net"
        }

        Write-Step "通过 winget 安装 $JdkId"
        winget install --id $JdkId --accept-source-agreements --accept-package-agreements --silent
        if ($LASTEXITCODE -ne 0 -and $LASTEXITCODE -ne -1978335189 <# 已安装 #>) {
            throw "JDK 安装失败 (exit=$LASTEXITCODE)"
        }
        Write-Ok "JDK 17 安装完成"
    }

    # 定位 JDK 安装路径并设置 JAVA_HOME
    $jdkDir = Get-ChildItem "C:\Program Files\Microsoft" -Directory -Filter "jdk-17*" -ErrorAction SilentlyContinue |
              Sort-Object Name -Descending | Select-Object -First 1 -ExpandProperty FullName

    if (-not $jdkDir) {
        throw "JDK 安装后未找到目录，请确认安装状态"
    }

    $env:JAVA_HOME = $jdkDir
    $env:Path = "$jdkDir\bin;$env:Path"
    [System.Environment]::SetEnvironmentVariable("JAVA_HOME", $jdkDir, [System.EnvironmentVariableTarget]::User)
    Write-Ok "JAVA_HOME = $jdkDir"
}

# ──────────────────── 2. .NET Android workload ─────────────────

if (-not $SkipWorkload) {
    Write-Step "检查 .NET Android workload"

    $workloads = dotnet workload list 2>&1 | Out-String
    if ($workloads -match "android" -and -not $Force) {
        Write-Ok "Android workload 已安装，跳过"
    }
    else {
        Write-Step "安装 .NET Android workload"
        dotnet workload install android
        if ($LASTEXITCODE -ne 0) {
            throw "workload 安装失败 (exit=$LASTEXITCODE)"
        }
        Write-Ok "Android workload 安装完成"
    }
}

# ──────────────────── 3. Android SDK 命令行工具 ─────────────────

Write-Step "检查 Android SDK 命令行工具"

$sdkmanager = Join-Path $SdkRoot "cmdline-tools\latest\bin\sdkmanager.bat"

if ((Test-Path $sdkmanager) -and -not $Force) {
    Write-Ok "命令行工具已存在，跳过下载"
}
else {
    Write-Step "下载 Android 命令行工具"

    New-Item -ItemType Directory -Path $SdkRoot -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $SdkRoot "cmdline-tools") -Force | Out-Null

    $zipPath = Join-Path $env:TEMP "android-cmdline-tools.zip"
    Invoke-WebRequest -Uri $CmdlineZipUrl -OutFile $zipPath -UseBasicParsing
    Write-Ok "下载完成: $([math]::Round((Get-Item $zipPath).Length / 1MB, 1)) MB"

    Write-Step "解压到 $SdkRoot\cmdline-tools\latest"
    $extractPath = Join-Path $env:TEMP "android-cmdline-extract"
    if (Test-Path $extractPath) { Remove-Item $extractPath -Recurse -Force }
    Expand-Archive -Path $zipPath -DestinationPath $extractPath -Force

    $latestDir = Join-Path $SdkRoot "cmdline-tools\latest"
    if (Test-Path $latestDir) { Remove-Item $latestDir -Recurse -Force }
    Move-Item -Path (Join-Path $extractPath "cmdline-tools") -Destination $latestDir -Force

    # 清理临时文件
    Remove-Item $zipPath -Force -ErrorAction SilentlyContinue
    Remove-Item $extractPath -Recurse -Force -ErrorAction SilentlyContinue

    Write-Ok "命令行工具安装完成"
}

# ──────────────────── 4. 接受 SDK 许可证 ───────────────────────

Write-Step "接受 SDK 许可证"

# 用管道自动应答 y
$licenseInput = ("y`n" * 20)
$licenseInput | cmd /c "`"$sdkmanager`" --sdk_root=`"$SdkRoot`" --licenses" 2>&1 | Out-Null
Write-Ok "许可证已接受"

# ──────────────────── 5. 安装 SDK 组件 ─────────────────────────

Write-Step "安装 SDK 组件"

$packageArgs = ($SdkPackages | ForEach-Object { "`"$_`"" }) -join " "
$installCmd = "`"$sdkmanager`" --sdk_root=`"$SdkRoot`" --install $packageArgs"

cmd /c $installCmd 2>&1 | ForEach-Object {
    if ($_ -match '^\[.*\] (100%|Installing|Unzipping)') {
        # 静默大部分进度，只显示关键行
    }
    elseif ($_ -match 'done|Installed') {
        Write-Host "  $_" -ForegroundColor DarkGray
    }
}

# 验证关键组件
$adbPath = Join-Path $SdkRoot "platform-tools\adb.exe"
$emulatorPath = Join-Path $SdkRoot "emulator\emulator.exe"

if (-not (Test-Path $adbPath))      { throw "adb 安装失败：$adbPath 不存在" }
if (-not (Test-Path $emulatorPath)) { throw "emulator 安装失败：$emulatorPath 不存在" }

Write-Ok "SDK 组件安装完成"

# ──────────────────── 6. 设置环境变量 ──────────────────────────

Write-Step "设置环境变量"

$env:ANDROID_SDK_ROOT = $SdkRoot
$env:ANDROID_HOME     = $SdkRoot
[System.Environment]::SetEnvironmentVariable("ANDROID_SDK_ROOT", $SdkRoot, [System.EnvironmentVariableTarget]::User)
[System.Environment]::SetEnvironmentVariable("ANDROID_HOME", $SdkRoot, [System.EnvironmentVariableTarget]::User)

Write-Ok "ANDROID_SDK_ROOT = $SdkRoot"
Write-Ok "ANDROID_HOME     = $SdkRoot"

# ──────────────────── 7. 创建 AVD ─────────────────────────────

if (-not $SkipAvd) {
    Write-Step "检查 AVD: $AvdName"

    $existingAvds = & $emulatorPath -list-avds 2>$null
    if ($existingAvds -contains $AvdName -and -not $Force) {
        Write-Ok "AVD '$AvdName' 已存在，跳过"
    }
    else {
        $avdmanager = Join-Path $SdkRoot "cmdline-tools\latest\bin\avdmanager.bat"
        $systemImage = "system-images;android-$ApiLevel;google_apis;x86_64"

        Write-Step "创建 AVD: $AvdName (device=$AvdDevice, API=$ApiLevel)"
        "no" | cmd /c "`"$avdmanager`" create avd --name $AvdName --package `"$systemImage`" --device `"$AvdDevice`" --force" 2>&1 | Out-Null

        # 验证
        $avdsAfter = & $emulatorPath -list-avds 2>$null
        if ($avdsAfter -contains $AvdName) {
            Write-Ok "AVD '$AvdName' 创建成功"
        }
        else {
            Write-Warn "AVD 创建可能未成功，请手动检查: $avdmanager list avd"
        }
    }
}

# ──────────────────── 完成 ─────────────────────────────────────

Write-Host ""
Write-Host "============================================" -ForegroundColor Green
Write-Host "  Android 开发环境搭建完成！" -ForegroundColor Green
Write-Host "============================================" -ForegroundColor Green
Write-Host ""
Write-Host "已安装组件：" -ForegroundColor White
Write-Host "  JDK:           $env:JAVA_HOME"
Write-Host "  Android SDK:   $SdkRoot"
Write-Host "  adb:           $adbPath"
Write-Host "  emulator:      $emulatorPath"
Write-Host "  AVD:           $AvdName"
Write-Host ""
Write-Host "后续操作：" -ForegroundColor White
Write-Host "  构建项目：     dotnet build ReadStorm.slnx"
Write-Host "  打包 APK：     .\scripts\android-dev-oneclick.ps1 -PackageOnly"
Write-Host "  完整联调：     .\scripts\android-dev-oneclick.ps1"
Write-Host ""
Write-Host "注意：新终端窗口需重启后环境变量才会生效。" -ForegroundColor Yellow
