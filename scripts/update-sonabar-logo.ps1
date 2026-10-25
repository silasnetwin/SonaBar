#requires -Version 7.0
<#
.SYNOPSIS
    Builds the SonaBar icon set from the vector master in resources\SonaBar.svg.

.DESCRIPTION
    The SVG is the single source of truth for the brand. This script renders it with a
    headless Chromium browser, produces the PNG sizes the app and the installer use and
    writes the multi-size .ico file. A committed raster copy is always present, so a machine
    without a browser can still build and ship the application.

.EXAMPLE
    pwsh ./scripts/update-sonabar-logo.ps1
#>
param(
    [string]$Source = (Join-Path $PSScriptRoot '..\resources\SonaBar.svg'),
    [int]$RenderSize = 1024
)

$ErrorActionPreference = 'Stop'
$resourceRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\resources'))
$sourcePath = [IO.Path]::GetFullPath($Source)
if (!(Test-Path -LiteralPath $sourcePath)) { throw "Logo source not found: $sourcePath" }

# The SVG has to be valid XML before it is turned into application icons.
try {
    [xml]$document = Get-Content -LiteralPath $sourcePath -Raw
} catch {
    throw "Logo source is not valid XML: $sourcePath"
}
if ($document.svg -eq $null) { throw "Logo source has no <svg> root element: $sourcePath" }
if ($document.svg.viewBox -ne '0 0 1024 1024') { throw "Logo source needs a 0 0 1024 1024 viewBox: $sourcePath" }

function Find-Renderer {
    $candidates = @(
        (Join-Path $env:ProgramFiles 'Google\Chrome\Application\chrome.exe'),
        (Join-Path ${env:ProgramFiles(x86)} 'Google\Chrome\Application\chrome.exe'),
        (Join-Path $env:LOCALAPPDATA 'Google\Chrome\Application\chrome.exe'),
        (Join-Path $env:ProgramFiles 'Microsoft\Edge\Application\msedge.exe'),
        (Join-Path ${env:ProgramFiles(x86)} 'Microsoft\Edge\Application\msedge.exe')
    )
    foreach ($candidate in $candidates) { if (Test-Path -LiteralPath $candidate) { return $candidate } }
    return $null
}

$renderer = Find-Renderer
if ($null -eq $renderer) {
    throw 'No Chromium based browser was found. Install Chrome or Edge to rebuild the icons from the SVG.'
}

