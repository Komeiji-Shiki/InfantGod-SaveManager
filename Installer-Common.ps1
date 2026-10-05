function Get-SaveManagerGameRoot([string]$GamePath) {
    $taskRoot = [IO.Path]::GetFullPath($GamePath).TrimEnd('\')
    if (-not (Test-Path -LiteralPath (Join-Path $taskRoot 'Aistalt.exe') -PathType Leaf)) { throw '请指定 Aistalt.exe 所在的 Build 目录。' }
    if (Get-Process -Name Aistalt -ErrorAction SilentlyContinue) { throw '请先关闭幼神，再安装或卸载。' }
    return $taskRoot
}
function Get-SaveManagerChildPath([string]$Root, [string]$Relative) {
    $taskPath = [IO.Path]::GetFullPath((Join-Path $Root $Relative))
    if (-not $taskPath.StartsWith($Root.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) { throw '文件路径超出了指定目录。' }
    return $taskPath
}
