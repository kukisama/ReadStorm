param(
    [ValidateSet('1','2','3')]
    [string]$Mode = '1', # 1=安卓 2=桌面 3=全部
    [string]$Project = "src/ReadStorm.Android/ReadStorm.Android.csproj",
    [string]$Configuration = "Debug",
    [string]$PackageId = "com.readstorm.app",
    [string]$AvdName = "ReadStorm_API34",
    [int]$BootTimeoutSeconds = 180,
    [switch]$SkipBuild,
    [switch]$NoEmulator,
    [switch]$ShowFullLogcat,
    [switch]$PackageOnly=$true, #虚拟机环境，只打包APK，不执行安装和联调
    [string]$OutputApkDir,
    [switch]$FastDebug =$true# 极速调试包模式，跳过签名包流程，极快
)

$ErrorActionPreference = "Stop"

function Write-Step([string]$message) {
    Write-Host "[STEP] $message" -ForegroundColor Cyan
}

function Write-Ok([string]$message) {
    Write-Host "[ OK ] $message" -ForegroundColor Green
}

function Write-Warn([string]$message) {
    Write-Host "[WARN] $message" -ForegroundColor Yellow
}

function Write-Err([string]$message) {
    Write-Host "[ERR ] $message" -ForegroundColor Red
}

function Exec([scriptblock]$block, [string]$errorMessage) {
    & $block
    if ($LASTEXITCODE -ne 0) {
        throw "$errorMessage (exit=$LASTEXITCODE)"
    }
}

function Get-SdkRoot {
    if ($env:ANDROID_SDK_ROOT -and (Test-Path $env:ANDROID_SDK_ROOT)) {
        return $env:ANDROID_SDK_ROOT
    }

    $fallback = Join-Path $env:LocalAppData "Android\Sdk"
    if (Test-Path $fallback) {
        return $fallback
    }

    throw "未找到 Android SDK。请先设置 ANDROID_SDK_ROOT 或安装到 $fallback"
}

function Wait-DeviceBootCompleted([string]$adbPath, [int]$timeoutSeconds) {
    $start = Get-Date
    while ((Get-Date) -lt $start.AddSeconds($timeoutSeconds)) {
        $boot = (& $adbPath shell getprop sys.boot_completed 2>$null).Trim()
        if ($boot -eq "1") {
            return $true
        }
        Start-Sleep -Seconds 2
    }

    return $false
}

function Get-LauncherComponent([string]$adbPath, [string]$packageId) {
    $resolved = (& $adbPath shell cmd package resolve-activity --brief $packageId 2>$null) |
        ForEach-Object { $_.Trim() } |
        Where-Object { $_ -and $_ -notmatch '^priority=' -and $_ -notmatch '^No activity found' }

    $component = $resolved | Where-Object { $_ -match '/' } | Select-Object -Last 1
    return $component
}

function Publish-DesktopApp {
    param(
        [string]$Configuration = "Release"
    )
    $repoRoot = Split-Path -Parent $PSScriptRoot
    $desktopProj = Join-Path $repoRoot "src/ReadStorm.Desktop/ReadStorm.Desktop.csproj"
    $outputDir = Join-Path $repoRoot "publish/desktop/$Configuration"
    Write-Step "发布桌面应用 ($Configuration)"
    Exec {
        & dotnet publish $desktopProj -c $Configuration -r win-x64 --self-contained true -p:PublishSingleFile=true -o $outputDir
    } "桌面端发布失败"
    Write-Ok "桌面端发布完成：$outputDir"
}

$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

# 1=安卓 2=桌面 3=全部
switch ($Mode) {
    '1' {
        # 安卓打包（原有流程）
        $DoAndroid = $true; $DoDesktop = $false
    }
    '2' {
        $DoAndroid = $false; $DoDesktop = $true
    }
    '3' {
        $DoAndroid = $true; $DoDesktop = $true
    }
    default {
        $DoAndroid = $true; $DoDesktop = $false
    }
}

if ($DoDesktop) {
    Publish-DesktopApp -Configuration $Configuration
    $desktopOutputDir = Join-Path $repoRoot "publish/desktop/$Configuration"
    Write-Host ""
}

if (-not $DoAndroid) {
    if ($PackageOnly -and $DoDesktop) {
        # 只打包桌面，且纯打包模式，自动打开桌面发布目录
        Start-Process explorer.exe $desktopOutputDir
    }
    Write-Ok "未选择安卓打包，流程结束。"
    return
}

