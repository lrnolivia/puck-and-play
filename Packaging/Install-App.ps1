param([string]$PackageRoot=$PSScriptRoot,[switch]$Startup,[switch]$EnableRecovery,[switch]$NoLaunch)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'Paths.ps1')
$installRoot=Join-Path $env:LOCALAPPDATA 'Programs\Puck & Play'
$sourceRoot=[IO.Path]::GetFullPath($PackageRoot)
Assert-PlainTree $sourceRoot
Assert-PlainTree $installRoot
if($sourceRoot.TrimEnd('\') -eq [IO.Path]::GetFullPath($installRoot).TrimEnd('\')){throw 'Run setup from the downloaded package, outside the installed application folder.'}
if (-not (Test-Path -LiteralPath (Join-Path $sourceRoot 'PuckAndPlay.exe'))) { throw 'The application package is incomplete.' }
if ((Test-Path -LiteralPath $installRoot) -and ((Get-Item -LiteralPath $installRoot).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'The installation folder is redirected. Choose a normal local folder.' }
New-Item -ItemType Directory -Path $installRoot -Force | Out-Null
$prefix=[IO.Path]::GetFullPath($installRoot)+'\'
Get-Process -Name PuckAndPlay,PuckAndPlay.UI -ErrorAction SilentlyContinue | Where-Object { $_.Path -and $_.Path.StartsWith($prefix,[StringComparison]::OrdinalIgnoreCase) } | ForEach-Object { $_.Kill();$_.WaitForExit() }
Get-ChildItem -LiteralPath $sourceRoot -Force | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $installRoot -Recurse -Force }
$exe=Join-Path $installRoot 'PuckAndPlay.exe'
$startupKey='HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
if ($Startup) { New-ItemProperty -LiteralPath $startupKey -Name 'Puck & Play' -Value ('"'+$exe+'" --tray') -PropertyType String -Force | Out-Null }
else { Remove-ItemProperty -LiteralPath $startupKey -Name 'Puck & Play' -ErrorAction SilentlyContinue }
$legacyRoot=Join-Path $env:LOCALAPPDATA 'Programs\Steam Controller Guard'
$legacyRun=(Get-ItemProperty -LiteralPath $startupKey -Name 'Steam Controller Guard' -ErrorAction SilentlyContinue).'Steam Controller Guard'
$backendExe=Join-Path $env:ProgramFiles 'Steam Controller Guard\SteamControllerGuard.exe'
if($legacyRun -in @(('"'+(Join-Path $legacyRoot 'SteamControllerGuard.exe')+'" --tray'),('"'+$backendExe+'" --tray'))) {
    Remove-ItemProperty -LiteralPath $startupKey -Name 'Steam Controller Guard'
}
$uninstallKey='HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\PuckAndPlay'
New-Item -Path $uninstallKey -Force | Out-Null
$powershell=Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
$registration=@{DisplayName='Puck & Play';DisplayVersion=(Get-Item -LiteralPath $exe).VersionInfo.FileVersion;Publisher='Lauren Olivia';InstallLocation=$installRoot;DisplayIcon=(Join-Path $installRoot 'puck.ico');URLInfoAbout='https://github.com/lrnolivia/puck-and-play';UninstallString=('"'+$powershell+'" -NoProfile -ExecutionPolicy Bypass -File "'+(Join-Path $installRoot 'Uninstall-App.ps1')+'"')}
foreach($entry in $registration.GetEnumerator()){New-ItemProperty -LiteralPath $uninstallKey -Name $entry.Key -Value $entry.Value -PropertyType String -Force | Out-Null}
$shell=New-Object -ComObject WScript.Shell
$menu=Join-Path ([Environment]::GetFolderPath('Programs')) 'Puck & Play'
New-Item -ItemType Directory -Path $menu -Force | Out-Null
$shortcut=$shell.CreateShortcut((Join-Path $menu 'Puck & Play.lnk'))
$shortcut.TargetPath=$exe;$shortcut.Arguments='--show-status';$shortcut.WorkingDirectory=$installRoot
$shortcut.IconLocation=(Join-Path $installRoot 'puck.ico')+',0';$shortcut.Description='Keep your Steam Controller puck ready to play';$shortcut.Save()
[void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($shortcut)
$shortcut=$shell.CreateShortcut((Join-Path $menu 'Uninstall Puck & Play.lnk'))
$shortcut.TargetPath=Join-Path $installRoot 'Uninstall.cmd';$shortcut.WorkingDirectory=$installRoot;$shortcut.Save()
[void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($shortcut)
[void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($shell)
& (Join-Path $installRoot 'Guard.Diagnostics.exe') --enroll
$enrolled=$LASTEXITCODE -eq 0
$recovery='Not enabled'
if ($EnableRecovery -and $enrolled) {
    try { & (Join-Path $installRoot 'Install-Recovery.ps1');$recovery='Enabled' }
    catch { $recovery='Not enabled: '+$_.Exception.Message }
}
$receipt=[ordered]@{Version=(Get-Item -LiteralPath $exe).VersionInfo.FileVersion;At=(Get-Date).ToString('o');Root=$installRoot;Startup=[bool]$Startup;Enrolled=$enrolled;Recovery=$recovery}
$receipt | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $installRoot 'installed.json') -Encoding UTF8
if(-not $NoLaunch) { Start-Process -FilePath $exe -ArgumentList '--show-status' -WorkingDirectory $installRoot -WindowStyle Hidden }
Write-Output ($receipt | ConvertTo-Json -Compress)