$workRoot = Join-Path ([IO.Path]::GetTempPath()) ('SonaBar-logo-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $workRoot -Force | Out-Null
try {
    $page = Join-Path $workRoot 'page.html'
    $sourceUrl = 'file:///' + ($sourcePath -replace '\\', '/')
    @"
<!doctype html><html><head><meta charset="utf-8"><style>
html,body{margin:0;padding:0;background:transparent}
img{display:block;width:100vw;height:100vh}
</style></head><body><img src="$sourceUrl"></body></html>
"@ | Set-Content -LiteralPath $page -Encoding UTF8

    $master = Join-Path $workRoot 'master.png'
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    & $renderer --headless=new --disable-gpu --no-sandbox --hide-scrollbars `
        --default-background-color=00000000 --window-size="$RenderSize,$RenderSize" `
        --screenshot="$master" ("file:///" + ($page -replace '\\', '/')) 2>$null | Out-Null
    $ErrorActionPreference = $previous
    if (!(Test-Path -LiteralPath $master)) { throw "The browser did not produce a rendering of $sourcePath" }

    Add-Type -ReferencedAssemblies @([System.Drawing.Bitmap].Assembly.Location, [System.Drawing.RectangleF].Assembly.Location, (Join-Path $PSHOME 'System.Private.Windows.GdiPlus.dll'), (Join-Path $PSHOME 'System.Private.Windows.Core.dll')) -TypeDefinition @'
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

public static class SonaBarIconWriter
{
    public static void Write(string master, string destination, int[] sizes)
    {
        using var original = new Bitmap(master);
        using var square = new Bitmap(original.Width, original.Height, PixelFormat.Format32bppArgb);
        using (var canvas = Graphics.FromImage(square))
        {
            canvas.Clear(Color.Transparent);
            canvas.CompositingMode = CompositingMode.SourceCopy;
            canvas.InterpolationMode = InterpolationMode.HighQualityBicubic;
            canvas.PixelOffsetMode = PixelOffsetMode.HighQuality;
            canvas.DrawImage(original, 0, 0, original.Width, original.Height);
        }
        foreach (var size in sizes)
        {
            using var icon = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (var canvas = Graphics.FromImage(icon))
            {
                canvas.Clear(Color.Transparent);
                canvas.CompositingMode = CompositingMode.SourceCopy;
                canvas.InterpolationMode = InterpolationMode.HighQualityBicubic;
                canvas.PixelOffsetMode = PixelOffsetMode.HighQuality;
                canvas.DrawImage(square, 0, 0, size, size);
            }
            icon.Save(System.IO.Path.Combine(destination, $"SonaBar-{size}.png"), ImageFormat.Png);
        }
        square.Save(System.IO.Path.Combine(destination, "SonaBar-Source.png"), ImageFormat.Png);
    }

    public static void WriteMark(string master, string destination)
    {
        using var original = new Bitmap(master);
        using var canvas = new Bitmap(original.Width, original.Height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(canvas))
        {
            g.Clear(Color.Transparent);
            g.CompositingMode = CompositingMode.SourceCopy;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.DrawImage(original, 0, 0, original.Width, original.Height);
        }
        canvas.Save(destination, ImageFormat.Png);
    }

    public static void WriteIcon(string destination, int[] sizes)
    {
        var frames = new byte[sizes.Length][];
        for (var i = 0; i < sizes.Length; i++)
            frames[i] = System.IO.File.ReadAllBytes(System.IO.Path.Combine(destination, $"SonaBar-{sizes[i]}.png"));
        using var output = new BinaryWriter(System.IO.File.Create(System.IO.Path.Combine(destination, "SonaBar.ico")));
        output.Write((ushort)0);
        output.Write((ushort)1);
        output.Write((ushort)frames.Length);
        var offset = 6 + 16 * frames.Length;
        for (var i = 0; i < frames.Length; i++)
        {
            output.Write((byte)(sizes[i] == 256 ? 0 : sizes[i]));
            output.Write((byte)(sizes[i] == 256 ? 0 : sizes[i]));
            output.Write((byte)0);
            output.Write((byte)0);
            output.Write((ushort)1);
            output.Write((ushort)32);
            output.Write(frames[i].Length);
            output.Write(offset);
            offset += frames[i].Length;
        }
        foreach (var frame in frames) output.Write(frame);
    }
}
'@

    $sizes = @(16, 24, 32, 48, 64, 128, 256)
    [SonaBarIconWriter]::Write($master, $resourceRoot, $sizes)
    [SonaBarIconWriter]::WriteIcon($resourceRoot, $sizes)

    # The transparent mark is used inside the application, where a bright tile would break
    # the dark surface. It is rendered from the vector as well.
    $markSource = Join-Path $resourceRoot 'SonaBar-mark.svg'
    if (Test-Path -LiteralPath $markSource) {
        $markPage = Join-Path $workRoot 'mark.html'
        $markUrl = 'file:///' + ($markSource -replace '\\', '/')
        @"
<!doctype html><html><head><meta charset="utf-8"><style>
html,body{margin:0;padding:0;background:transparent}
img{display:block;width:100vw;height:100vh}
</style></head><body><img src="$markUrl"></body></html>
"@ | Set-Content -LiteralPath $markPage -Encoding UTF8
        $markMaster = Join-Path $workRoot 'mark.png'
        $previous = $ErrorActionPreference
        $ErrorActionPreference = 'Continue'
        & $renderer --headless=new --disable-gpu --no-sandbox --hide-scrollbars `
            --default-background-color=00000000 --window-size="512,512" `
            --screenshot="$markMaster" ("file:///" + ($markPage -replace '\\', '/')) 2>$null | Out-Null
        $ErrorActionPreference = $previous
        if (Test-Path -LiteralPath $markMaster) {
            [SonaBarIconWriter]::WriteMark($markMaster, (Join-Path $resourceRoot 'SonaBar-Mark.png'))
        }
    }
    Write-Host "SonaBar icons rebuilt from $sourcePath"
}
finally {
    Remove-Item -LiteralPath $workRoot -Recurse -Force -ErrorAction SilentlyContinue
}
