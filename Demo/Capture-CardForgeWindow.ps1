$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class CardForgeWindowCapture {
    [StructLayout(LayoutKind.Sequential)]
    public struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out Rect r);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
}
"@
$p=Get-Process -Id 1836 -ErrorAction Stop
$rect=New-Object CardForgeWindowCapture+Rect
if(![CardForgeWindowCapture]::GetWindowRect($p.MainWindowHandle,[ref]$rect)){throw 'Could not obtain game window bounds'}
$w=$rect.Right-$rect.Left;$h=$rect.Bottom-$rect.Top
if($w -lt 300 -or $h -lt 200){throw "Unexpected game window size $w x $h"}
[CardForgeWindowCapture]::SetForegroundWindow($p.MainWindowHandle)|Out-Null
Start-Sleep -Milliseconds 450
$bmp=[System.Drawing.Bitmap]::new($w,$h)
$g=[System.Drawing.Graphics]::FromImage($bmp)
try {$g.CopyFromScreen($rect.Left,$rect.Top,0,0,[System.Drawing.Size]::new($w,$h))} finally {$g.Dispose()}
$path='D:\CardForgeStudio\Logs\CardForgeClient-UI-Play.png'
$bmp.Save($path,[System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()
"Captured $w x $h gameplay window: $path"
