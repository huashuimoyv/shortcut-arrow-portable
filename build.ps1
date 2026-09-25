$ErrorActionPreference = 'Stop'
$sourceFile = Get-ChildItem -LiteralPath $PSScriptRoot -Recurse -Filter "ArrowTool.cs" | Select-Object -First 1
if (-not $sourceFile) {
    throw "ArrowTool.cs not found under: $PSScriptRoot"
}
$srcDir = $sourceFile.Directory.FullName
$script = Join-Path $srcDir 'build.ps1'
if (Test-Path -LiteralPath $script) {
    & $script
} else {
    throw "Build script not found: $script"
}
