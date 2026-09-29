# Generates samples/sample-drawing-shaft.png - a simple dimensioned technical drawing of a stepped, turned shaft.
# Windows PowerShell 5.1 (uses System.Drawing / GDI+). Run:  powershell -File samples\generate-sample-drawing.ps1

Add-Type -AssemblyName System.Drawing

$W = 1800; $H = 1150
$bmp = New-Object System.Drawing.Bitmap $W, $H
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = 'AntiAlias'
$g.TextRenderingHint = 'AntiAliasGridFit'
$g.Clear([System.Drawing.Color]::White)

$ink    = [System.Drawing.Color]::FromArgb(20, 20, 20)
$thick  = New-Object System.Drawing.Pen $ink, 3
$thin   = New-Object System.Drawing.Pen $ink, 1.4
$center = New-Object System.Drawing.Pen $ink, 1.2; $center.DashPattern = @(18, 4, 3, 4)
$hidden = New-Object System.Drawing.Pen $ink, 1.4; $hidden.DashPattern = @(6, 4)
$hatch  = New-Object System.Drawing.Pen $ink, 1
$brush  = New-Object System.Drawing.SolidBrush $ink
$font   = New-Object System.Drawing.Font 'Arial', 17
$fontS  = New-Object System.Drawing.Font 'Arial', 13
$fontB  = New-Object System.Drawing.Font 'Arial', 20, ([System.Drawing.FontStyle]::Bold)
$fontT  = New-Object System.Drawing.Font 'Arial', 26, ([System.Drawing.FontStyle]::Bold)

function Arrow([double]$x, [double]$y, [double]$dx, [double]$dy) {
    # small filled arrow head at (x,y) pointing in direction (dx,dy)
    $len = 14; $w = 5
    $px = -$dy; $py = $dx
    $pts = @(
        (New-Object System.Drawing.PointF $x, $y),
        (New-Object System.Drawing.PointF ($x - $dx*$len + $px*$w), ($y - $dy*$len + $py*$w)),
        (New-Object System.Drawing.PointF ($x - $dx*$len - $px*$w), ($y - $dy*$len - $py*$w))
    )
    $g.FillPolygon($brush, [System.Drawing.PointF[]]$pts)
}
function HDim([double]$x1, [double]$x2, [double]$y, [string]$text, [double]$extFrom) {
    # horizontal dimension with extension lines from y=$extFrom
    $g.DrawLine($thin, $x1, $extFrom, $x1, $y + 8); $g.DrawLine($thin, $x2, $extFrom, $x2, $y + 8)
    $g.DrawLine($thin, $x1, $y, $x2, $y); Arrow $x1 $y -1 0; Arrow $x2 $y 1 0
    $sz = $g.MeasureString($text, $font)
    $g.DrawString($text, $font, $brush, (($x1 + $x2) / 2 - $sz.Width / 2), ($y - $sz.Height - 2))
}
function VDim([double]$x, [double]$y1, [double]$y2, [string]$text) {
    $g.DrawLine($thin, $x, $y1, $x, $y2); Arrow $x $y1 0 -1; Arrow $x $y2 0 1
    $state = $g.Save()
    $g.TranslateTransform($x - 6, ($y1 + $y2) / 2); $g.RotateTransform(-90)
    $sz = $g.MeasureString($text, $font)
    $g.DrawString($text, $font, $brush, (-$sz.Width / 2), (-$sz.Height))
    $g.Restore($state)
}
function Roughness([double]$x, [double]$y, [string]$text) {
    # simple surface roughness symbol (check mark) with value
    $g.DrawLine($thin, $x, $y, ($x + 10), ($y + 18)); $g.DrawLine($thin, ($x + 10), ($y + 18), ($x + 32), ($y - 18))
    $g.DrawLine($thin, ($x - 8), $y, $x, $y)
    $g.DrawString($text, $fontS, $brush, ($x + 20), ($y - 38))
}

# --- Frame ---------------------------------------------------------------------------------
$g.DrawRectangle($thick, 20, 20, $W - 40, $H - 40)

# --- Shaft, side view. Scale 4 px / mm. Sections: Ø25h6 x 40 | Ø40 x 100 | Ø30k6 x 40 ---------
$s = 4.0; $x0 = 230; $cy = 430
$xA = $x0; $xB = $x0 + 40*$s; $xC = $x0 + 140*$s; $xD = $x0 + 180*$s
$r25 = 12.5*$s; $r40 = 20*$s; $r30 = 15*$s; $ch = 1*$s

$outline = @(
    @(($xA), ($cy - $r25 + $ch)), @(($xA + $ch), ($cy - $r25)), @($xB, ($cy - $r25)),
    @($xB, ($cy - $r40)), @($xC, ($cy - $r40)), @($xC, ($cy - $r30)),
    @(($xD - $ch), ($cy - $r30)), @($xD, ($cy - $r30 + $ch)),
    @($xD, ($cy + $r30 - $ch)), @(($xD - $ch), ($cy + $r30)), @($xC, ($cy + $r30)),
    @($xC, ($cy + $r40)), @($xB, ($cy + $r40)), @($xB, ($cy + $r25)),
    @(($xA + $ch), ($cy + $r25)), @($xA, ($cy + $r25 - $ch))
)
$pts = $outline | ForEach-Object { New-Object System.Drawing.PointF $_[0], $_[1] }
$g.DrawPolygon($thick, [System.Drawing.PointF[]]$pts)
# chamfer edge lines
$g.DrawLine($thin, ($xA + $ch), ($cy - $r25), ($xA + $ch), ($cy + $r25))
$g.DrawLine($thin, ($xD - $ch), ($cy - $r30), ($xD - $ch), ($cy + $r30))
# centre line
$g.DrawLine($center, ($xA - 40), $cy, ($xD + 40), $cy)

