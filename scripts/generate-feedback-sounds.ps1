param([string]$Output = (Join-Path $PSScriptRoot '../resources/sounds'))

# Original SonaBar feedback tones. They are generated from sine waves here so
# the repository contains no third-party samples and every release can be
# reproduced from source.
$ErrorActionPreference = 'Stop'
$Output = [IO.Path]::GetFullPath($Output)
New-Item -ItemType Directory -Force -Path $Output | Out-Null
$sampleRate = 44100

function Write-Tone {
    param(
        [string]$Path,
        [array]$Segments
    )

    $samples = [Collections.Generic.List[double]]::new()
    foreach ($segment in $Segments) {
        $frequency = [double]$segment[0]
        $seconds = [double]$segment[1]
        $amplitude = [double]$segment[2]
        $count = [int][Math]::Round($sampleRate * $seconds)
        $fade = [Math]::Min($count, [int]($sampleRate * 0.012))
        for ($i = 0; $i -lt $count; $i++) {
            $envelope = if ($i -lt $fade) { $i / $fade } elseif ($i -gt $count - $fade) { ($count - $i) / $fade } else { 1.0 }
            $samples.Add([Math]::Sin(2 * [Math]::PI * $frequency * $i / $sampleRate) * $amplitude * $envelope)
        }
    }

    $dataBytes = $samples.Count * 2
    $stream = [IO.File]::Open($Path, [IO.FileMode]::Create, [IO.FileAccess]::Write)
    try {
        $writer = [IO.BinaryWriter]::new($stream)
        $writer.Write([Text.Encoding]::ASCII.GetBytes('RIFF'))
        $writer.Write([int](36 + $dataBytes))
        $writer.Write([Text.Encoding]::ASCII.GetBytes('WAVE'))
        $writer.Write([Text.Encoding]::ASCII.GetBytes('fmt '))
        $writer.Write([int]16)
        $writer.Write([int16]1)
        $writer.Write([int16]1)
        $writer.Write([int]$sampleRate)
        $writer.Write([int]($sampleRate * 2))
        $writer.Write([int16]2)
        $writer.Write([int16]16)
        $writer.Write([Text.Encoding]::ASCII.GetBytes('data'))
        $writer.Write([int]$dataBytes)
        foreach ($sample in $samples) {
            $writer.Write([int16][Math]::Round([Math]::Max(-1, [Math]::Min(1, $sample)) * 32767))
        }
        $writer.Flush()
    }
    finally {
        $stream.Dispose()
    }
}

Write-Tone (Join-Path $Output 'start.wav') @(
    @(660, 0.085, 0.16),
    @(880, 0.105, 0.13)
)
Write-Tone (Join-Path $Output 'done.wav') @(
    @(880, 0.075, 0.13),
    @(1174.66, 0.135, 0.12)
)
Write-Tone (Join-Path $Output 'error.wav') @(
    @(220, 0.14, 0.15),
    @(196, 0.16, 0.13)
)

Get-ChildItem -LiteralPath $Output -Filter '*.wav' | Select-Object Name, Length
