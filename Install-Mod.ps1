param([Parameter(Mandatory = $true)][string]$GamePath)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Installer-Common.ps1')
$taskGameRoot = Get-SaveManagerGameRoot $GamePath
$taskPlugin = 'BepInEx\plugins\InfantGodSaveManager'
$taskStatePath = Join-Path $taskGameRoot '.InfantGodSaveManager-install.json'
$taskExisting = $null
if (Test-Path -LiteralPath $taskStatePath) {
    $taskExisting = Get-Content -LiteralPath $taskStatePath -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($taskExisting.status -eq 'Uninstalled') { $taskExisting = $null }
}
if (-not (Test-Path -LiteralPath (Join-Path $PSScriptRoot "$taskPlugin\Graywill.InfantGodSaveManager.dll"))) { throw '请从编译后的安装包运行安装脚本。' }
$taskCore = Join-Path $taskGameRoot 'BepInEx\core\BepInEx.dll'
if (Test-Path -LiteralPath $taskCore) {
    if ([Diagnostics.FileVersionInfo]::GetVersionInfo($taskCore).FileMajorPart -ne 5) { throw '此 Mod 需要 BepInEx 5，当前加载器主版本不同。' }
} else {
    foreach ($taskName in @('winhttp.dll','doorstop_config.ini','.doorstop_version')) {
        if (Test-Path -LiteralPath (Join-Path $taskGameRoot $taskName)) { throw "已有未知加载器文件 $taskName，请先确认其来源。" }
    }
    New-Item -ItemType Directory -Path (Join-Path $taskGameRoot 'BepInEx') -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'BepInEx\core') -Destination (Join-Path $taskGameRoot 'BepInEx') -Recurse -Force
    foreach ($taskName in @('winhttp.dll','doorstop_config.ini','.doorstop_version')) { Copy-Item -LiteralPath (Join-Path $PSScriptRoot $taskName) -Destination $taskGameRoot -Force }
}
$taskBackup = Join-Path $taskGameRoot ('.InfantGodSaveManager-backup-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
$taskRecords = [Collections.Generic.List[object]]::new()
$taskRecorded = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
if ($taskExisting) {
    $taskBackup = [IO.Path]::GetFullPath([string]$taskExisting.backupRoot)
    if (-not $taskBackup.StartsWith($taskGameRoot + '\', [StringComparison]::OrdinalIgnoreCase)) { throw '备份目录不属于指定游戏。' }
    foreach ($taskRecord in $taskExisting.files) { $taskRecords.Add($taskRecord); $taskRecorded.Add([string]$taskRecord.path) | Out-Null }
}
New-Item -ItemType Directory -Path $taskBackup -Force | Out-Null
$taskState = [ordered]@{ status='Installing'; version='1.0.3'; backupRoot=$taskBackup; files=$taskRecords }
$taskSourceRoot = Join-Path $PSScriptRoot $taskPlugin
foreach ($taskFile in Get-ChildItem -LiteralPath $taskSourceRoot -File -Recurse) {
    $taskRelative = $taskPlugin + '\' + $taskFile.FullName.Substring($taskSourceRoot.Length + 1)
    $taskTarget = Get-SaveManagerChildPath $taskGameRoot $taskRelative
    if (-not $taskRecorded.Contains($taskRelative)) {
        $taskExists = Test-Path -LiteralPath $taskTarget
        if ($taskExists) {
            $taskSaved = Get-SaveManagerChildPath $taskBackup $taskRelative
            New-Item -ItemType Directory -Path (Split-Path -Parent $taskSaved) -Force | Out-Null
            Copy-Item -LiteralPath $taskTarget -Destination $taskSaved -Force
        }
        $taskRecords.Add([ordered]@{ path=$taskRelative; existed=$taskExists }); $taskRecorded.Add($taskRelative) | Out-Null
    }
    $taskState | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $taskStatePath -Encoding utf8
    New-Item -ItemType Directory -Path (Split-Path -Parent $taskTarget) -Force | Out-Null
    Copy-Item -LiteralPath $taskFile.FullName -Destination $taskTarget -Force
}
$taskState.status = 'Installed'; $taskState | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $taskStatePath -Encoding utf8
Write-Output '幼神多存档管理已安装。主菜单“载入游戏”或游戏内开始菜单“存档/读取”打开新版页面。'
Write-Output '原快速保存保留；“快速加载(F9)”读取当前槽最近一次普通保存或快速保存。'
