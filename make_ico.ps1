Add-Type -AssemblyName System.Drawing
$bmp = New-Object System.Drawing.Bitmap 64, 64
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias

# Background circle: Modern Windows Accent (#0078D4)
$brush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(0, 120, 215))
$g.FillEllipse($brush, 2, 2, 60, 60)

# Pen for camera/screenshot icon
$pen = New-Object System.Drawing.Pen([System.Drawing.Color]::White, 3.5)
$pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
$pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round

# Camera body
$g.DrawRectangle($pen, 17, 21, 30, 24)
# Lens
$g.DrawEllipse($pen, 26, 27, 12, 12)
# Flash / Shutter
$g.FillRectangle([System.Drawing.Brushes]::White, 21, 16, 7, 4)

$g.Dispose()
$hicon = $bmp.GetHicon()
$icon = [System.Drawing.Icon]::FromHandle($hicon)
$fs = [System.IO.File]::OpenWrite('G:\Git\CapToPath\src\app.ico')
$icon.Save($fs)
$fs.Close()
$icon.Dispose()
$bmp.Dispose()
Write-Host "Icon created successfully!"