$adb = $null
$emulator = $null

if (-not $PackageOnly) {
    $sdkRoot = Get-SdkRoot
    $adb = Join-Path $sdkRoot "platform-tools\adb.exe"
    $emulator = Join-Path $sdkRoot "emulator\emulator.exe"

    if (!(Test-Path $adb)) { throw "未找到 adb: $adb" }
    if (!(Test-Path $emulator)) { throw "未找到 emulator: $emulator" }

    Write-Step "启动 adb 服务"
    Exec { & $adb start-server | Out-Null } "adb start-server 失败"

    $devices = & $adb devices
    $emulatorOnline = $devices | Where-Object { $_ -match '^emulator-\d+\s+device$' }

    if (-not $NoEmulator) {
        if (-not $emulatorOnline) {
            Write-Step "未发现在线模拟器，启动 AVD: $AvdName"
            $existingAvds = & $emulator -list-avds
            if ($existingAvds -notcontains $AvdName) {
                throw "未找到 AVD '$AvdName'。请先创建，或传入 -AvdName 指定现有 AVD。"
            }

            # 将模拟器输出重定向到日志文件，便于排查启动失败
            $emulatorLog = Join-Path $repoRoot "publish\emulator-startup.log"
            $emulatorLogDir = Split-Path $emulatorLog -Parent
            if (!(Test-Path $emulatorLogDir)) {
                New-Item -ItemType Directory -Path $emulatorLogDir -Force | Out-Null
            }

            Write-Step "模拟器日志将写入: $emulatorLog"
            $emulatorProc = Start-Process -FilePath $emulator `
                -ArgumentList @("-avd", $AvdName, "-no-snapshot-load", "-gpu", "swiftshader_indirect", "-no-metrics") `
                -RedirectStandardOutput $emulatorLog `
                -RedirectStandardError "$emulatorLog.err" `
                -PassThru

            # 等待几秒，检查模拟器是否立即崩溃退出
            Start-Sleep -Seconds 5
            if ($emulatorProc.HasExited) {
                $exitCode = $emulatorProc.ExitCode
                Write-Err "模拟器进程已退出 (exit=$exitCode)"
                if (Test-Path "$emulatorLog.err") {
                    $errContent = Get-Content "$emulatorLog.err" -Raw
                    if ($errContent) {
                        Write-Err "模拟器错误输出:"
                        Write-Host $errContent -ForegroundColor Red
                    }
                }
                if (Test-Path $emulatorLog) {
                    $stdContent = Get-Content $emulatorLog -Raw
                    if ($stdContent) {
                        Write-Warn "模拟器标准输出:"
                        Write-Host $stdContent -ForegroundColor Yellow
                    }
                }
                throw "模拟器启动失败，请检查上方日志。常见原因: 未启用硬件加速(WHPX/Hyper-V)、AVD 配置损坏等。"
            }

            Write-Step "等待模拟器上线"
            Exec { & $adb wait-for-device | Out-Null } "等待设备失败"

            if (-not (Wait-DeviceBootCompleted -adbPath $adb -timeoutSeconds $BootTimeoutSeconds)) {
                throw "模拟器在 ${BootTimeoutSeconds}s 内未完成启动"
            }
            Write-Ok "模拟器已就绪"
        }
        else {
            Write-Ok "检测到在线模拟器，复用现有设备"
        }
    }
    else {
        Write-Warn "已启用 -NoEmulator，将使用当前已连接设备"
    }
}
else {
    Write-Warn "已启用 -PackageOnly：仅打包 APK，不执行模拟器安装与联调"
}


if (-not $SkipBuild) {
    if ($PackageOnly) {
        if ($FastDebug) {
            Write-Step "极速调试包模式：签名 + 禁用链接器加速"
            Exec {
                & dotnet build $Project -c Debug -t:SignAndroidPackage -p:AndroidPackageFormats=apk -p:AndroidPackageFormat=apk -p:AndroidLinkMode=None -p:RunAOTCompilation=false -v minimal
            } "APK 调试包构建失败"
        } else {
            Write-Step "生成可分发 APK ($Configuration)"
            Exec {
                & dotnet build $Project -c $Configuration -t:SignAndroidPackage -p:AndroidPackageFormats=apk -p:AndroidPackageFormat=apk -v minimal
            } "APK 打包失败"
        }
    }
    else {
        Write-Step "构建 Android 项目 ($Configuration)"
        Exec { & dotnet build $Project -c $Configuration -v minimal } "dotnet build 失败"
    }
} else {
    Write-Warn "已启用 -SkipBuild，跳过构建"
}

