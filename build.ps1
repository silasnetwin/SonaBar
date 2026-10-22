param([ValidateSet('Debug','Release')][string]$Configuration='Release',[string]$UpdateFeed='',[ValidatePattern('^[a-z0-9-]+$')][string]$UpdateChannel='stable',[switch]$SkipClean)
$ErrorActionPreference='Stop'
$env:DOTNET_CLI_TELEMETRY_OPTOUT='1'
$root=$PSScriptRoot
Set-Location -LiteralPath $root
$dotnet=Join-Path $root '.tools/dotnet/dotnet.exe'
if(!(Test-Path -LiteralPath $dotnet)) {
    $installed=Get-Command dotnet -ErrorAction SilentlyContinue
    if($installed -and ((& $installed.Source --list-sdks) -match '^10\.')) { $dotnet=$installed.Source }
    else {
        New-Item -ItemType Directory -Force '.tools' | Out-Null
        Invoke-WebRequest 'https://dot.net/v1/dotnet-install.ps1' -OutFile '.tools/dotnet-install.ps1'
        & '.tools/dotnet-install.ps1' -Channel 10.0 -InstallDir (Join-Path $root '.tools/dotnet') -NoPath
    }
}
function Dotnet { & $dotnet @args; if($LASTEXITCODE -ne 0){throw "dotnet failed: $args"} }
$cmake=Get-Command cmake -ErrorAction SilentlyContinue
if(!$cmake) {
    $visualStudioCmake='C:\Program Files\Microsoft Visual Studio\2022\Community\Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe'
    if(Test-Path -LiteralPath $visualStudioCmake) { $cmake=Get-Item -LiteralPath $visualStudioCmake }
}
if(!$cmake) { throw 'CMake 3.21 or newer is required to build the pinned whisper.cpp native runtime. Install CMake, then rerun build.ps1.' }
$cmakePath=if($cmake -is [System.Management.Automation.CommandInfo]) { $cmake.Source } else { $cmake.FullName }
$nativeBuild=Join-Path $root 'artifacts/native-build'
$nativeOut=Join-Path $root 'artifacts/native'
$generator=@('-S','src/Voxora.Transcription.Native','-B',$nativeBuild,'-A','x64','-DSINEFLOW_ENABLE_VULKAN=OFF')
& $cmakePath @generator
if($LASTEXITCODE -ne 0){throw 'CMake configuration failed for SineFlowWhisperNative.'}
& $cmakePath --build $nativeBuild --config $Configuration --target SineFlowWhisperNative
if($LASTEXITCODE -ne 0){throw 'CMake build failed for SineFlowWhisperNative.'}
New-Item -ItemType Directory -Force $nativeOut | Out-Null
$nativeDlls=Get-ChildItem -LiteralPath $nativeBuild -Recurse -Filter '*.dll' | Where-Object { $_.Name -match '^(SineFlowWhisperNative|whisper|ggml)' }
if(!$nativeDlls){throw 'Native whisper.cpp build did not produce runtime DLLs.'}
Copy-Item -LiteralPath $nativeDlls.FullName -Destination $nativeOut -Force
$dist=Join-Path $root 'dist'
if(!$SkipClean) {
    Dotnet clean Voxora.slnx -c $Configuration -v quiet
    if(Test-Path -LiteralPath $dist) {
        if([IO.Path]::GetFullPath($dist) -ne [IO.Path]::Combine([IO.Path]::GetFullPath($root),'dist')) { throw 'Invalid dist path' }
        Remove-Item -LiteralPath $dist -Recurse -Force
    }
}
New-Item -ItemType Directory -Force $dist,'artifacts' | Out-Null
Dotnet restore Voxora.slnx
Dotnet build Voxora.slnx -c $Configuration --no-restore
Dotnet test Voxora.slnx -c $Configuration --no-build --logger 'trx;LogFileName=tests.trx' --results-directory artifacts
Dotnet publish src/Voxora.App -c $Configuration -r win-x64 --self-contained true -o (Join-Path $dist 'SineFlow') '-p:PublishTrimmed=false'
Copy-Item -LiteralPath $nativeDlls.FullName -Destination (Join-Path $dist 'SineFlow') -Force
if($UpdateFeed) {
    $feedUri=[Uri]$UpdateFeed
    if($feedUri.Scheme -ne 'https') { throw 'UpdateFeed must use HTTPS.' }
    @{ feed=$feedUri.AbsoluteUri; channel=$UpdateChannel } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $dist 'SineFlow/sineflow-update.json') -Encoding utf8
}
& (Join-Path $root 'scripts/package-dependencies.ps1') -Destination (Join-Path $dist 'SineFlow')
Copy-Item -LiteralPath 'THIRD_PARTY_NOTICES.md' -Destination (Join-Path $dist 'SineFlow')
Copy-Item -LiteralPath 'resources/licenses' -Destination (Join-Path $dist 'SineFlow') -Recurse -Force
$zip=Join-Path $dist 'SineFlow-Portable-x64.zip'
if(Test-Path -LiteralPath $zip){Remove-Item -LiteralPath $zip -Force}
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory((Join-Path $dist 'SineFlow'),$zip,[IO.Compression.CompressionLevel]::Optimal,$false)
Dotnet publish installer/Voxora.Setup -c $Configuration -r win-x64 --self-contained true '-p:PublishSingleFile=true' '-p:IncludeNativeLibrariesForSelfExtract=true' -o (Join-Path $root 'artifacts/installer')
Copy-Item -LiteralPath 'artifacts/installer/SineFlow-Setup-x64.exe' -Destination $dist
Get-FileHash -LiteralPath $zip,(Join-Path $dist 'SineFlow-Setup-x64.exe') -Algorithm SHA256 | ForEach-Object { "$($_.Hash.ToLowerInvariant())  $([IO.Path]::GetFileName($_.Path))" } | Set-Content -LiteralPath (Join-Path $dist 'SHA256SUMS.txt')
Write-Host "Release artifacts: $dist"
