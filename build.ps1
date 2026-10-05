param([Parameter(Mandatory = $true)][string]$GamePath, [string]$OutputPath, [switch]$SkipBuild)
$ErrorActionPreference = 'Stop'
$taskVersion = '1.0.2'
$taskManaged = Join-Path $GamePath 'Aistalt_Data\Managed'
if (-not (Test-Path -LiteralPath (Join-Path $taskManaged 'InfantGod.dll'))) { throw 'GamePath 应指向 Aistalt.exe 所在目录。' }
if (-not $OutputPath) { $OutputPath = Join-Path $PSScriptRoot "dist\InfantGod-SaveManager-v$taskVersion" }
$OutputPath = [IO.Path]::GetFullPath($OutputPath)
if ((Test-Path -LiteralPath $OutputPath) -and (Get-ChildItem -LiteralPath $OutputPath -Force | Select-Object -First 1)) { throw '输出目录已有内容，请指定新的 OutputPath。' }
$taskSource = Join-Path $PSScriptRoot 'src\plugin'
$taskDependencies = Join-Path $taskSource 'deps'
if (-not (Test-Path -LiteralPath (Join-Path $taskDependencies 'bepinex\BepInEx\core\BepInEx.dll'))) {
    & python (Join-Path $taskSource 'bootstrap.py')
    if ($LASTEXITCODE -ne 0) { throw 'BepInEx 加载器准备失败。' }
}
New-Item -ItemType Directory -Path (Join-Path $taskDependencies 'feed') -Force | Out-Null
if (-not $SkipBuild) {
    $taskOldCliHome = $env:DOTNET_CLI_HOME
    $taskOldTelemetry = $env:DOTNET_CLI_TELEMETRY_OPTOUT
    $env:DOTNET_CLI_HOME = Join-Path $taskDependencies 'dotnet-home'; $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    try {
        & dotnet build (Join-Path $taskSource 'InfantGodSaveManager.csproj') -c Release "-p:GameManagedPath=$taskManaged" --nologo --verbosity minimal
        if ($LASTEXITCODE -ne 0) { throw '多存档管理插件编译失败。' }
    } finally { $env:DOTNET_CLI_HOME = $taskOldCliHome; $env:DOTNET_CLI_TELEMETRY_OPTOUT = $taskOldTelemetry }
}
$taskPlugin = Join-Path $OutputPath 'BepInEx\plugins\InfantGodSaveManager'
New-Item -ItemType Directory -Path $taskPlugin,(Join-Path $OutputPath 'LICENSES') -Force | Out-Null
$taskAssembly = Join-Path $taskSource 'bin\Release\netstandard2.1\Graywill.InfantGodSaveManager.dll'
Copy-Item -LiteralPath $taskAssembly -Destination $taskPlugin
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'data\scene-labels.json') -Destination $taskPlugin
$taskLoader = Join-Path $taskDependencies 'bepinex'
Copy-Item -LiteralPath (Join-Path $taskLoader 'BepInEx\core') -Destination (Join-Path $OutputPath 'BepInEx') -Recurse
foreach ($taskName in @('winhttp.dll','doorstop_config.ini','.doorstop_version')) { Copy-Item -LiteralPath (Join-Path $taskLoader $taskName) -Destination $OutputPath }
foreach ($taskName in @('Install-Mod.ps1','Uninstall-Mod.ps1','Installer-Common.ps1','README.md','ATTRIBUTIONS.txt','THIRD-PARTY-NOTICES.md','LICENSE')) { Copy-Item -LiteralPath (Join-Path $PSScriptRoot $taskName) -Destination $OutputPath }
foreach ($taskLicense in Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'LICENSES') -File) { Copy-Item -LiteralPath $taskLicense.FullName -Destination (Join-Path $OutputPath 'LICENSES') }
$taskZip = $OutputPath + '.zip'
if (Test-Path -LiteralPath $taskZip) { throw '同名 ZIP 已存在，请指定新的 OutputPath。' }
Compress-Archive -Path (Join-Path $OutputPath '*') -DestinationPath $taskZip -CompressionLevel Optimal
Write-Output "手动安装目录：$OutputPath"
Write-Output "Mod ZIP：$taskZip"
