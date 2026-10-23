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
$parakeetBuild=Join-Path $root 'artifacts/parakeet-build'
$parakeetOut=Join-Path $root 'artifacts/parakeet-native'
$vcpkg=$null
if($env:VCPKG_ROOT -and (Test-Path -LiteralPath (Join-Path $env:VCPKG_ROOT 'vcpkg.exe'))) {
    $vcpkg=Join-Path $env:VCPKG_ROOT 'vcpkg.exe'
}
if(!$vcpkg) {
    $vcpkg=Get-ChildItem 'C:\Program Files\Microsoft Visual Studio\2022\*\VC\vcpkg\vcpkg.exe' -ErrorAction SilentlyContinue |
        Select-Object -First 1 -ExpandProperty FullName
}
if(!$vcpkg) {
    $command=Get-Command vcpkg -ErrorAction SilentlyContinue
    if($command) { $vcpkg=$command.Source }
}
if(!$vcpkg -or !(Test-Path -LiteralPath $vcpkg)) { throw 'vcpkg is required to build the pinned NeMo-Speech.cpp ASR runtime. Install the Visual Studio C++ workload or set VCPKG_ROOT.' }
& $vcpkg install '--triplet=x64-windows' '--x-feature=asr' '--feature-flags=manifests' '--x-manifest-root=third_party/NeMo-Speech.cpp' '--x-install-root=artifacts/vcpkg'
if($LASTEXITCODE -ne 0){throw 'vcpkg could not install the pinned NeMo-Speech.cpp ASR dependencies.'}
$vcpkgRoot=Split-Path -Parent $vcpkg
$parakeetToolchain=Join-Path $vcpkgRoot 'scripts/buildsystems/vcpkg.cmake'
$parakeetGenerator=@('-S','src/Voxora.Parakeet.Native','-B',$parakeetBuild,'-A','x64',"-DCMAKE_TOOLCHAIN_FILE=$parakeetToolchain",'-DVCPKG_TARGET_TRIPLET=x64-windows',"-DVCPKG_INSTALLED_DIR=$(Join-Path $root 'artifacts/vcpkg')")
& $cmakePath @parakeetGenerator
if($LASTEXITCODE -ne 0){throw 'CMake configuration failed for SineFlowParakeetNative.'}
& $cmakePath --build $parakeetBuild --config $Configuration --target nemo_speech_asr_c
if($LASTEXITCODE -ne 0){throw 'CMake build failed for nemo_speech_asr_c.'}
New-Item -ItemType Directory -Force $parakeetOut | Out-Null
$parakeetDlls=@(
    Get-ChildItem -LiteralPath (Join-Path $parakeetBuild "bin/$Configuration") -Filter '*.dll' -ErrorAction SilentlyContinue
    Get-ChildItem -LiteralPath (Join-Path $root 'artifacts/vcpkg/x64-windows/bin') -Filter '*.dll' -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -match '^(abseil_dll|libprotobuf)\.dll$' }
) | Sort-Object FullName -Unique
if(!$parakeetDlls){throw 'NeMo-Speech.cpp build did not produce runtime DLLs.'}
Copy-Item -LiteralPath $parakeetDlls.FullName -Destination $parakeetOut -Force
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
Copy-Item -LiteralPath $parakeetDlls.FullName -Destination (Join-Path $dist 'SineFlow') -Force
if($UpdateFeed) {
    $feedUri=[Uri]$UpdateFeed
    if($feedUri.Scheme -ne 'https') { throw 'UpdateFeed must use HTTPS.' }
    @{ feed=$feedUri.AbsoluteUri; channel=$UpdateChannel } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $dist 'SineFlow/sineflow-update.json') -Encoding utf8
}
& (Join-Path $root 'scripts/package-dependencies.ps1') -Destination (Join-Path $dist 'SineFlow')
& (Join-Path $root 'scripts/package-qwen.ps1') -Destination (Join-Path (Join-Path $dist 'SineFlow') 'qwen')
Copy-Item -LiteralPath 'THIRD_PARTY_NOTICES.md' -Destination (Join-Path $dist 'SineFlow')
Copy-Item -LiteralPath 'resources/licenses' -Destination (Join-Path $dist 'SineFlow') -Recurse -Force
Copy-Item -LiteralPath 'third_party/NeMo-Speech.cpp/THIRD_PARTY_NOTICES.md' -Destination (Join-Path $dist 'SineFlow/NeMo-Speech-THIRD_PARTY_NOTICES.md') -Force
$requiredRuntime=@('nemo_speech_asr_c.dll','nemo_speech_asr.dll','ggml.dll','ggml-base.dll','ggml-cpu.dll','abseil_dll.dll','libprotobuf.dll','vcomp140.dll')
foreach($name in $requiredRuntime) {
    if(!(Test-Path -LiteralPath (Join-Path (Join-Path $dist 'SineFlow') $name))) { throw "Release package is missing required runtime file $name." }
}
$requiredQwen=@('python.exe','qwen_sidecar.py','Lib/site-packages/torch/__init__.py','Lib/site-packages/transformers/__init__.py','runtime-info.json')
foreach($name in $requiredQwen) {
    if(!(Test-Path -LiteralPath (Join-Path (Join-Path (Join-Path $dist 'SineFlow') 'qwen') $name))) { throw "Release package is missing required Qwen runtime file $name." }
}
$zip=Join-Path $dist 'SineFlow-Portable-x64.zip'
if(Test-Path -LiteralPath $zip){Remove-Item -LiteralPath $zip -Force}
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory((Join-Path $dist 'SineFlow'),$zip,[IO.Compression.CompressionLevel]::Optimal,$false)
Dotnet publish installer/Voxora.Setup -c $Configuration -r win-x64 --self-contained true '-p:PublishSingleFile=true' '-p:IncludeNativeLibrariesForSelfExtract=true' -o (Join-Path $root 'artifacts/installer')
Copy-Item -LiteralPath 'artifacts/installer/SineFlow-Setup-x64.exe' -Destination $dist
Get-FileHash -LiteralPath $zip,(Join-Path $dist 'SineFlow-Setup-x64.exe') -Algorithm SHA256 | ForEach-Object { "$($_.Hash.ToLowerInvariant())  $([IO.Path]::GetFileName($_.Path))" } | Set-Content -LiteralPath (Join-Path $dist 'SHA256SUMS.txt')
Write-Host "Release artifacts: $dist"
