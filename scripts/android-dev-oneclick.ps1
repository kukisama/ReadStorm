
param(
    [Parameter(Position = 0)]
    [string]$QuickScenario,
<#
1	安卓 + 桌面 Release 交付包
2	安卓 Debug + 模拟器联调（完整日志）
3	安卓 Debug + 真机联调（完整日志）
4	安卓 FastDebug 快速打包（构建冒烟）
5	安卓 Debug 交付包（仅打包，不安装）
6	桌面 Debug 打包
手动安装apk示例：
adb -s UWAYYTOFX8FILBS4 install -r -d -t "C:\你的\路径\app.apk"
# adb -s UWAYYTOFX8FILBS4 install -r -d -t "c:\Scripts\ReadStorm\publish\android\release\ReadStorm-release.apk"
#>
    [ValidateSet('1', '2', '3')]
    [string]$Mode = '1', # 1=安卓 2=桌面 3=全部
    [switch]$PackageOnly = $false, #只打包APK，不执行安装和联调

    [string]$Project = "src/ReadStorm.Android/ReadStorm.Android.csproj",
    [string]$DesktopProject = "src/ReadStorm.Desktop/ReadStorm.Desktop.csproj",
    [string]$Configuration = "release",
    [string]$PackageId = "com.readstorm.app",
    [string]$AvdName = "ReadStorm_API34",
    [string[]]$SharedAvdCandidates = @("ReadStorm_API34", "PhonoArk_API34", "Pixel_7_API_34", "Pixel_6_API_34"),
    [int]$BootTimeoutSeconds = 180,
    [switch]$SkipBuild,
    [switch]$NoEmulator,
    [switch]$ShowFullLogcat,
    [string]$OutputApkDir,
    [bool]$FastDebug = $true,# 极速调试包模式：生成签名调试包（Debug 配置、禁用链接器、禁用 AOT），加速开发调试迭代
    [bool]$ForceRepackIfStale = $true, # FastDebug 下若未产出本次新 APK，则自动 Clean + 重打包
    [bool]$AggressiveBuild = $true, # 激进并行构建：尽可能提高 CPU 利用率
    [bool]$PreferPCore = $true, # 默认优先绑定到性能核（P-core）
    [int]$MaxCpu = 0 # 0=自动使用 Floor(物理核*0.85)
)

$ErrorActionPreference = "Stop"

$script:PCoreLogicalIds = @()
$script:PCoreAffinityMask = $null
$script:QuickDeviceCheckOnly = $false

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

function Apply-QuickScenarioPreset([string]$quickScenario) {
    if ([string]::IsNullOrWhiteSpace($quickScenario)) {
        return
    }

    switch ($quickScenario) {
        '1' {
            # 交付安卓+桌面 Release 包（给用户）
            $script:Mode = '3'
            $script:Configuration = 'Release'
            $script:PackageOnly = $true
            $script:FastDebug = $false
            $script:NoEmulator = $true
            $script:ShowFullLogcat = $false
            $script:SkipBuild = $false
            Write-Ok "快捷场景 1：安卓+桌面 Release 交付包"
        }
        '2' {
            # 安卓 Debug + 模拟器联调 + 完整日志
            $script:Mode = '1'
            $script:Configuration = 'Debug'
            $script:PackageOnly = $false
            $script:FastDebug = $false
            $script:NoEmulator = $false
            $script:ShowFullLogcat = $true
            $script:SkipBuild = $false
            Write-Ok "快捷场景 2：安卓 Debug + 模拟器联调（含完整日志）"
        }
        '3' {
            # 安卓 Debug + 真机联调 + 完整日志
            $script:Mode = '1'
            $script:Configuration = 'Debug'
            $script:PackageOnly = $false
            $script:FastDebug = $false
            $script:NoEmulator = $true
            $script:ShowFullLogcat = $true
            $script:SkipBuild = $false
            Write-Ok "快捷场景 3：安卓 Debug + 真机联调（含完整日志）"
        }
        '4' {
            # 安卓快速打包（构建冒烟）
            $script:Mode = '1'
            $script:Configuration = 'Debug'
            $script:PackageOnly = $true
            $script:FastDebug = $true
            $script:NoEmulator = $true
            $script:ShowFullLogcat = $false
            $script:SkipBuild = $false
            Write-Ok "快捷场景 4：安卓 FastDebug 快速打包（构建冒烟）"
        }
        '5' {
            # 安卓 Debug 交付包（仅打包，不安装）
            $script:Mode = '1'
            $script:Configuration = 'Debug'
            $script:PackageOnly = $true
            $script:FastDebug = $false
            $script:NoEmulator = $true
            $script:ShowFullLogcat = $false
            $script:SkipBuild = $false
            Write-Ok "快捷场景 5：安卓 Debug 交付包（仅打包）"
        }
        '6' {
            # 桌面 Debug 打包
            $script:Mode = '2'
            $script:Configuration = 'Debug'
            $script:PackageOnly = $true
            $script:FastDebug = $false
            $script:NoEmulator = $true
            $script:ShowFullLogcat = $false
            $script:SkipBuild = $false
            Write-Ok "快捷场景 6：桌面 Debug 打包"
        }
        default {
            throw "无效快捷场景: $quickScenario。可用值: 1/2/3/4/5/6"
        }
    }
}

