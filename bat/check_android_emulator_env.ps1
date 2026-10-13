# [REA Tool Mods] Android emulator (Nox) environment check
# Checks: CPU virtualization (VT-x / AMD-V), Hyper-V family features that hold VT,
#         Nox install + process, ADB connection and device ABI.
# Exit code: 0 = ready, 1 = blocking problem, 2 = ready with warnings
param(
    [int[]]$Ports = @(62001, 62025, 62026)
)

$ErrorActionPreference = 'SilentlyContinue'
$script:fail = 0
$script:warn = 0

function Ok($msg)   { Write-Host "  [ OK ] $msg" -ForegroundColor Green }
function Warn($msg) { Write-Host "  [WARN] $msg" -ForegroundColor Yellow; $script:warn++ }
function Fail($msg) { Write-Host "  [FAIL] $msg" -ForegroundColor Red; $script:fail++ }
function Info($msg) { Write-Host "         $msg" -ForegroundColor Gray }

Write-Host "==================================================================="
Write-Host "  [REA Tool Mods] Android emulator (Nox) environment check"
Write-Host "==================================================================="
Write-Host ""

# -----------------------------------------------------------------
# 1. CPU virtualization
# -----------------------------------------------------------------
Write-Host "[1/5] CPU virtualization (Intel VT-x / AMD-V)"
$cpu = Get-CimInstance Win32_Processor | Select-Object -First 1
$cs = Get-CimInstance Win32_ComputerSystem
Info "CPU: $("$($cpu.Name)".Trim())"

if ($cs.HypervisorPresent) {
    # Windows does not report VirtualizationFirmwareEnabled while a hypervisor owns VT,
    # but a running hypervisor proves VT is enabled in firmware.
    Ok "VT is enabled in BIOS/UEFI (a Windows hypervisor is already running)."
    Warn "VT is currently owned by the Hyper-V platform (see step 2)."
} elseif ($cpu.VirtualizationFirmwareEnabled) {
    Ok "VT is enabled in BIOS/UEFI and free for Nox."
} else {
    Fail "VT is DISABLED in BIOS/UEFI."
    Info "Reboot into BIOS/UEFI and enable 'Intel Virtualization Technology (VT-x)'"
    Info "or 'SVM Mode' (AMD), then run this check again."
}
Write-Host ""

# -----------------------------------------------------------------
# 2. Hyper-V family features (they take VT away from Nox)
# -----------------------------------------------------------------
Write-Host "[2/5] Windows features that use VT (Hyper-V family)"
$featureNames = @{
    'Microsoft-Hyper-V-Hypervisor'  = 'Hyper-V'
    'VirtualMachinePlatform'        = 'Virtual Machine Platform (WSL2)'
    'HypervisorPlatform'            = 'Windows Hypervisor Platform'
    'Containers-DisposableClientVM' = 'Windows Sandbox'
}
$filter = ($featureNames.Keys | ForEach-Object { "Name='$_'" }) -join ' OR '
$enabled = @(Get-CimInstance Win32_OptionalFeature -Filter $filter | Where-Object { $_.InstallState -eq 1 })
foreach ($f in $enabled) { Info "ON : $($featureNames[$f.Name])" }

$dg = Get-CimInstance -Namespace root\Microsoft\Windows\DeviceGuard -ClassName Win32_DeviceGuard
$vbsOn = $dg -and $dg.VirtualizationBasedSecurityStatus -eq 2
if ($vbsOn) { Info "ON : Virtualization-based security (Memory integrity / Credential Guard)" }

if ($enabled.Count -eq 0 -and -not $vbsOn) {
    Ok "No Hyper-V family feature is active."
} elseif ($cs.HypervisorPresent) {
    Info "Depending on the Nox build, Nox may fail to start or run very slowly while Hyper-V is active."
    Info "If Nox does not work, either install a Hyper-V compatible Nox build, or (as admin):"
    Info "  bcdedit /set hypervisorlaunchtype off   -> reboot"
    Info "  undo: bcdedit /set hypervisorlaunchtype auto -> reboot"
    Info "  Note: while off, WSL2, Docker (WSL2), Windows Sandbox and Memory integrity stop working."
} else {
    Info "These features are installed, but no hypervisor is running this boot, so Nox can use VT."
    Info "If you turned it off with bcdedit, WSL2 / Docker (WSL2) / Windows Sandbox stay unavailable until:"
    Info "  bcdedit /set hypervisorlaunchtype auto   -> reboot"
}
Write-Host ""

