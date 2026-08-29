# Generates pastel.ico — gradient rounded square with a white clipboard glyph
Add-Type -AssemblyName System.Drawing

function New-IconPng([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.Clear([System.Drawing.Color]::Transparent)

    $pad = [Math]::Max(1, [int]($size * 0.04))
    $rect = New-Object System.Drawing.Rectangle($pad, $pad, ($size - 2*$pad), ($size - 2*$pad))
    $radius = [int]($size * 0.22)

    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $radius * 2
    $path.AddArc($rect.X, $rect.Y, $d, $d, 180, 90)
    $path.AddArc($rect.Right - $d, $rect.Y, $d, $d, 270, 90)
    $path.AddArc($rect.Right - $d, $rect.Bottom - $d, $d, $d, 0, 90)
    $path.AddArc($rect.X, $rect.Bottom - $d, $d, $d, 90, 90)
    $path.CloseFigure()

    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        $rect,
        [System.Drawing.Color]::FromArgb(255, 240, 147, 251),
        [System.Drawing.Color]::FromArgb(255, 245, 87, 108),
        45.0)
    $g.FillPath($brush, $path)

    # white clipboard glyph: board + clip
    $penW = [Math]::Max(1.5, $size * 0.055)
    $pen = New-Object System.Drawing.Pen([System.Drawing.Color]::White, $penW)
    $pen.LineJoin = 'Round'
    $bx = $size * 0.30; $by = $size * 0.26
    $bw = $size * 0.40; $bh = $size * 0.48
    $br = $size * 0.06
    $board = New-Object System.Drawing.Drawing2D.GraphicsPath
    $bd = $br * 2
    $board.AddArc($bx, $by, $bd, $bd, 180, 90)
    $board.AddArc($bx + $bw - $bd, $by, $bd, $bd, 270, 90)
    $board.AddArc($bx + $bw - $bd, $by + $bh - $bd, $bd, $bd, 0, 90)
    $board.AddArc($bx, $by + $bh - $bd, $bd, $bd, 90, 90)
    $board.CloseFigure()
    $g.DrawPath($pen, $board)
    # clip on top
    $clipW = $size * 0.18; $clipH = $size * 0.09
    $clipX = $size/2 - $clipW/2; $clipY = $by - $clipH/2
    $clipBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::White)
    $g.FillRectangle($clipBrush, [single]$clipX, [single]$clipY, [single]$clipW, [single]$clipH)
    # content lines
    $lp = New-Object System.Drawing.Pen([System.Drawing.Color]::White, ([Math]::Max(1, $size * 0.04)))
    $lp.StartCap = 'Round'; $lp.EndCap = 'Round'
    $lx1 = $bx + $bw*0.2; $lx2 = $bx + $bw*0.8
    $g.DrawLine($lp, [single]$lx1, [single]($by + $bh*0.32), [single]$lx2, [single]($by + $bh*0.32))
    $g.DrawLine($lp, [single]$lx1, [single]($by + $bh*0.55), [single]$lx2, [single]($by + $bh*0.55))
    $g.DrawLine($lp, [single]$lx1, [single]($by + $bh*0.78), [single]($bx + $bw*0.55), [single]($by + $bh*0.78))

    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    return ,$ms.ToArray()
}

$sizes = 16, 24, 32, 48, 64, 128, 256
$pngs = @()
foreach ($s in $sizes) { $pngs += ,(New-IconPng $s) }

$out = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter($out)
$bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $data = $pngs[$i]
    $bw.Write([Byte]($(if ($s -ge 256) { 0 } else { $s })))
    $bw.Write([Byte]($(if ($s -ge 256) { 0 } else { $s })))
    $bw.Write([Byte]0); $bw.Write([Byte]0)
    $bw.Write([UInt16]1); $bw.Write([UInt16]32)
    $bw.Write([UInt32]$data.Length)
    $bw.Write([UInt32]$offset)
    $offset += $data.Length
}
foreach ($data in $pngs) { $bw.Write($data) }
$bw.Flush()
[IO.File]::WriteAllBytes((Join-Path $PSScriptRoot 'pastel.ico'), $out.ToArray())
Write-Host "pastel.ico written ($($out.Length) bytes)"
