$ErrorActionPreference='Stop'
$p='D:\CardForgeStudio\Assets\CardForgeStudio\CardForgeClient\Runtime\UI\CardForgeToolkitClient.cs'
$s=[IO.File]::ReadAllText($p)
$a=$s.IndexOf('        private void BuildShell()')
$b=$s.IndexOf('        private static Color ColorFrom(', $a)
if($a -lt 0 -or $b -lt 0){throw 'Legacy shell block not found'}
$s=$s.Substring(0,$a)+$s.Substring($b)
$a=$s.IndexOf('        private void DrawPlayer(')
$b=$s.IndexOf('        private void DrawActions()', $a)
if($a -lt 0 -or $b -lt 0){throw 'Legacy match rendering block not found'}
$s=$s.Substring(0,$a)+$s.Substring($b)
[IO.File]::WriteAllText($p,$s,(New-Object Text.UTF8Encoding($false)))
foreach($f in @('PatchArenaUi.py','PatchArenaUi.ps1','RepairGallery.ps1')){
    $target=Join-Path 'D:\CardForgeStudio\Demo' $f
    if(Test-Path $target){Remove-Item -LiteralPath $target -Force}
}
'Removed unused legacy scroll battlefield and old shell, plus transient patch scripts.'
