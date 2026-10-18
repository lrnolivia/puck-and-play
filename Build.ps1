$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$outputRoot = Join-Path $PSScriptRoot 'bin'
New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null
$sources = @('UsbDevices.cs','ReceiverConfiguration.cs','DeviceFault.cs','ControllerConnection.cs','Models.cs','ConnectionView.cs','UiBridge.cs','Guard.cs','Brand.cs','Windows.cs','Tests.cs') | ForEach-Object { Join-Path $PSScriptRoot $_ }
$iconPath=Join-Path $PSScriptRoot 'assets\puck-play.ico'
if (-not (Test-Path -LiteralPath $iconPath)) { throw 'The application icon is missing from assets.' }
$artworkPath=Join-Path $PSScriptRoot 'assets\puck-play-mark.png'
if (-not (Test-Path -LiteralPath $artworkPath)) { throw 'The application artwork is missing from assets.' }
$common = @('/nologo','/optimize+','/platform:x64','/warn:4','/warnaserror+',('/win32manifest:' + (Join-Path $PSScriptRoot 'app.manifest')),('/win32icon:'+$iconPath),('/resource:'+$iconPath+',PuckPlay.Icon'),('/resource:'+$artworkPath+',PuckPlay.Artwork'),'/reference:System.Windows.Forms.dll','/reference:System.Drawing.dll','/reference:System.Web.Extensions.dll')
foreach($state in @('green','yellow','red','grey')) {
    $statusIcon=Join-Path $PSScriptRoot ('assets\status\puck-'+$state+'.ico')
    if(-not (Test-Path -LiteralPath $statusIcon)){throw 'Export the status artwork before building.'}
    $common+=('/resource:'+$statusIcon+',PuckPlay.Icon.'+$state)
}
& $compiler @common '/target:winexe' ('/out:' + (Join-Path $outputRoot 'SteamControllerGuard.exe')) @sources
if ($LASTEXITCODE -ne 0) { throw 'Tray executable compilation failed.' }
& $compiler @common '/target:winexe' ('/out:' + (Join-Path $outputRoot 'PuckAndPlay.exe')) @sources
if ($LASTEXITCODE -ne 0) { throw 'Puck & Play executable compilation failed.' }
& $compiler @common '/target:exe' ('/out:' + (Join-Path $outputRoot 'Guard.Diagnostics.exe')) @sources
if ($LASTEXITCODE -ne 0) { throw 'Diagnostics executable compilation failed.' }
& (Join-Path $outputRoot 'Guard.Diagnostics.exe') --test
if ($LASTEXITCODE -ne 0) { throw 'Safety checks failed.' }
& (Get-Command dotnet -ErrorAction Stop).Source build (Join-Path $PSScriptRoot 'WinUI\PuckAndPlay.UI.csproj') -c Release -p:Platform=x64 -o (Join-Path $outputRoot 'WinUI') --nologo
if ($LASTEXITCODE -ne 0) { throw 'WinUI interface compilation failed.' }
$assets=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'WinUI\obj\project.assets.json') -Raw | ConvertFrom-Json
$noticeRoot=Join-Path $outputRoot 'WinUI\Notices'
foreach($library in $assets.libraries.PSObject.Properties) {
    if($library.Value.type -ne 'package'){continue}
    $packageRoot=Join-Path (Join-Path $env:USERPROFILE '.nuget\packages') $library.Value.path
    $notices=@(Get-ChildItem -LiteralPath $packageRoot -File -ErrorAction SilentlyContinue | Where-Object { $_.Name -match '^(LICENSE|NOTICE|ThirdPartyNotices)(\.|$)' })
    if($notices.Count -eq 0){continue}
    $destination=Join-Path $noticeRoot ($library.Name -replace '/','-')
    New-Item -ItemType Directory -Path $destination -Force | Out-Null
    foreach($notice in $notices){Copy-Item -LiteralPath $notice.FullName -Destination $destination -Force}
}
$nativeNotices=Join-Path $PSScriptRoot 'assets\native-notices'
if(Test-Path -LiteralPath $nativeNotices) {
    $nativeNoticeOutput=Join-Path $noticeRoot 'Crystal-renderer'
    New-Item -ItemType Directory -Path $nativeNoticeOutput -Force | Out-Null
    Get-ChildItem -LiteralPath $nativeNotices -Filter '*.txt' -File | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $nativeNoticeOutput -Force }
}
Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $outputRoot 'SteamControllerGuard.exe')