# keyway on Ø30 section: 28 mm long, centred in the section, seen from above (dashed = depth t=4)
$kx1 = $xC + 6*$s; $kx2 = $kx1 + 28*$s
$g.DrawLine($thick, $kx1, ($cy - $r30), $kx1, ($cy - $r30 + 4*$s))
$g.DrawLine($thick, $kx2, ($cy - $r30), $kx2, ($cy - $r30 + 4*$s))
$g.DrawLine($thick, $kx1, ($cy - $r30 + 4*$s), $kx2, ($cy - $r30 + 4*$s))

# --- Dimensions ----------------------------------------------------------------------------
HDim $xA $xD ($cy + $r40 + 110) '180 ±0,1' ($cy + $r40 + 5)
HDim $xA $xB ($cy - $r40 - 50) '40' ($cy - $r25 - 5)
HDim $xB $xC ($cy - $r40 - 50) '100' ($cy - $r40 - 5)
HDim $xC $xD ($cy - $r40 - 50) '40' ($cy - $r30 - 5)
HDim $kx1 $kx2 ($cy - $r40 - 110) '28' ($cy - $r30 - 5)
HDim $xC $kx1 ($cy - $r40 - 110) '6' ($cy - $r40 - 5)

VDim ($xA + 70) ($cy - $r25) ($cy + $r25) 'Ø25h6'
VDim ($xB + 200) ($cy - $r40) ($cy + $r40) 'Ø40'
VDim ($xD - 30) ($cy - $r30) ($cy + $r30) 'Ø30k6'

Roughness ($xA + 95) ($cy - $r25 - 30) 'Ra 1,6'
Roughness ($xC + 125) ($cy + $r30 + 30) 'Ra 1,6'
$g.DrawString('2x 1x45°', $font, $brush, ($xA - 120), ($cy + $r25 + 20))
$g.DrawLine($thin, ($xA - 30), ($cy + $r25 + 32), ($xA + 2), ($cy + $r25 - 2))

# --- Section A-A through the keyway --------------------------------------------------------
$sx = 1390; $sy = 430; $sr = 15*4.5
$g.DrawString('A-A', $fontB, $brush, ($sx - 25), ($sy - $sr - 115))
# cutting plane mark (below the shaft, through the middle of the keyway)
$g.DrawLine($thick, ($kx1 + 56), ($cy + $r30 + 12), ($kx1 + 56), ($cy + $r30 + 42))
$g.DrawString('A', $fontB, $brush, ($kx1 + 62), ($cy + $r30 + 20))
$kh = 4 * 4.5; $kwh = 4 * 4.5
$rect = New-Object System.Drawing.RectangleF ($sx - $sr), ($sy - $sr), (2*$sr), (2*$sr)
# hatched disc with keyway cut-out
$region = New-Object System.Drawing.Drawing2D.GraphicsPath
$region.AddEllipse($rect)
$g.SetClip($region)
for ($i = -200; $i -lt 200; $i += 12) { $g.DrawLine($hatch, ($sx + $i - 100), ($sy + 100), ($sx + $i + 100), ($sy - 100)) }
$g.ResetClip()
$g.FillRectangle([System.Drawing.Brushes]::White, ($sx - $kwh), ($sy - $sr - 2), (2*$kwh), ($kh + 2))
$g.DrawEllipse($thick, $rect)
$g.DrawRectangle($thick, ($sx - $kwh), ($sy - $sr), (2*$kwh), $kh)
$g.DrawLine($center, ($sx - $sr - 25), $sy, ($sx + $sr + 25), $sy)
$g.DrawLine($center, $sx, ($sy - $sr - 25), $sx, ($sy + $sr + 25))
HDim ($sx - $kwh) ($sx + $kwh) ($sy - $sr - 45) '8P9' ($sy - $sr)
$g.DrawString('t = 4', $font, $brush, ($sx + $sr + 12), ($sy - $sr - 8))
$g.DrawString('Ø30k6', $font, $brush, ($sx - 32), ($sy + $sr + 30))

# --- General notes -------------------------------------------------------------------------
$notes = @(
    'Notes:',
    '1. Unspecified tolerances ISO 2768-m.',
    '2. Break sharp edges 0,3 max.',
    '3. Remaining surfaces Ra 3,2.'
)
$ny = 760
foreach ($n in $notes) { $g.DrawString($n, $font, $brush, 60, $ny); $ny += 30 }

# --- Title block ---------------------------------------------------------------------------
$tx = 1040; $ty = 830; $tw = 740; $th = 300
$g.DrawRectangle($thick, $tx, $ty, $tw, $th)
$rows = @(
    @('Name', 'SHAFT / WAŁEK', $fontT),
    @('Part no.', 'PV-1001-A', $fontB),
    @('Material', 'C45 (1.0503) - round bar', $font),
    @('Scale', '1:1   Sheet 1/1   Units: mm', $font),
    @('Drawn', 'PRODVISOR AI demo sample', $font)
)
$ry = $ty
$rowH = $th / $rows.Count
foreach ($r in $rows) {
    $g.DrawLine($thin, $tx, $ry, ($tx + $tw), $ry)
    $g.DrawString($r[0], $fontS, $brush, ($tx + 10), ($ry + 8))
    $g.DrawString($r[1], $r[2], $brush, ($tx + 150), ($ry + ($rowH - $r[2].Height) / 2))
    $ry += $rowH
}
$g.DrawLine($thin, ($tx + 140), $ty, ($tx + 140), ($ty + $th))

$out = Join-Path $PSScriptRoot 'sample-drawing-shaft.png'
$bmp.Save($out, [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose(); $bmp.Dispose()
Write-Output "Saved $out"
