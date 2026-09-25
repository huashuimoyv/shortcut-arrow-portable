$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
$destination = Join-Path (Split-Path -Parent $PSScriptRoot) '快捷方式箭头.exe'
& $compiler /nologo /target:winexe /platform:anycpu /optimize+ /codepage:65001 "/out:$destination" "/win32manifest:$PSScriptRoot\app.manifest" "/win32icon:$PSScriptRoot\app-icon.ico" "/resource:$PSScriptRoot\app-icon.ico,AppIcon" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Xml.dll "$PSScriptRoot\ArrowTool.cs" "$PSScriptRoot\IndividualIcons.cs"
if ($LASTEXITCODE -ne 0) { throw '编译失败' }
Get-Item -LiteralPath $destination | Select-Object FullName, Length
