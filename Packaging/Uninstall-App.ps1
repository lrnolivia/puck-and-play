param([switch]$RecoveryOnly,[string]$OwnerSid='')
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'Paths.ps1')
$installRoot=Join-Path $env:LOCALAPPDATA 'Programs\Puck & Play'
$identity=[Security.Principal.WindowsIdentity]::GetCurrent()
try {
    if(-not $OwnerSid){$OwnerSid=$identity.User.Value}
    $admin=([Security.Principal.WindowsPrincipal]$identity).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
} finally { $identity.Dispose() }
$backend=Join-Path $env:ProgramFiles 'Steam Controller Guard'
$receiptPath=Join-Path $backend 'installed.json'
Assert-PlainTree $installRoot
Assert-PlainPath $backend
if($RecoveryOnly) {
    if(-not $admin){throw 'Administrator approval is required to remove USB recovery.'}
    if(-not (Test-Path -LiteralPath $receiptPath)){return}
    $receipt=Get-Content -LiteralPath $receiptPath -Raw | ConvertFrom-Json
    if($receipt.OwnerSid -ne $OwnerSid){throw 'Recovery belongs to a different Windows account.'}
    $backendExe=Join-Path $backend 'SteamControllerGuard.exe'
    Assert-PlainTree $backend
    foreach($name in @('Steam Controller Guard - Device Recovery','Steam Controller Guard - Reconnect Puck')) {
        $task=Get-ScheduledTask -TaskName $name -ErrorAction SilentlyContinue
        if($task -and @($task.Actions | Where-Object { $_.Execute -ne $backendExe }).Count -eq 0) {
            Stop-ScheduledTask -TaskName $name -ErrorAction SilentlyContinue
            Unregister-ScheduledTask -TaskName $name -Confirm:$false
        }
    }
    Get-Process -Name SteamControllerGuard -ErrorAction SilentlyContinue | Where-Object {$_.Path -eq $backendExe} | Stop-Process
    foreach($name in @('SteamControllerGuard.exe','Guard.Diagnostics.exe','installed.json')) { $path=Join-Path $backend $name;if(Test-Path -LiteralPath $path){Remove-Item -LiteralPath $path -Force} }
    return
}
if(Test-Path -LiteralPath $receiptPath) {
    $powershell=Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
    try {
        $arguments='-NoProfile -ExecutionPolicy Bypass -File "{0}" -RecoveryOnly -OwnerSid "{1}"' -f $PSCommandPath,$OwnerSid
        $process=Start-Process -FilePath $powershell -ArgumentList $arguments -Verb RunAs -WindowStyle Hidden -PassThru
        $process.WaitForExit()
        if($process.ExitCode -ne 0){throw 'Recovery removal was not approved.'}
    } catch { Write-Warning 'The user app will be removed. Elevated USB recovery remains installed until an administrator removes it.' }
}
$prefix=[IO.Path]::GetFullPath($installRoot)+'\'
Get-Process -Name PuckAndPlay,PuckAndPlay.UI -ErrorAction SilentlyContinue | Where-Object { $_.Path -and $_.Path.StartsWith($prefix,[StringComparison]::OrdinalIgnoreCase) } | Stop-Process
$runKey='HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$run=(Get-ItemProperty -LiteralPath $runKey -Name 'Puck & Play' -ErrorAction SilentlyContinue).'Puck & Play'
if($run -and $run.Contains($installRoot)){Remove-ItemProperty -LiteralPath $runKey -Name 'Puck & Play'}
$uninstallKey='HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\PuckAndPlay'
if((Get-ItemProperty -LiteralPath $uninstallKey -ErrorAction SilentlyContinue).InstallLocation -eq $installRoot){Remove-Item -LiteralPath $uninstallKey}
$menu=Join-Path ([Environment]::GetFolderPath('Programs')) 'Puck & Play'
foreach($name in @('Puck & Play.lnk','Uninstall Puck & Play.lnk')) { $path=Join-Path $menu $name;if(Test-Path -LiteralPath $path){Remove-Item -LiteralPath $path} }
$manifest=Join-Path $installRoot 'package-files.txt'
if(Test-Path -LiteralPath $manifest) {
    foreach($relative in Get-Content -LiteralPath $manifest) {
        $target=[IO.Path]::GetFullPath((Join-Path $installRoot $relative))
        if(-not $target.StartsWith($prefix,[StringComparison]::OrdinalIgnoreCase)){throw 'An invalid package path was found; removal stopped.'}
        if(Test-Path -LiteralPath $target -PathType Leaf){Remove-Item -LiteralPath $target -Force -ErrorAction SilentlyContinue}
    }
}
Write-Output 'Puck & Play startup and application files were removed. Settings and logs were kept.'
