#Requires -Version 7.0
<#
.SYNOPSIS
    Draws the package icon: a loudness meter crossed by its reference level.
.DESCRIPTION
    The bars are momentary levels and the horizontal rule is the target that
    EBU R128 normalises towards. The rule is what separates the mark from a
    generic equaliser. Everything is flat fill so that it stays legible when
    NuGet renders it at 32 pixels.
#>
[CmdletBinding()]
param(
    [string] $Path = (Join-Path $PSScriptRoot '..\..\icon.png'),
    [int] $Size = 256
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Drawing

function New-RoundedPath {
    param(
        [float] $X,
        [float] $Y,
        [float] $Width,
        [float] $Height,
        [float] $Radius
    )

    $limit = [Math]::Min($Width, $Height) / 2.0
    if ($Radius -gt $limit) { $Radius = $limit }

    $diameter = $Radius * 2.0
    $path = [System.Drawing.Drawing2D.GraphicsPath]::new()

    if ($Radius -le 0) {
        $path.AddRectangle([System.Drawing.RectangleF]::new($X, $Y, $Width, $Height))
        return $path
    }

    $path.AddArc($X, $Y, $diameter, $diameter, 180, 90)
    $path.AddArc($X + $Width - $diameter, $Y, $diameter, $diameter, 270, 90)
    $path.AddArc($X + $Width - $diameter, $Y + $Height - $diameter, $diameter, $diameter, 0, 90)
    $path.AddArc($X, $Y + $Height - $diameter, $diameter, $diameter, 90, 90)
    $path.CloseFigure()
    return $path
}

$scale = $Size / 256.0

$ink = [System.Drawing.Color]::FromArgb(0x12, 0x1A, 0x26)
$level = [System.Drawing.Color]::FromArgb(0xEE, 0xF3, 0xFA)
$target = [System.Drawing.Color]::FromArgb(0xF2, 0xA9, 0x3B)

$barCount = 5
$barWidth = 26.0 * $scale
$barGap = 12.5 * $scale
$left = 38.0 * $scale
$baseline = 210.0 * $scale
$span = 164.0 * $scale
$radius = 8.0 * $scale

$heights = @(0.34, 0.86, 0.55, 1.00, 0.68)
$targetLevel = 0.72

$canvas = [System.Drawing.Bitmap]::new($Size, $Size,
    [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$graphics = [System.Drawing.Graphics]::FromImage($canvas)

try {
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $graphics.Clear($ink)

    $levelBrush = [System.Drawing.SolidBrush]::new($level)
    $targetBrush = [System.Drawing.SolidBrush]::new($target)

    try {
        for ($i = 0; $i -lt $barCount; ++$i) {
            $height = $span * $heights[$i]
            $x = $left + ($i * ($barWidth + $barGap))
            $y = $baseline - $height

            $bar = New-RoundedPath -X $x -Y $y -Width $barWidth -Height $height -Radius $radius
            try {
                $graphics.FillPath($levelBrush, $bar)
            }
            finally {
                $bar.Dispose()
            }
        }

        $ruleHeight = 9.0 * $scale
        $ruleLeft = 28.0 * $scale
        $ruleWidth = ($Size - (2.0 * $ruleLeft))
        $ruleTop = $baseline - ($span * $targetLevel) - ($ruleHeight / 2.0)

        $rule = New-RoundedPath -X $ruleLeft -Y $ruleTop -Width $ruleWidth `
            -Height $ruleHeight -Radius ($ruleHeight / 2.0)
        try {
            $graphics.FillPath($targetBrush, $rule)
        }
        finally {
            $rule.Dispose()
        }
    }
    finally {
        $levelBrush.Dispose()
        $targetBrush.Dispose()
    }
}
finally {
    $graphics.Dispose()
}

$opaque = $canvas.Clone(
    [System.Drawing.Rectangle]::new(0, 0, $Size, $Size),
    [System.Drawing.Imaging.PixelFormat]::Format24bppRgb)

try {
    $full = [System.IO.Path]::GetFullPath($Path)
    $opaque.Save($full, [System.Drawing.Imaging.ImageFormat]::Png)
    Write-Output "wrote $full at ${Size}x${Size}"
}
finally {
    $opaque.Dispose()
    $canvas.Dispose()
}
