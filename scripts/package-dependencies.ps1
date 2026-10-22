param([Parameter(Mandatory=$true)][string]$Destination)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$cache=Join-Path $root '.tools'
New-Item -ItemType Directory -Force $cache | Out-Null
$archive=Join-Path $cache 'ffmpeg.zip'
$manifest=Get-Content (Join-Path $PSScriptRoot 'dependencies.json') -Raw | ConvertFrom-Json
if(!(Test-Path -LiteralPath $archive)) { Invoke-WebRequest $manifest.ffmpeg.url -OutFile $archive }
if((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne $manifest.ffmpeg.sha256) { throw 'FFmpeg checksum mismatch. Refresh the pinned dependency manifest before changing this binary.' }
$extract=Join-Path $cache 'ffmpeg'
if(!(Test-Path -LiteralPath $extract)) { Expand-Archive -LiteralPath $archive -DestinationPath $extract }
$decoder=Get-ChildItem -LiteralPath $extract -Filter ffmpeg.exe -Recurse | Select-Object -First 1
$native=Join-Path $Destination 'native'
New-Item -ItemType Directory -Force $native | Out-Null
Copy-Item -LiteralPath $decoder.FullName -Destination $native
Get-ChildItem -LiteralPath $decoder.DirectoryName -Filter '*.dll' | Copy-Item -Destination $native
Copy-Item -LiteralPath (Join-Path $decoder.Directory.Parent.FullName 'LICENSE.txt') -Destination (Join-Path $native 'FFmpeg-LICENSE.txt')
# App-local release CRT files. These are redistributable files installed by Microsoft build tooling.
$crt=Get-ChildItem 'C:/Program Files/Microsoft Visual Studio/*/*/VC/Redist/MSVC/*/x64/Microsoft.VC143.CRT' -Directory -ErrorAction SilentlyContinue | Sort-Object FullName -Descending | Select-Object -First 1
foreach($name in @('vcruntime140.dll','vcruntime140_1.dll','msvcp140.dll','msvcp140_1.dll','msvcp140_2.dll','concrt140.dll')) {
    $source=if($crt){Join-Path $crt.FullName $name}else{Join-Path $env:WINDIR "System32/$name"}
    if(!(Test-Path -LiteralPath $source)) { throw "Build machine is missing Microsoft C++ redistributable file $name." }
    Copy-Item -LiteralPath $source -Destination $Destination
}
# Retain only Windows x64 runtime payloads in the x64 distribution.
$runtimes=Join-Path $Destination 'runtimes'
if(Test-Path -LiteralPath $runtimes) {
    $resolvedRoot=[IO.Path]::GetFullPath($runtimes)+[IO.Path]::DirectorySeparatorChar
    Get-ChildItem -LiteralPath $runtimes -Directory -Recurse | Where-Object { $_.Name -match '^(linux|osx|win-arm|win-x86|android|ios|maccatalyst|browser)' } | Sort-Object { $_.FullName.Length } -Descending | ForEach-Object {
        if(!$_.FullName.StartsWith($resolvedRoot,[StringComparison]::OrdinalIgnoreCase)) { throw 'Runtime cleanup escaped destination' }
        Remove-Item -LiteralPath $_.FullName -Recurse -Force
    }
}