$apkDir = Join-Path $repoRoot "src\ReadStorm.Android\bin\$Configuration\net10.0-android"
if (!(Test-Path $apkDir)) {
    throw "未找到 APK 输出目录: $apkDir"
}


# 优先找 Signed.apk，兜底找任意 apk
$apk = Get-ChildItem -Path $apkDir -Filter "*Signed.apk" -File -ErrorAction SilentlyContinue |
    Select-Object -First 1
if (-not $apk) {
    $apk = Get-ChildItem -Path $apkDir -Filter "*.apk" -File -ErrorAction SilentlyContinue |
        Select-Object -First 1
}
if (-not $apk) {
    throw "未找到 APK 文件：$apkDir"
}

if ($PackageOnly) {
    if (-not $OutputApkDir) {
        $OutputApkDir = Join-Path $repoRoot "publish\android\$Configuration"
    }

    if (!(Test-Path $OutputApkDir)) {
        New-Item -ItemType Directory -Path $OutputApkDir -Force | Out-Null
    }

    $finalApk = Join-Path $OutputApkDir ("ReadStorm-" + $Configuration.ToLowerInvariant() + ".apk")
    Copy-Item -Path $apk.FullName -Destination $finalApk -Force

    Write-Host ""
    Write-Ok "APK 打包完成（仅交付文件）"
    Write-Host "- APK: $finalApk"

    # 打包完成后自动打开目标文件夹（安卓/桌面/全部）
    if ($DoDesktop) {
        if (-not $desktopOutputDir) {
            $desktopOutputDir = Join-Path $repoRoot "publish/desktop/$Configuration"
        }
        Start-Process explorer.exe $desktopOutputDir
    }
    Start-Process explorer.exe $OutputApkDir
    return
}

Write-Step "安装 APK: $($apk.Name)"
Exec { & $adb install -r $apk.FullName } "adb install 失败"

$component = Get-LauncherComponent -adbPath $adb -packageId $PackageId
if (-not $component) {
    throw "无法解析启动 Activity，请检查 PackageId 是否正确：$PackageId"
}

Write-Step "清理日志并启动应用: $component"
Exec { & $adb logcat -c } "logcat 清理失败"
Exec { & $adb shell am force-stop $PackageId } "force-stop 失败"
Exec { & $adb shell am start -n $component } "am start 失败"

Start-Sleep -Seconds 4

$logScopeProcess = $null
$procLines = & $adb shell ps -A 2>$null
$procRow = $procLines | Where-Object { $_ -match ("\s" + [regex]::Escape($PackageId) + "$") } | Select-Object -First 1
if ($procRow) {
    $columns = ($procRow -split '\s+') | Where-Object { $_ }
    $procNum = if ($columns.Count -ge 2) { $columns[1] } else { "unknown" }
    $logScopeProcess = if ($columns.Count -ge 2) { $columns[1] } else { $null }
    Write-Ok "应用进程在线，PID=$procNum"
}
else {
    Write-Warn "未检测到应用进程，可能启动失败，继续输出错误日志"
}

Write-Step "抓取近期日志（错误关键词过滤）"
$patterns = @(
    'FATAL EXCEPTION',
    'AndroidRuntime',
    'No assemblies found',
    'Fast Deployment',
    'Unable to start activity',
    'IllegalStateException',
    'Force finishing',
    'Process .* has died',
    'Unhandled Exception',
    'F monodroid',
    'Abort at monodroid'
)

if ($logScopeProcess) {
    $rawLogs = & $adb logcat --pid=$logScopeProcess -d -t 400
}
else {
    $rawLogs = & $adb logcat -d -t 400
}
$filtered = $rawLogs | Select-String -Pattern ($patterns -join '|')

if ($filtered) {
    Write-Warn "发现潜在错误日志（请关注以下输出）:"
    $filtered
}
else {
    Write-Ok "未发现关键错误日志"
}

if ($ShowFullLogcat) {
    Write-Step "输出完整近期日志（最后 400 行）"
    $rawLogs
}

Write-Host ""
Write-Ok "一键联调完成"
Write-Host "- APK: $($apk.FullName)"
Write-Host "- Package: $PackageId"
Write-Host "- Activity: $component"
