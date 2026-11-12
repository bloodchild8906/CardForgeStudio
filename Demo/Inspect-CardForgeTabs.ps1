param([ValidateSet('Play','Decks','Inventory','Store','Profile')][string]$Tab='Play')
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class CardForgeUiClick {
    [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr window, out Rect rectangle);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint x, uint y, uint data, UIntPtr extra);
}
"@
$p=Get-Process CardForgeStudio -ErrorAction Stop|Select-Object -First 1
$rectangle=New-Object CardForgeUiClick+Rect
if(![CardForgeUiClick]::GetWindowRect($p.MainWindowHandle,[ref]$rectangle)){throw 'Could not find game window'}
$offset = switch($Tab){'Play'{235} 'Decks'{315} 'Inventory'{410} 'Store'{490} 'Profile'{575}}
[CardForgeUiClick]::SetForegroundWindow($p.MainWindowHandle)|Out-Null
Start-Sleep -Milliseconds 350
[CardForgeUiClick]::SetCursorPos($rectangle.Left+$offset,$rectangle.Top+71)|Out-Null
[CardForgeUiClick]::mouse_event(2,0,0,0,[UIntPtr]::Zero)
Start-Sleep -Milliseconds 130
[CardForgeUiClick]::mouse_event(4,0,0,0,[UIntPtr]::Zero)
Start-Sleep -Seconds 3
$w=$rectangle.Right-$rectangle.Left
$h=$rectangle.Bottom-$rectangle.Top
$b=[Drawing.Bitmap]::new($w,$h)
$g=[Drawing.Graphics]::FromImage($b)
try{$g.CopyFromScreen($rectangle.Left,$rectangle.Top,0,0,[Drawing.Size]::new($w,$h))}finally{$g.Dispose()}
$path="D:\CardForgeStudio\Logs\CardForgeClient-UI-$Tab.png"
$b.Save($path,[Drawing.Imaging.ImageFormat]::Png);$b.Dispose()
"Captured $Tab $w x $h -> $path"