# -----------------------------------------------------------------
# 3. Nox installation
# -----------------------------------------------------------------
Write-Host "[3/5] Nox (NoxPlayer) installation"
$uninstallKeys = @(
    'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*',
    'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*',
    'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*'
)
$candidates = @()
foreach ($e in (Get-ItemProperty $uninstallKeys | Where-Object { $_.DisplayName -like '*Nox*' })) {
    if ($e.InstallLocation) { $candidates += $e.InstallLocation.Trim('"') }
    if ($e.DisplayIcon) { $candidates += (Split-Path ($e.DisplayIcon.Split(',')[0].Trim('"'))) }
    if ($e.UninstallString) { $candidates += (Split-Path ($e.UninstallString.Trim('"') -replace '(?i)(\.exe).*$', '$1')) }
}
$candidates += @("$env:ProgramFiles\Nox", "${env:ProgramFiles(x86)}\Nox", 'D:\Program Files\Nox', 'D:\Program Files (x86)\Nox')

$noxBin = $null
foreach ($c in $candidates) {
    if (-not $c) { continue }
    foreach ($d in @($c, (Join-Path $c 'bin'))) {
        if ((Test-Path (Join-Path $d 'Nox.exe')) -or (Test-Path (Join-Path $d 'nox_adb.exe'))) { $noxBin = $d; break }
    }
    if ($noxBin) { break }
}

if ($noxBin) {
    $ver = "$((Get-Item (Join-Path $noxBin 'Nox.exe')).VersionInfo.ProductVersion)".TrimStart('V', 'v', '.')
    Ok "Installed: $noxBin (version $ver)"
} else {
    Fail "Nox is not installed (or installed in an unknown folder)."
    Info "Download: https://www.bignox.com"
}
Write-Host ""

# -----------------------------------------------------------------
# 4. Nox process
# -----------------------------------------------------------------
Write-Host "[4/5] Nox process"
$noxRunning = [bool](Get-Process -Name Nox, NoxVMHandle, NoxVMSVC)
if ($noxRunning) {
    Ok "Nox is running."
} else {
    Warn "Nox is not running. Start Nox and run this check again to test ADB."
}
Write-Host ""

# -----------------------------------------------------------------
# 5. ADB connection
# -----------------------------------------------------------------
Write-Host "[5/5] ADB connection"
$adb = $null
if ($noxBin -and (Test-Path (Join-Path $noxBin 'nox_adb.exe'))) { $adb = Join-Path $noxBin 'nox_adb.exe' }
if (-not $adb) {
    $cmd = Get-Command adb
    if ($cmd) { $adb = $cmd.Source }
}

if (-not $adb) {
    Fail "No adb found (nox_adb.exe in the Nox folder, or Android SDK platform-tools on PATH)."
} else {
    Ok "adb: $adb"
    if ($noxRunning) {
        # 62001 = first Nox instance, 62025+ = multi-instance
        foreach ($p in $Ports) { & $adb connect "127.0.0.1:$p" 2>&1 | Out-Null }
    }

    $devices = @(& $adb devices 2>$null |
        Select-Object -Skip 1 |
        Where-Object { $_ -match "`tdevice$" } |
        ForEach-Object { ($_ -split "`t")[0] })

    if ($devices.Count -eq 0 -and -not $noxRunning) {
        Info "Device test skipped (Nox is not running)."
    } elseif ($devices.Count -eq 0) {
        Warn "Nox is running but no device is connected."
        Info "Wait for the Android home screen, then run this check again."
    } else {
        foreach ($d in $devices) {
            $model = (& $adb -s $d shell getprop ro.product.model 2>$null | Out-String).Trim()
            $release = (& $adb -s $d shell getprop ro.build.version.release 2>$null | Out-String).Trim()
            $abis = (& $adb -s $d shell getprop ro.product.cpu.abilist 2>$null | Out-String).Trim()
            Ok "$d  - $model, Android $release"
            Info "ABI: $abis"
            if ($abis -notmatch 'arm') {
                Warn "This instance reports no ARM ABI; ARM-only Unity builds (lib/arm64-v8a/libil2cpp.so) may not install."
            }
        }
        Write-Host ""
        Info "Next steps for an app you installed in Nox:"
        Info "  `"$adb`" -s <device> shell pm list packages -3"
        Info "  `"$adb`" -s <device> shell pm path <package>"
        Info "  `"$adb`" -s <device> pull <each path from pm path> <work folder>\apk"
        Info "  (split APKs: pull base.apk and every split_config.*.apk)"
    }
}

Write-Host ""
Write-Host "==================================================================="
if ($script:fail -gt 0) {
    Write-Host "  RESULT: $($script:fail) problem(s), $($script:warn) warning(s). Fix the [FAIL] items above." -ForegroundColor Red
    $code = 1
} elseif ($script:warn -gt 0) {
    Write-Host "  RESULT: usable, with $($script:warn) warning(s)." -ForegroundColor Yellow
    $code = 2
} else {
    Write-Host "  RESULT: emulator environment is ready." -ForegroundColor Green
    $code = 0
}
Write-Host "==================================================================="
exit $code
