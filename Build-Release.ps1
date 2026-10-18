param([switch]$SkipBuild)
$ErrorActionPreference='Stop'
if(-not $SkipBuild){& (Join-Path $PSScriptRoot 'Build.ps1')}
$version=(Get-Item -LiteralPath (Join-Path $PSScriptRoot 'bin\PuckAndPlay.exe')).VersionInfo.FileVersion
$shortVersion=([version]$version).ToString(3)
$output=Join-Path $PSScriptRoot 'dist'
$package=Join-Path $output ('PuckAndPlay-'+$shortVersion+'-windows-x64')
if(Test-Path -LiteralPath $package){throw 'The package output already exists. Use a fresh release checkout or move the previous package aside.'}
$interface=Join-Path $package ('Interface\'+$version)
New-Item -ItemType Directory -Path $interface -Force | Out-Null
$dotnet='dotnet'
& $dotnet publish (Join-Path $PSScriptRoot 'WinUI\PuckAndPlay.UI.csproj') -c Release -r win-x64 -p:Platform=x64 --self-contained true -p:WindowsAppSDKSelfContained=true -p:DebugType=None -p:DebugSymbols=false -o $interface --nologo
if($LASTEXITCODE -ne 0){throw 'Self-contained interface publication failed.'}
foreach($name in @('PuckAndPlay.exe','Guard.Diagnostics.exe')){Copy-Item -LiteralPath (Join-Path $PSScriptRoot ('bin\'+$name)) -Destination $package}
foreach($name in @('README.md','LICENSE','NOTICE')){if(Test-Path -LiteralPath (Join-Path $PSScriptRoot $name)){Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination $package}}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'assets\puck-play.ico') -Destination (Join-Path $package 'puck.ico')
Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'Packaging') -File | Where-Object {$_.Extension -in @('.ps1','.cmd')} | ForEach-Object {Copy-Item -LiteralPath $_.FullName -Destination $package}
$noticeSource=Join-Path $PSScriptRoot 'bin\WinUI\Notices'
if(Test-Path -LiteralPath $noticeSource){Copy-Item -LiteralPath $noticeSource -Destination $interface -Recurse -Force}
$files=@(Get-ChildItem -LiteralPath $package -File -Recurse | ForEach-Object {$_.FullName.Substring($package.Length+1)})
@($files+'package-files.txt'+'SHA256SUMS.txt'+'installed.json') | Set-Content -LiteralPath (Join-Path $package 'package-files.txt') -Encoding UTF8
$checksums=Get-ChildItem -LiteralPath $package -File -Recurse | ForEach-Object { (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash+'  '+$_.FullName.Substring($package.Length+1) }
$checksums | Set-Content -LiteralPath (Join-Path $package 'SHA256SUMS.txt') -Encoding UTF8
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip=Join-Path $output ('PuckAndPlay-'+$shortVersion+'-portable-windows-x64.zip')
[IO.Compression.ZipFile]::CreateFromDirectory($package,$zip,[IO.Compression.CompressionLevel]::Optimal,$false)
$payloadHash=(Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash
$metadata=Join-Path $output 'PackageInfo.cs'
('namespace PuckPlay.Setup { internal static class PackageInfo { internal const string PayloadSha256="'+$payloadHash+'"; } }') | Set-Content -LiteralPath $metadata -Encoding UTF8
$setup=Join-Path $output ('PuckAndPlay-'+$shortVersion+'-Setup.exe')
$compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $compiler /nologo /optimize+ /platform:x64 /target:winexe /warnaserror+ ('/out:'+$setup) ('/win32manifest:'+(Join-Path $PSScriptRoot 'app.manifest')) ('/win32icon:'+(Join-Path $PSScriptRoot 'assets\status\puck-green.ico')) ('/resource:'+$zip+',PuckPlay.Payload') ('/resource:'+(Join-Path $PSScriptRoot 'assets\status\puck-green.ico')+',PuckPlay.Icon') ('/resource:'+(Join-Path $PSScriptRoot 'assets\status\puck-green.png')+',PuckPlay.SetupArtwork') /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.IO.Compression.dll /reference:System.IO.Compression.FileSystem.dll (Join-Path $PSScriptRoot 'Packaging\Setup.cs') $metadata
if($LASTEXITCODE -ne 0){throw 'Installer compilation failed.'}
$verify=Join-Path $output 'verify-installer'
$process=Start-Process -FilePath $setup -ArgumentList @('--verify-package',('"'+$verify+'"')) -WindowStyle Hidden -PassThru -Wait
if($process.ExitCode -ne 0){throw 'Installer payload verification failed.'}
@($zip,$setup) | ForEach-Object {(Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash+'  '+[IO.Path]::GetFileName($_)} | Set-Content -LiteralPath (Join-Path $output 'SHA256SUMS.txt') -Encoding UTF8
Get-Item -LiteralPath $zip,$setup | Select-Object Name,Length
