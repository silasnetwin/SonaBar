param(
    [Parameter(Mandatory = $true)][string]$Destination,
    [switch]$Force
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$cache = Join-Path $root '.tools/qwen-runtime'
$archive = Join-Path $cache 'cpython-3.12.15-20261021-x86_64-pc-windows-msvc-install_only.tar.gz'
$runtime = Join-Path $cache 'python'
$requirements = Join-Path $PSScriptRoot 'qwen-requirements.txt'
$stamp = Join-Path $cache 'runtime.json'
$runtimeUrl = 'https://github.com/astral-sh/python-build-standalone/releases/download/20261021/cpython-3.12.15%2B20261021-x86_64-pc-windows-msvc-install_only.tar.gz'
$runtimeSha = 'be7f14464d1d66f84429ad27e01f6d1482963afff64a2fb5256b2a51c7b5d398'
$requirementsSha = (Get-FileHash -LiteralPath $requirements -Algorithm SHA256).Hash.ToLowerInvariant()

New-Item -ItemType Directory -Force $cache | Out-Null
$install = $Force -or !(Test-Path -LiteralPath $runtime) -or !(Test-Path -LiteralPath $stamp)
if (!$install -and (Get-Content -LiteralPath $stamp -Raw | ConvertFrom-Json).requirementsSha256 -ne $requirementsSha) { $install = $true }
if ($install) {
    if (!(Test-Path -LiteralPath $archive)) {
        Write-Host 'Downloading the embedded Qwen Python runtime...'
        Invoke-WebRequest -Uri $runtimeUrl -OutFile $archive
    }
    $actual = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actual -ne $runtimeSha) { throw "The embedded Python runtime hash does not match the pinned release (expected $runtimeSha, received $actual)." }
    if (Test-Path -LiteralPath $runtime) { Remove-Item -LiteralPath $runtime -Recurse -Force }
    tar.exe -xzf $archive -C $cache
    if ($LASTEXITCODE -ne 0 -or !(Test-Path -LiteralPath (Join-Path $runtime 'python.exe'))) { throw 'The embedded Python runtime could not be extracted.' }
    Write-Host 'Installing pinned Qwen dependencies into the embedded runtime...'
    & (Join-Path $runtime 'python.exe') -m pip install --disable-pip-version-check --no-warn-script-location --upgrade --target (Join-Path $runtime 'Lib/site-packages') -r $requirements
    if ($LASTEXITCODE -ne 0) { throw 'Pinned Qwen dependencies could not be installed.' }
    foreach ($relative in @('Lib/test', 'Lib/idlelib', 'Lib/tkinter', 'Lib/turtledemo', 'Lib/ensurepip', 'Lib/lib2to3', 'Lib/msilib', 'Lib/curses', 'Lib/dbm', 'Lib/venv', 'Lib/site-packages/pip', 'Lib/site-packages/torch/include', 'Lib/site-packages/torch/share')) {
        $path = Join-Path $runtime $relative
        if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Recurse -Force }
    }
    Get-ChildItem -LiteralPath $runtime -Filter '*.pdb' -Recurse -File | Remove-Item -Force
    Get-ChildItem -LiteralPath (Join-Path $runtime 'Lib/site-packages/torch/lib') -Filter '*.lib' -File -ErrorAction SilentlyContinue | Remove-Item -Force
    Get-ChildItem -LiteralPath $runtime -Filter '*.pyc' -Recurse -File | Remove-Item -Force
}

if (Test-Path -LiteralPath $Destination) { Remove-Item -LiteralPath $Destination -Recurse -Force }
New-Item -ItemType Directory -Force $Destination | Out-Null
& robocopy $runtime $Destination /E /R:2 /W:1 /NFL /NDL /NJH /NJS /NP | Out-Null
if ($LASTEXITCODE -ge 8) { throw "The embedded Qwen runtime could not be copied (robocopy exit code $LASTEXITCODE)." }
Copy-Item -LiteralPath (Join-Path $root 'src/Voxora.Transcription/Qwen/qwen_sidecar.py') -Destination (Join-Path $Destination 'qwen_sidecar.py') -Force
$licenseSource = Join-Path $root 'resources/licenses/qwen'
if (Test-Path -LiteralPath $licenseSource) {
    $licenseTarget = Join-Path (Join-Path $Destination 'licenses') 'qwen'
    New-Item -ItemType Directory -Force (Split-Path $licenseTarget -Parent) | Out-Null
    Copy-Item -LiteralPath $licenseSource -Destination $licenseTarget -Recurse -Force
}
$versions = & (Join-Path $Destination 'python.exe') -c "import json,numpy,soundfile,tokenizers,torch,transformers; print(json.dumps({'python':'3.12.15','torch':torch.__version__,'transformers':transformers.__version__,'numpy':numpy.__version__,'soundfile':soundfile.__version__,'tokenizers':tokenizers.__version__}))"
if ($LASTEXITCODE -ne 0) {
    if (Test-Path -LiteralPath $stamp) { Remove-Item -LiteralPath $stamp -Force }
    throw 'The packaged Qwen runtime could not be imported.'
}
@{ python = '3.12.15'; archiveSha256 = $runtimeSha; requirementsSha256 = $requirementsSha } | ConvertTo-Json | Set-Content -LiteralPath $stamp -Encoding utf8
$versions | Set-Content -LiteralPath (Join-Path $Destination 'runtime-info.json') -Encoding utf8
Write-Host "Qwen runtime packaged at $Destination"
