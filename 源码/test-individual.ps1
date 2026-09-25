$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms
[void][Reflection.Assembly]::LoadFile((Join-Path (Split-Path -Parent $PSScriptRoot) '快捷方式箭头.exe'))
$taskTestRoot=Join-Path ([IO.Path]::GetTempPath()) ('ArrowIndividualTest-'+[Guid]::NewGuid().ToString('N'))
$taskReport=Join-Path $PSScriptRoot 'individual-test-result.txt'
$taskLauncherRoot=Join-Path $env:LOCALAPPDATA 'ShortcutArrowPortable\Launchers'
$taskBefore=@(Get-ChildItem -LiteralPath $taskLauncherRoot -Directory -ErrorAction SilentlyContinue | ForEach-Object FullName)
New-Item -ItemType Directory -Path $taskTestRoot | Out-Null
$taskPassed=[Collections.Generic.List[string]]::new()
function Assert-Check([bool]$value,[string]$label) { if(-not $value){throw $label}; $taskPassed.Add($label) }
try {
    $taskSource=Join-Path $taskTestRoot 'probe.cs'
    [IO.File]::WriteAllText($taskSource,'using System; using System.IO; class Probe { static void Main(string[] args) { File.WriteAllText(args[0], args[1]+"\n"+Environment.CurrentDirectory); } }')
    $taskProbe=Join-Path $taskTestRoot 'probe.exe'
    & 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe' /nologo /target:winexe "/out:$taskProbe" $taskSource
    if($LASTEXITCODE -ne 0){throw 'probe compile failed'}
    $taskShortcutPath=Join-Path $taskTestRoot '带 空格的测试.lnk'
    $taskLaunchReport=Join-Path $taskTestRoot 'launch result.txt'
    $taskShell=New-Object -ComObject WScript.Shell
    $taskLink=$taskShell.CreateShortcut($taskShortcutPath)
    $taskLink.TargetPath=$taskProbe
    $taskLink.Arguments='"'+$taskLaunchReport+'" "参数 含空格"'
    $taskLink.WorkingDirectory=$taskTestRoot
    $taskLink.IconLocation=(Join-Path $PSScriptRoot 'app-icon.ico')+',0'
    $taskLink.Save()
    $taskOriginalIcon=$taskLink.IconLocation
    $taskOriginalArgs=$taskLink.Arguments
    [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($taskLink)
    [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($taskShell)
    $taskItem=[IndividualIcon]::new($taskShortcutPath,(Join-Path $taskTestRoot 'storage'))
    $taskBitmap=$taskItem.ReadIcon($false)
    Assert-Check ($taskBitmap.Width -eq 256) 'Largest icon frame loaded'
    $taskRounded=[IndividualIcon]::Rounded($taskBitmap,$false)
    Assert-Check ($taskRounded.GetPixel(0,0).A -eq 0) 'Corners transparent'
    Assert-Check ($taskRounded.GetPixel(128,128).ToArgb() -eq $taskBitmap.GetPixel(128,128).ToArgb()) 'Center artwork preserved'
    $taskRounded.Dispose();$taskBitmap.Dispose()
    $taskItem.ApplyRounded($false)
    Assert-Check $taskItem.CanRestore 'Icon backup created'
    Assert-Check ([IndividualIcon]::Property($taskShortcutPath,'TargetPath') -eq $taskProbe) 'Target preserved'
    Assert-Check ([IndividualIcon]::Property($taskShortcutPath,'Arguments') -eq $taskOriginalArgs) 'Arguments preserved'
    $taskItem.ApplyRounded($true)
    $taskItem.Restore()
    Assert-Check ([IndividualIcon]::Property($taskShortcutPath,'IconLocation') -eq $taskOriginalIcon) 'Repeated rounding restores original'
    $taskItem.ApplyRounded($false)
    $taskManaged=[IndividualIcon]::Property($taskShortcutPath,'IconLocation')
    [IndividualIcon]::SetIcon($taskShortcutPath,'external.ico,1')
    $taskRefused=$false
    try{$taskItem.Restore()}catch{$taskRefused=$true}
    Assert-Check $taskRefused 'External icon changes protected'
    [IndividualIcon]::SetIcon($taskShortcutPath,$taskManaged)
    $taskItem.Restore()
    $taskLauncher=Join-Path $taskTestRoot '无箭头 启动器.exe'
    $taskBitmap=$taskItem.ReadIcon($false)
    $taskItem.CreateLauncher($taskLauncher,$taskBitmap)
    Assert-Check (Test-Path -LiteralPath $taskLauncher) 'Single EXE launcher generated'
    $taskRefused=$false
    try{$taskItem.CreateLauncher($taskLauncher,$taskBitmap)}catch{$taskRefused=$true}
    Assert-Check $taskRefused 'Existing EXE is not overwritten'
    $taskBitmap.Dispose()
    # The launcher must not depend on the original shortcut after creation.
    Remove-Item -LiteralPath $taskShortcutPath
    Start-Process -FilePath $taskLauncher -WindowStyle Hidden -Wait
    $taskDeadline=(Get-Date).AddSeconds(8)
    while(-not(Test-Path -LiteralPath $taskLaunchReport) -and (Get-Date) -lt $taskDeadline){Start-Sleep -Milliseconds 100}
    Assert-Check (Test-Path -LiteralPath $taskLaunchReport) 'Launcher starts target without original shortcut'
    $taskResult=[IO.File]::ReadAllText($taskLaunchReport)
    Assert-Check ($taskResult -eq ('参数 含空格'+"`n"+$taskTestRoot)) 'Unicode arguments and working directory preserved'
    [IO.File]::WriteAllLines($taskReport,@('PASS: '+$taskPassed.Count+' checks')+$taskPassed)
    Get-Content -LiteralPath $taskReport
} catch {
    [IO.File]::WriteAllText($taskReport,($_ | Out-String))
    throw
} finally {
    $taskResolved=[IO.Path]::GetFullPath($taskTestRoot)
    if($taskResolved.StartsWith([IO.Path]::GetFullPath([IO.Path]::GetTempPath()),[StringComparison]::OrdinalIgnoreCase) -and [IO.Path]::GetFileName($taskResolved).StartsWith('ArrowIndividualTest-')){
        Remove-Item -LiteralPath $taskResolved -Recurse -Force
    }
    # Remove only test-owned launcher payloads whose captured target was this probe.
    foreach($taskNew in @(Get-ChildItem -LiteralPath $taskLauncherRoot -Directory -ErrorAction SilentlyContinue | Where-Object { $_.FullName -notin $taskBefore })){
        $taskStoredLink=Join-Path $taskNew.FullName 'launch.lnk'
        if((Test-Path -LiteralPath $taskStoredLink) -and [IndividualIcon]::Property($taskStoredLink,'TargetPath') -eq $taskProbe){
            $taskResolvedLauncher=[IO.Path]::GetFullPath($taskNew.FullName)
            if([IO.Path]::GetDirectoryName($taskResolvedLauncher) -eq [IO.Path]::GetFullPath($taskLauncherRoot)) { Remove-Item -LiteralPath $taskStoredLink -Force; if(@(Get-ChildItem -LiteralPath $taskResolvedLauncher -Force).Count -eq 0){Remove-Item -LiteralPath $taskResolvedLauncher} }
        }
    }
}