function Ensure-CpuSetNativeType {
    if ('CpuSetNative' -as [type]) {
        return $true
    }

    try {
        $src = @"
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;

public static class CpuSetNative
{
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct SYSTEM_CPU_SET_INFORMATION
    {
        public UInt32 Size;
        public int Type;
        public SYSTEM_CPU_SET CpuSet;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct SYSTEM_CPU_SET
    {
        public UInt32 Id;
        public UInt16 Group;
        public byte LogicalProcessorIndex;
        public byte CoreIndex;
        public byte LastLevelCacheIndex;
        public byte NumaNodeIndex;
        public byte EfficiencyClass;
        public byte AllFlags;
        public byte SchedulingClass;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
        public byte[] Padding;
        public UInt64 AllocationTag;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemCpuSetInformation(
        IntPtr info,
        int len,
        ref int retLen,
        IntPtr process,
        uint flags);

    public static SYSTEM_CPU_SET[] GetCpuSets()
    {
        int bytes = 0;
        GetSystemCpuSetInformation(IntPtr.Zero, 0, ref bytes, IntPtr.Zero, 0);
        if (bytes <= 0)
        {
            return Array.Empty<SYSTEM_CPU_SET>();
        }

        IntPtr buffer = Marshal.AllocHGlobal(bytes);
        try
        {
            if (!GetSystemCpuSetInformation(buffer, bytes, ref bytes, IntPtr.Zero, 0))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            var list = new List<SYSTEM_CPU_SET>();
            int offset = 0;
            while (offset < bytes)
            {
                IntPtr p = IntPtr.Add(buffer, offset);
                var info = Marshal.PtrToStructure<SYSTEM_CPU_SET_INFORMATION>(p);
                if (info.Type == 0)
                {
                    list.Add(info.CpuSet);
                }

                if (info.Size == 0)
                {
                    break;
                }

                offset += (int)info.Size;
            }

            return list.ToArray();
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }
}
"@

        Add-Type -TypeDefinition $src -ErrorAction Stop
        return $true
    }
    catch {
        return $false
    }
}

function Get-CpuSetCoreClassification {
    $rows = @()

    # 感谢 Florian Zimmer 的 Retrieve-IntelCPUCoreEfficiencyClass 项目提供思路：
    # https://github.com/FlorianZimmer/Retrieve-IntelCPUCoreEfficiencyClass

    if (-not (Ensure-CpuSetNativeType)) {
        return $rows
    }

    try {
        $sets = [CpuSetNative]::GetCpuSets()
        if (-not $sets -or $sets.Count -eq 0) {
            return $rows
        }

        foreach ($s in $sets) {
            if ($null -eq $s) {
                continue
            }

            $rows += [PSCustomObject]@{
                CPU       = [int]$s.LogicalProcessorIndex
                CoreIndex = [int]$s.CoreIndex
                EClass    = [int]$s.EfficiencyClass
                SClass    = [int]$s.SchedulingClass
                CoreType  = "Unknown"
            }
        }

        if (-not $rows -or $rows.Count -eq 0) {
            return @()
        }

        $maxEClass = ($rows | Measure-Object -Property EClass -Maximum).Maximum
        $minEClass = ($rows | Measure-Object -Property EClass -Minimum).Minimum

        if ($maxEClass -ne $minEClass) {
            $rows | ForEach-Object {
                if ($_.EClass -eq $maxEClass) {
                    $_.CoreType = "P"
                }
                elseif ($_.EClass -eq $minEClass -and $_.SClass -eq 0) {
                    $_.CoreType = "LP-E"
                }
                else {
                    $_.CoreType = "E"
                }
            }
        }

        return @($rows | Sort-Object -Property CPU)
    }
    catch {
        return @()
    }
}

function Get-PerformanceCoreLogicalProcessorIds {
    $ids = @()

    $cpuSetRows = Get-CpuSetCoreClassification
    if ($cpuSetRows -and $cpuSetRows.Count -gt 0) {
        $distinctEClass = @($cpuSetRows | Select-Object -ExpandProperty EClass -Unique)
        if ($distinctEClass.Count -gt 1) {
            $ids = $cpuSetRows |
            Where-Object { $_.CoreType -eq 'P' } |
            Sort-Object -Property CPU |
            Select-Object -ExpandProperty CPU
            return @($ids)
        }
    }

    try {
        $cpuRegPath = "HKLM:\HARDWARE\DESCRIPTION\System\CentralProcessor"
        if (-not (Test-Path $cpuRegPath)) {
            return $ids
        }

        $coreKeys = Get-ChildItem -Path $cpuRegPath -ErrorAction SilentlyContinue |
        Where-Object { $_.PSChildName -match '^\d+$' }
        if (-not $coreKeys) {
            return $ids
        }

        $entries = @()
        foreach ($k in $coreKeys) {
            $p = Get-ItemProperty -Path $k.PSPath -ErrorAction SilentlyContinue
            if ($null -eq $p -or -not ($p.PSObject.Properties.Name -contains 'EfficiencyClass')) {
                continue
            }

            $entries += [PSCustomObject]@{
                Id              = [int]$k.PSChildName
                EfficiencyClass = [int]$p.EfficiencyClass
            }
        }

        if (-not $entries) {
            return $ids
        }

        $maxEff = ($entries | Measure-Object -Property EfficiencyClass -Maximum).Maximum
        $ids = $entries |
        Where-Object { $_.EfficiencyClass -eq $maxEff } |
        Sort-Object -Property Id |
        ForEach-Object { $_.Id }
    }
    catch {
        # 仅影响 P 核亲和性，不阻断主流程
    }

    return @($ids)
}

function Get-AffinityMaskFromLogicalIds([int[]]$logicalIds) {
    if (-not $logicalIds -or $logicalIds.Count -eq 0) {
        return $null
    }

    if ([IntPtr]::Size -lt 8) {
        Write-Warn "当前 PowerShell 不是 64 位进程，跳过亲和性绑定"
        return $null
    }

    $mask = [uint64]0
    foreach ($id in $logicalIds) {
        if ($id -lt 0 -or $id -ge 64) {
            Write-Warn "逻辑线程 ID=$id 超出单进程亲和性位图范围(0-63)，跳过 P 核亲和性绑定"
            return $null
        }

        $mask = $mask -bor ([uint64]1 -shl $id)
    }

    return $mask
}

function Initialize-PCorePreference([pscustomobject]$cpuTopology) {
    $script:PCoreLogicalIds = @()
    $script:PCoreAffinityMask = $null

    if (-not $PreferPCore) {
        Write-Warn "PreferPCore 已关闭：构建进程不做 P 核亲和性绑定"
        return
    }

    $pCoreIds = Get-PerformanceCoreLogicalProcessorIds
    if (-not $pCoreIds -or $pCoreIds.Count -eq 0) {
        if ($cpuTopology.HybridDetected) {
            Write-Warn "检测到混合架构，但无法读取 P 核线程列表；将仅设置高优先级，不做亲和性绑定"
        }
        else {
            Write-Warn "未检测到可区分的 P/E 核信息；将仅设置高优先级，不做亲和性绑定"
        }
        return
    }

    $affinity = Get-AffinityMaskFromLogicalIds -logicalIds $pCoreIds
    if ($null -eq $affinity) {
        Write-Warn "P 核亲和性位图计算失败；将仅设置高优先级"
        return
    }

    $script:PCoreLogicalIds = @($pCoreIds)
    $script:PCoreAffinityMask = $affinity
    Write-Ok "PreferPCore 已启用：P 核逻辑线程 ID = $($script:PCoreLogicalIds -join ', ')"
}

function Invoke-DotNetCommand([string[]]$dotnetArgs, [string]$errorMessage) {
    if (-not $PreferPCore) {
        Exec { & dotnet @dotnetArgs } $errorMessage
        return
    }

    $proc = Start-Process -FilePath "dotnet" -ArgumentList $dotnetArgs -NoNewWindow -PassThru

    try {
        $proc.PriorityClass = [System.Diagnostics.ProcessPriorityClass]::High
    }
    catch {
        Write-Warn "无法设置 dotnet 进程优先级为 High：$($_.Exception.Message)"
    }

    if ($null -ne $script:PCoreAffinityMask) {
        try {
            $proc.ProcessorAffinity = [IntPtr]$script:PCoreAffinityMask
        }
        catch {
            Write-Warn "设置 dotnet 进程 P 核亲和性失败：$($_.Exception.Message)"
        }
    }

    $proc.WaitForExit()
    if ($proc.ExitCode -ne 0) {
        throw "$errorMessage (exit=$($proc.ExitCode))"
    }
}

function Invoke-FastDebugPackageBuild([string]$projectPath) {
    $buildArgs = @(
        "build", $projectPath,
        "-c", "Debug",
        "-t:SignAndroidPackage",
        "-p:AndroidPackageFormats=apk",
        "-p:AndroidPackageFormat=apk",
        "-p:AndroidLinkMode=None",
        "-p:RunAOTCompilation=false",
        "-v", "minimal"
    ) + (Get-BuildTuningArgs -configuration "Debug" -forInstall:$false)

    Invoke-DotNetCommand -dotnetArgs $buildArgs -errorMessage "APK 调试包构建失败"
}

function Install-ApkWithAutoFix([string]$adbPath, [string]$apkPath, [string]$packageId) {
    $installOutput = (& $adbPath install -r $apkPath 2>&1 | Out-String)
    if ($LASTEXITCODE -eq 0) {
        Write-Ok "APK 安装成功"
        return
    }

    if ($installOutput -match 'INSTALL_FAILED_UPDATE_INCOMPATIBLE') {
        Write-Warn "检测到签名不一致：设备上已安装的 $packageId 与当前 APK 签名不同。"
        Write-Warn "将自动卸载旧包后重装（会清空该应用本地数据）。"

        $uninstallOutput = (& $adbPath uninstall $packageId 2>&1 | Out-String)
        if ($LASTEXITCODE -ne 0 -and $uninstallOutput -notmatch 'Unknown package') {
            throw "自动卸载旧包失败：$uninstallOutput"
        }

        $retryOutput = (& $adbPath install -r $apkPath 2>&1 | Out-String)
        if ($LASTEXITCODE -ne 0) {
            throw "自动重装失败：$retryOutput"
        }

        Write-Ok "已通过“卸载旧包 + 重装”完成安装"
        return
    }

    throw "adb install 失败：$installOutput"
}

function Get-CpuTopologyInfo {
    $cpuInfo = [ordered]@{
        Model             = "Unknown"
        PhysicalCores     = [Environment]::ProcessorCount
        LogicalProcessors = [Environment]::ProcessorCount
        PerformanceCores  = $null
        EfficiencyCores   = $null
        HybridDetected    = $false
    }

    try {
        $processors = Get-CimInstance -ClassName Win32_Processor -ErrorAction Stop
        if ($processors) {
            $names = $processors | ForEach-Object { $_.Name } | Where-Object { $_ }
            if ($names) {
                $cpuInfo.Model = (($names | Select-Object -Unique) -join " / ").Trim()
            }

            $physical = ($processors | Measure-Object -Property NumberOfCores -Sum).Sum
            $logical = ($processors | Measure-Object -Property NumberOfLogicalProcessors -Sum).Sum

            if ($physical -gt 0) {
                $cpuInfo.PhysicalCores = [int]$physical
            }
            if ($logical -gt 0) {
                $cpuInfo.LogicalProcessors = [int]$logical
            }
        }
    }
    catch {
        # 保持默认值兜底
    }

    # 尝试检测 Intel Hybrid（P/E）信息：
    # 1) 优先使用 GetSystemCpuSetInformation（可区分 P / E / LP-E）
    # 2) 兜底看注册表 EfficiencyClass
    try {
        $cpuSetRows = Get-CpuSetCoreClassification
        if ($cpuSetRows -and $cpuSetRows.Count -gt 0) {
            $distinctEClass = @($cpuSetRows | Select-Object -ExpandProperty EClass -Unique)
            if ($distinctEClass.Count -gt 1) {
                $cpuInfo.HybridDetected = $true

                $pCoreCount = @(
                    $cpuSetRows |
                    Where-Object { $_.CoreType -eq 'P' } |
                    Select-Object -ExpandProperty CoreIndex -Unique
                ).Count

                $eCoreCount = @(
                    $cpuSetRows |
                    Where-Object { $_.CoreType -in @('E', 'LP-E') } |
                    Select-Object -ExpandProperty CoreIndex -Unique
                ).Count

                if ($pCoreCount -gt 0) {
                    $cpuInfo.PerformanceCores = [int]$pCoreCount
                }
                if ($eCoreCount -gt 0) {
                    $cpuInfo.EfficiencyCores = [int]$eCoreCount
                }

                return [PSCustomObject]$cpuInfo
            }
        }

        $cpuRegPath = "HKLM:\HARDWARE\DESCRIPTION\System\CentralProcessor"
        if (Test-Path $cpuRegPath) {
            $coreKeys = Get-ChildItem -Path $cpuRegPath -ErrorAction SilentlyContinue |
            Where-Object { $_.PSChildName -match '^\d+$' }

            if ($coreKeys) {
                $effClasses = @()
                foreach ($k in $coreKeys) {
                    $p = Get-ItemProperty -Path $k.PSPath -ErrorAction SilentlyContinue
                    if ($null -ne $p -and $p.PSObject.Properties.Name -contains 'EfficiencyClass') {
                        $effClasses += [int]$p.EfficiencyClass
                    }
                }

                $distinct = $effClasses | Select-Object -Unique
                if ($distinct.Count -gt 1) {
                    $cpuInfo.HybridDetected = $true
                }
            }
        }
    }
    catch {
        # 仅影响展示，不阻断主流程
    }

    return [PSCustomObject]$cpuInfo
}

function Get-RecommendedMaxCpu([int]$physicalCores) {
    if ($physicalCores -lt 1) {
        return 1
    }

    # 目标：最短总耗时优先，默认采用 85% * 物理核并向下取整
    $recommended = [math]::Floor($physicalCores * 0.85)
    if ($recommended -lt 1) {
        $recommended = 1
    }
    return [int]$recommended
}

function Get-BuildTuningArgs([string]$configuration, [bool]$forInstall = $false) {
    $tuningArgs = @()
    if (-not $AggressiveBuild) {
        return $tuningArgs
    }

    $cpuTopology = Get-CpuTopologyInfo
    $cpu = if ($MaxCpu -gt 0) { $MaxCpu } else { Get-RecommendedMaxCpu -physicalCores $cpuTopology.PhysicalCores }
    if ($cpu -lt 1) {
        $cpu = 1
    }

    # 强制并行节点 + 关闭节点复用（让单次构建尽量吃满 CPU）
    $tuningArgs += "-m:$cpu"
    $tuningArgs += "-nr:false"
    $tuningArgs += "-p:BuildInParallel=true"
    $tuningArgs += "-p:AndroidUseAapt2Daemon=true"

    # 仅“纯打包不安装”时允许关闭嵌入以加速；
    # 若后续要 adb install 并运行，必须嵌入程序集，否则会出现
    # “No assemblies found ... Assuming this is part of Fast Deployment” 启动崩溃。
    if ($configuration -eq "Debug" -and -not $forInstall) {
        $tuningArgs += "-p:EmbedAssembliesIntoApk=false"
    }

    return $tuningArgs
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

function Invoke-DeviceConnectivityCheck {
    $sdkRoot = Get-SdkRoot
    $adbPath = Join-Path $sdkRoot "platform-tools\adb.exe"
    if (!(Test-Path $adbPath)) {
        throw "未找到 adb: $adbPath"
    }

    Write-Step "执行设备连通性检查"
    Exec { & $adbPath start-server | Out-Null } "adb start-server 失败"

    $devices = & $adbPath devices
    Write-Host ($devices -join [Environment]::NewLine)

    $online = $devices | Where-Object { $_ -match '^\S+\s+device$' }
    if (-not $online -or $online.Count -eq 0) {
        throw "未检测到在线设备（device）"
    }

    foreach ($line in $online) {
        $parts = ($line -split '\s+') | Where-Object { $_ }
        $serial = $parts[0]
        $model = (& $adbPath -s $serial shell getprop ro.product.model 2>$null).Trim()
        if ([string]::IsNullOrWhiteSpace($model)) {
            $model = "unknown"
        }
        Write-Ok "设备在线: serial=$serial, model=$model"
    }
}

function Resolve-TargetAvdName([string]$emulatorPath, [string]$requestedAvdName, [string[]]$sharedCandidates) {
    $existingAvds = @(& $emulatorPath -list-avds)
    if (-not $existingAvds -or $existingAvds.Count -eq 0) {
        throw "当前环境未发现可用 AVD。请先在 Android Studio 创建模拟器。"
    }

    if (-not [string]::IsNullOrWhiteSpace($requestedAvdName)) {
        if ($existingAvds -contains $requestedAvdName) {
            return $requestedAvdName
        }

        Write-Warn "指定的 AVD '$requestedAvdName' 不存在，将尝试共享候选 AVD。"
    }

    foreach ($candidate in $sharedCandidates) {
        if (-not [string]::IsNullOrWhiteSpace($candidate) -and $existingAvds -contains $candidate) {
            return $candidate
        }
    }

    return $existingAvds[0]
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

function Resolve-ProjectPath([string]$repoRoot, [string]$projectPath) {
    if ([string]::IsNullOrWhiteSpace($projectPath)) {
        throw "项目路径不能为空"
    }

    if ([System.IO.Path]::IsPathRooted($projectPath)) {
        return [System.IO.Path]::GetFullPath($projectPath)
    }

    return [System.IO.Path]::GetFullPath((Join-Path $repoRoot $projectPath))
}

function Publish-DesktopApp {
    param(
        [string]$Configuration = "Release",
        [string]$DesktopProjectPath
    )
    $repoRoot = Split-Path -Parent $PSScriptRoot
    $outputDir = Join-Path $repoRoot "publish/desktop/$Configuration"
    Write-Step "发布桌面应用 ($Configuration)"
    $publishArgs = @(
        "publish", $DesktopProjectPath,
        "-c", $Configuration,
        "-r", "win-x64",
        "--self-contained", "false",
        "-p:PublishSingleFile=false",
        "-p:SelfContained=false",
        "-o", $outputDir
    )
    Invoke-DotNetCommand -dotnetArgs $publishArgs -errorMessage "桌面端发布失败"
    Write-Ok "桌面端发布完成(FDD)：$outputDir"
}

$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

$androidProjectPath = Resolve-ProjectPath -repoRoot $repoRoot -projectPath $Project
if (!(Test-Path $androidProjectPath)) {
    throw "未找到 Android 项目文件: $androidProjectPath"
}

$desktopProjectPath = Resolve-ProjectPath -repoRoot $repoRoot -projectPath $DesktopProject
if (!(Test-Path $desktopProjectPath)) {
    throw "未找到 Desktop 项目文件: $desktopProjectPath"
}

$androidProjectDir = Split-Path -Parent $androidProjectPath
$androidProjectName = [System.IO.Path]::GetFileNameWithoutExtension($androidProjectPath)
$androidArtifactName = if ($androidProjectName.EndsWith(".Android", [System.StringComparison]::OrdinalIgnoreCase)) {
    $androidProjectName.Substring(0, $androidProjectName.Length - ".Android".Length)
}
else {
    $androidProjectName
}

Apply-QuickScenarioPreset -quickScenario $QuickScenario

if ($script:QuickDeviceCheckOnly) {
    Invoke-DeviceConnectivityCheck
    return
}

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
    Publish-DesktopApp -Configuration $Configuration -DesktopProjectPath $desktopProjectPath
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


$effectiveAndroidConfiguration = $Configuration
if ($PackageOnly -and $FastDebug) {
    if ($Configuration -ine "Debug") {
        Write-Warn "FastDebug 固定使用 Debug 打包。当前 Configuration='$Configuration' 将自动改为 Debug，以避免读取旧目录 APK。"
    }
    $effectiveAndroidConfiguration = "Debug"
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
            $resolvedAvdName = Resolve-TargetAvdName -emulatorPath $emulator -requestedAvdName $AvdName -sharedCandidates $SharedAvdCandidates
            Write-Step "未发现在线模拟器，启动 AVD: $resolvedAvdName"

            # 将模拟器输出重定向到日志文件，便于排查启动失败
            $emulatorLog = Join-Path $repoRoot "publish\emulator-startup.log"
            $emulatorLogDir = Split-Path $emulatorLog -Parent
            if (!(Test-Path $emulatorLogDir)) {
                New-Item -ItemType Directory -Path $emulatorLogDir -Force | Out-Null
            }

            Write-Step "模拟器日志将写入: $emulatorLog"
            $emulatorProc = Start-Process -FilePath $emulator `
                -ArgumentList @("-avd", $resolvedAvdName, "-no-snapshot-load", "-gpu", "swiftshader_indirect", "-no-metrics") `
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
    $buildStartAt = Get-Date
    $cpuTopology = Get-CpuTopologyInfo
    Write-Ok "CPU 型号: $($cpuTopology.Model)"
    Write-Ok "核心信息: 物理核=$($cpuTopology.PhysicalCores), 逻辑线程=$($cpuTopology.LogicalProcessors)"
    if ($cpuTopology.HybridDetected -and $null -ne $cpuTopology.PerformanceCores -and $null -ne $cpuTopology.EfficiencyCores) {
        Write-Ok "混合架构检测: 性能核(P)≈$($cpuTopology.PerformanceCores), 能效核(E)≈$($cpuTopology.EfficiencyCores)"
    }
    else {
        Write-Warn "混合架构(P/E)细分未检测到或不适用（将仅使用物理核总数进行并行推荐）"
    }

    Initialize-PCorePreference -cpuTopology $cpuTopology
    if ($PreferPCore -and $null -ne $script:PCoreAffinityMask) {
        Write-Ok "构建进程将优先绑定到 P 核并以 High 优先级运行"
    }
    elseif ($PreferPCore) {
        Write-Warn "PreferPCore 已开启，但当前仅可应用 High 优先级（未绑定亲和性）"
    }

    if ($AggressiveBuild) {
        $effectiveCpu = if ($MaxCpu -gt 0) { $MaxCpu } else { Get-RecommendedMaxCpu -physicalCores $cpuTopology.PhysicalCores }
        Write-Ok "激进并行模式已启用 (并行节点=$effectiveCpu，策略=Floor(物理核*0.85)，Debug 纯打包默认 EmbedAssembliesIntoApk=false)"
    }

    if ($PackageOnly) {
        if ($FastDebug) {
            Write-Step "极速调试包模式：签名 + 禁用链接器加速"
            Invoke-FastDebugPackageBuild -projectPath $androidProjectPath
        }
        else {
            Write-Step "生成可分发 APK ($effectiveAndroidConfiguration)"
            $buildArgs = @(
                "build", $androidProjectPath,
                "-c", $effectiveAndroidConfiguration,
                "-t:SignAndroidPackage",
                "-p:AndroidPackageFormats=apk",
                "-p:AndroidPackageFormat=apk",
                "-v", "minimal"
            ) + (Get-BuildTuningArgs -configuration $effectiveAndroidConfiguration -forInstall:$false)
            Invoke-DotNetCommand -dotnetArgs $buildArgs -errorMessage "APK 打包失败"
        }
    }
    else {
        Write-Step "生成用于安装联调的 APK ($effectiveAndroidConfiguration，嵌入程序集)"
        $buildArgs = @(
            "build", $androidProjectPath,
            "-c", $effectiveAndroidConfiguration,
            "-t:SignAndroidPackage",
            "-p:AndroidPackageFormats=apk",
            "-p:AndroidPackageFormat=apk",
            "-p:EmbedAssembliesIntoApk=true",
            "-p:AndroidUseSharedRuntime=false",
            "-v", "minimal"
        ) + (Get-BuildTuningArgs -configuration $effectiveAndroidConfiguration -forInstall:$true)
        Invoke-DotNetCommand -dotnetArgs $buildArgs -errorMessage "安装联调 APK 打包失败"
    }
}
else {
    Write-Warn "已启用 -SkipBuild，跳过构建"
}

$apkDir = Join-Path $androidProjectDir "bin\$effectiveAndroidConfiguration\net10.0-android"
if (!(Test-Path $apkDir)) {
    throw "未找到 APK 输出目录: $apkDir"
}


# 优先找 Signed.apk，兜底找任意 apk
$apkCandidates = Get-ChildItem -Path $apkDir -Filter "*.apk" -File -ErrorAction SilentlyContinue |
Sort-Object LastWriteTime -Descending

if ($PackageOnly) {
    $signedCandidates = @($apkCandidates | Where-Object { $_.Name -like "*Signed.apk" })
    $recentCutoff = if ($buildStartAt) { $buildStartAt.AddSeconds(-10) } else { $null }

    if ($recentCutoff) {
        $apk = $signedCandidates | Where-Object { $_.LastWriteTime -ge $recentCutoff } | Select-Object -First 1

        if (-not $apk -and $FastDebug -and $ForceRepackIfStale -and -not $SkipBuild) {
            Write-Warn "FastDebug 未检测到本次新生成 Signed.apk，执行 Clean + 重打包兜底"
            $cleanArgs = @("clean", $androidProjectPath, "-c", "Debug", "-v", "minimal")
            Invoke-DotNetCommand -dotnetArgs $cleanArgs -errorMessage "FastDebug 清理失败"
            Invoke-FastDebugPackageBuild -projectPath $androidProjectPath

            $signedCandidates = @(Get-ChildItem -Path $apkDir -Filter "*Signed.apk" -File -ErrorAction SilentlyContinue |
                Sort-Object LastWriteTime -Descending)
            $apk = $signedCandidates | Where-Object { $_.LastWriteTime -ge $recentCutoff } | Select-Object -First 1
        }
    }

    if (-not $apk) {
        Write-Warn "未找到本次新生成 Signed.apk，将回退使用输出目录中最新 Signed.apk"
        $apk = $signedCandidates | Select-Object -First 1
    }
}
else {
    $recentCutoff = if ($buildStartAt) { $buildStartAt.AddSeconds(-10) } else { (Get-Date).AddDays(-3650) }
    $apk = $apkCandidates | Where-Object { $_.LastWriteTime -ge $recentCutoff } | Select-Object -First 1

    if (-not $apk) {
        Write-Warn "未找到本次新生成 APK，将回退使用输出目录中最新 APK"
        $apk = $apkCandidates | Select-Object -First 1
    }
}

if (-not $apk) {
    throw "未找到 APK 文件：$apkDir"
}

if ($PackageOnly) {
    if (-not $OutputApkDir) {
        $OutputApkDir = Join-Path $repoRoot "publish\android\$effectiveAndroidConfiguration"
    }

    if (!(Test-Path $OutputApkDir)) {
        New-Item -ItemType Directory -Path $OutputApkDir -Force | Out-Null
    }

    $finalApk = Join-Path $OutputApkDir ($androidArtifactName + "-" + $effectiveAndroidConfiguration.ToLowerInvariant() + ".apk")
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
Install-ApkWithAutoFix -adbPath $adb -apkPath $apk.FullName -packageId $PackageId

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
