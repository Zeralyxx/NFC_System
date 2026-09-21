[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$destination = Join-Path $PSScriptRoot '../../NFC_System/Assets/Branding'
New-Item -ItemType Directory -Path $destination -Force | Out-Null
$icon = [Drawing.Bitmap]::FromFile((Join-Path $PSScriptRoot 'nfc-system-icon-v1.png'))
$logo = [Drawing.Bitmap]::FromFile((Join-Path $PSScriptRoot 'nfc-system-logo-v1.png'))

function Export-Png([Drawing.Image]$Source, [int]$Width, [int]$Height, [string]$Name, [double]$Fit = 1.0) {
    $bitmap = [Drawing.Bitmap]::new($Width, $Height, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    $attributes = [Drawing.Imaging.ImageAttributes]::new()
    try {
        $graphics.Clear([Drawing.Color]::Transparent)
        $graphics.CompositingQuality = [Drawing.Drawing2D.CompositingQuality]::HighQuality
        $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $attributes.SetWrapMode([Drawing.Drawing2D.WrapMode]::TileFlipXY)
        $ratio = [Math]::Min($Width / $Source.Width, $Height / $Source.Height) * $Fit
        $w = [Math]::Max(1, [int][Math]::Round($Source.Width * $ratio))
        $h = [Math]::Max(1, [int][Math]::Round($Source.Height * $ratio))
        $rectangle = [Drawing.Rectangle]::new([int](($Width - $w) / 2), [int](($Height - $h) / 2), $w, $h)
        $graphics.DrawImage($Source, $rectangle, 0, 0, $Source.Width, $Source.Height, [Drawing.GraphicsUnit]::Pixel, $attributes)
        $bitmap.Save((Join-Path $destination $Name), [Drawing.Imaging.ImageFormat]::Png)
    }
    finally { $attributes.Dispose(); $graphics.Dispose(); $bitmap.Dispose() }
}

try {
    Export-Png $icon 512 512 'Icon.png'
    Export-Png $logo 1200 400 'Logo.png'
    foreach ($scale in @(100, 125, 150, 200, 400)) {
        foreach ($size in @(44, 150)) {
            $pixels = [int][Math]::Ceiling($size * $scale / 100)
            Export-Png $icon $pixels $pixels "Square${size}x${size}Logo.scale-$scale.png"
        }
        Export-Png $logo ([int][Math]::Ceiling(310 * $scale / 100)) ([int][Math]::Ceiling(150 * $scale / 100)) "Wide310x150Logo.scale-$scale.png" 0.90
        Export-Png $logo ([int][Math]::Ceiling(620 * $scale / 100)) ([int][Math]::Ceiling(300 * $scale / 100)) "SplashScreen.scale-$scale.png" 0.85
        $storePixels = [int][Math]::Ceiling(50 * $scale / 100)
        Export-Png $icon $storePixels $storePixels "StoreLogo.scale-$scale.png"
    }
    $sizes = @(16, 20, 24, 32, 40, 48, 64, 96, 128, 256)
    $frames = @()
    foreach ($size in $sizes) {
        $name = "Square44x44Logo.targetsize-${size}_altform-unplated.png"
        Export-Png $icon $size $size $name
        Copy-Item -LiteralPath (Join-Path $destination $name) -Destination (Join-Path $destination "Square44x44Logo.targetsize-$size.png") -Force
        $frames += ,([IO.File]::ReadAllBytes((Join-Path $destination $name)))
    }
    # ICO directory followed by PNG-encoded frames; no artwork is redrawn or recolored.
    $stream = [IO.File]::Create((Join-Path $destination 'AppIcon.ico'))
    $writer = [IO.BinaryWriter]::new($stream)
    try {
        $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$sizes.Count)
        $offset = [uint32](6 + 16 * $sizes.Count)
        for ($i = 0; $i -lt $sizes.Count; $i++) {
            $dimension = if ($sizes[$i] -eq 256) { 0 } else { $sizes[$i] }
            $writer.Write([byte]$dimension); $writer.Write([byte]$dimension)
            $writer.Write([byte]0); $writer.Write([byte]0)
            $writer.Write([uint16]1); $writer.Write([uint16]32)
            $writer.Write([uint32]$frames[$i].Length); $writer.Write($offset)
            $offset += $frames[$i].Length
        }
        foreach ($frame in $frames) { $writer.Write([byte[]]$frame) }
    }
    finally { $writer.Dispose(); $stream.Dispose() }
}
finally { $icon.Dispose(); $logo.Dispose() }
Write-Output "Windows branding assets generated in $destination"
