param(
    [Parameter(Mandatory = $true)][string]$GamePath,
    [string]$SaveDataPath = (Join-Path ([Environment]::GetFolderPath('UserProfile')) 'AppData\LocalLow\Colsalley\Aistalt\SaveData')
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Installer-Common.ps1')
$taskGameRoot = Get-SaveManagerGameRoot $GamePath
$taskSettings = Join-Path ([IO.Path]::GetFullPath($SaveDataPath)) 'settings.es3'
if (Test-Path -LiteralPath $taskSettings) {
    try { $taskSaveSettings = Get-Content -LiteralPath $taskSettings -Raw -Encoding UTF8 | ConvertFrom-Json }
    catch { throw '无法读取原生 settings.es3。若游戏使用自定义存档目录，请通过 -SaveDataPath 指定实际 SaveData 目录。当前未卸载。' }
    if ($null -eq $taskSaveSettings.settings.value.activeSlotId) { throw '原生设置没有可识别的当前槽位，当前未卸载。' }
    if ([int]$taskSaveSettings.settings.value.activeSlotId -ge 3) {
        throw '当前使用新增槽位，原版只识别前三槽。请先启动游戏，读取原始三个槽中的一个并正常退出，再卸载。存档文件无需搬移，本脚本未改写任何存档。'
    }
}
$taskStatePath = Join-Path $taskGameRoot '.InfantGodSaveManager-install.json'
if (-not (Test-Path -LiteralPath $taskStatePath)) { throw '没有本 Mod 的安装记录，未改动游戏文件。' }
$taskState = Get-Content -LiteralPath $taskStatePath -Raw -Encoding UTF8 | ConvertFrom-Json
if ($taskState.status -eq 'Uninstalled') { Write-Output '本 Mod 已卸载。'; exit 0 }
$taskBackup = [IO.Path]::GetFullPath([string]$taskState.backupRoot)
if (-not $taskBackup.StartsWith($taskGameRoot + '\', [StringComparison]::OrdinalIgnoreCase)) { throw '备份目录不属于指定游戏。' }
foreach ($taskRecord in $taskState.files) {
    $taskTarget = Get-SaveManagerChildPath $taskGameRoot ([string]$taskRecord.path)
    if ($taskRecord.existed) {
        $taskSaved = Get-SaveManagerChildPath $taskBackup ([string]$taskRecord.path)
        if (-not (Test-Path -LiteralPath $taskSaved)) { throw "缺少原文件备份：$taskSaved" }
        Copy-Item -LiteralPath $taskSaved -Destination $taskTarget -Force
    } elseif (Test-Path -LiteralPath $taskTarget) { Remove-Item -LiteralPath $taskTarget -Force }
}
$taskState.status = 'Uninstalled'; $taskState | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $taskStatePath -Encoding utf8
Write-Output '多存档管理已卸载，原有文件已恢复。新增槽、手动点、缩略图和原生快照均保留。'
Write-Output '重新安装即可继续访问新增槽和手动点；共用加载器与其他 Mod 保留。'
