param([string]$OwnerSid='')
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'Paths.ps1')
$identity=[Security.Principal.WindowsIdentity]::GetCurrent()
try {
    if(-not $OwnerSid) { $OwnerSid=$identity.User.Value }
    if($OwnerSid -notmatch '^S-1-5-21-\d+-\d+-\d+-\d+$') { throw 'A local Windows user identity is required.' }
    $admin=([Security.Principal.WindowsPrincipal]$identity).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
    if(-not $admin) {
        $powershell=Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
        $arguments='-NoProfile -ExecutionPolicy Bypass -File "{0}" -OwnerSid "{1}"' -f $PSCommandPath,$OwnerSid
        $process=Start-Process -FilePath $powershell -ArgumentList $arguments -Verb RunAs -WindowStyle Hidden -PassThru
        $process.WaitForExit()
        if($process.ExitCode -ne 0) { throw 'USB recovery was not enabled. You can try again from Troubleshooting.' }
        return
    }
    if($identity.User.Value -ne $OwnerSid) { throw 'Approve with the signed-in administrator account. The portable app still works without elevated recovery.' }
} finally { $identity.Dispose() }
$installRoot=Join-Path $env:ProgramFiles 'Steam Controller Guard'
$dataRoot=Join-Path $env:ProgramData 'SteamControllerGuard'
Assert-PlainTree $installRoot
Assert-PlainTree $dataRoot
$oldReceipt=Join-Path $installRoot 'installed.json'
if(Test-Path -LiteralPath $oldReceipt) {
    $previous=Get-Content -LiteralPath $oldReceipt -Raw | ConvertFrom-Json
    if($previous.OwnerSid -and $previous.OwnerSid -ne $OwnerSid) { throw 'USB recovery is registered to another Windows account.' }
}
foreach($directory in @($installRoot,$dataRoot)) {
    if((Test-Path -LiteralPath $directory) -and ((Get-Item -LiteralPath $directory).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'A recovery folder is redirected; setup stopped.' }
    New-Item -ItemType Directory -Path $directory -Force | Out-Null
    $acl=New-Object Security.AccessControl.DirectorySecurity
    $acl.SetAccessRuleProtection($true,$false)
    $acl.SetOwner((New-Object Security.Principal.SecurityIdentifier 'S-1-5-32-544'))
    foreach($rule in @(@('S-1-5-18','FullControl'),@('S-1-5-32-544','FullControl'),@('S-1-5-32-545','ReadAndExecute'))) {
        $acl.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule((New-Object Security.Principal.SecurityIdentifier $rule[0]),$rule[1],'ContainerInherit,ObjectInherit','None','Allow')))
    }
    Set-Acl -LiteralPath $directory -AclObject $acl
}
$exe=Join-Path $installRoot 'SteamControllerGuard.exe'
Get-Process -Name SteamControllerGuard -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $exe } | ForEach-Object { $_.Kill();$_.WaitForExit() }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'PuckAndPlay.exe') -Destination $exe -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Guard.Diagnostics.exe') -Destination (Join-Path $installRoot 'Guard.Diagnostics.exe') -Force
foreach($file in Get-ChildItem -LiteralPath $installRoot,$dataRoot -File) {
    $acl=Get-Acl -LiteralPath $file.FullName
    $acl.SetAccessRuleProtection($false,$true)
    foreach($rule in @($acl.Access | Where-Object {-not $_.IsInherited})){[void]$acl.RemoveAccessRuleSpecific($rule)}
    Set-Acl -LiteralPath $file.FullName -AclObject $acl
}
& (Join-Path $installRoot 'Guard.Diagnostics.exe') --enroll-machine
if($LASTEXITCODE -ne 0) { throw 'Connect one working puck, then enable recovery again.' }
$statePath=Join-Path $dataRoot 'repair-state.json'
if(Test-Path -LiteralPath $statePath) {
    & (Join-Path $installRoot 'Guard.Diagnostics.exe') --validate-state
    $valid=$LASTEXITCODE -eq 0
    if(-not $valid) {
        $backup=Join-Path $dataRoot 'repair-state.corrupt.json'
        if(Test-Path -LiteralPath $backup){Remove-Item -LiteralPath $backup -Force}
        Move-Item -LiteralPath $statePath -Destination $backup
    }
}
$principal=New-ScheduledTaskPrincipal -UserId $OwnerSid -LogonType Interactive -RunLevel Highest
$settings=New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -MultipleInstances IgnoreNew -ExecutionTimeLimit (New-TimeSpan -Minutes 2) -Hidden
$tasks=@('Steam Controller Guard - Device Recovery','Steam Controller Guard - Reconnect Puck')
$legacy=Get-ScheduledTask -TaskName 'Steam Controller Guard' -ErrorAction SilentlyContinue
if($legacy -and @($legacy.Actions | Where-Object {$_.Execute -ne $exe -or $_.Arguments -ne '--tray'}).Count -eq 0) {
    Stop-ScheduledTask -TaskName $legacy.TaskName -ErrorAction SilentlyContinue
    Unregister-ScheduledTask -TaskName $legacy.TaskName -Confirm:$false
}
for($index=0;$index -lt $tasks.Count;$index++) {
    $argument=if($index -eq 0){'--repair'}else{'--reconnect'}
    $existing=Get-ScheduledTask -TaskName $tasks[$index] -ErrorAction SilentlyContinue
    if($existing -and @($existing.Actions | Where-Object { $_.Execute -ne $exe }).Count -gt 0) { throw 'An existing task uses a different executable; setup stopped.' }
    if($existing -and $existing.State -eq 'Running'){Stop-ScheduledTask -TaskName $tasks[$index]}
    $action=New-ScheduledTaskAction -Execute $exe -Argument $argument -WorkingDirectory $installRoot
    Register-ScheduledTask -TaskName $tasks[$index] -Action $action -Principal $principal -Settings $settings -Description 'Bounded recovery of the enrolled Steam Controller puck only.' -Force | Out-Null
}
$scheduler=New-Object -ComObject Schedule.Service
$folder=$null
try {
    $scheduler.Connect();$folder=$scheduler.GetFolder('\')
    foreach($name in $tasks) {
        $task=$folder.GetTask($name)
        try {$task.SetSecurityDescriptor("O:BAG:BAD:P(A;;FA;;;SY)(A;;FA;;;BA)(A;;FRFX;;;$OwnerSid)",0)}
        finally {[void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($task)}
    }
}
finally { if($folder){[void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($folder)};[void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($scheduler) }
[ordered]@{Version=(Get-Item -LiteralPath $exe).VersionInfo.FileVersion;OwnerSid=$OwnerSid;SHA256=(Get-FileHash -LiteralPath $exe).Hash;At=(Get-Date).ToString('o')} | ConvertTo-Json | Set-Content -LiteralPath $oldReceipt -Encoding UTF8
