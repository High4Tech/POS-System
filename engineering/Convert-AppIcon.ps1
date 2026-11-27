[CmdletBinding()]
param([Parameter(Mandatory)][string]$Source,[Parameter(Mandatory)][string]$Destination)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Drawing
$original=[Drawing.Image]::FromFile((Resolve-Path -LiteralPath $Source).Path)
try {
    $frames=foreach($size in @(256,128,64,48,32,16)) {
        $bitmap=[Drawing.Bitmap]::new($size,$size,[Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $graphics=[Drawing.Graphics]::FromImage($bitmap)
        try {
            $graphics.InterpolationMode=[Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $graphics.DrawImage($original,0,0,$size,$size)
            $memory=[IO.MemoryStream]::new()
            try {$bitmap.Save($memory,[Drawing.Imaging.ImageFormat]::Png);[pscustomobject]@{Size=$size;Bytes=$memory.ToArray()}} finally {$memory.Dispose()}
        } finally {$graphics.Dispose();$bitmap.Dispose()}
    }
    $stream=[IO.File]::Create([IO.Path]::GetFullPath($Destination));$writer=[IO.BinaryWriter]::new($stream)
    try {
        $writer.Write([uint16]0);$writer.Write([uint16]1);$writer.Write([uint16]$frames.Count)
        $offset=6+16*$frames.Count
        foreach($frame in $frames){$dimension=if($frame.Size -eq 256){0}else{$frame.Size};$writer.Write([byte]$dimension);$writer.Write([byte]$dimension);$writer.Write([byte]0);$writer.Write([byte]0);$writer.Write([uint16]1);$writer.Write([uint16]32);$writer.Write([uint32]$frame.Bytes.Length);$writer.Write([uint32]$offset);$offset+=$frame.Bytes.Length}
        foreach($frame in $frames){$writer.Write([byte[]]$frame.Bytes)}
    } finally {$writer.Dispose();$stream.Dispose()}
} finally {$original.Dispose()}
