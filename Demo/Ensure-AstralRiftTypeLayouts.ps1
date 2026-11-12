param([switch]$Rebuild)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Drawing
$root=Split-Path $PSScriptRoot -Parent
$assets=Join-Path $root 'Assets\CardForgeData\blocks\astral-rift-core'
$layouts=Join-Path $assets 'layouts'
$cards=Join-Path $assets 'cards'
$blockFile=Join-Path $assets 'block.json'
$baseFile=Join-Path $layouts 'ar-layout-front.json'
$frameDir=Join-Path $root 'Assets\CardForgeStudio\CardForgeData\demo\astral-rift\frames'
if(!(Test-Path $baseFile)){throw "Base card layout missing: $baseFile"}
New-Item -Path $frameDir -ItemType Directory -Force|Out-Null
$utf8=[Text.UTF8Encoding]::new($false)
function WriteJson([string]$path,$obj) {
    [IO.File]::WriteAllText($path,(ConvertTo-Json -InputObject $obj -Depth 75),$utf8)
}
function ColorOf([string]$hex,[int]$alpha=255){
    $hex=$hex.TrimStart('#')
    return [Drawing.Color]::FromArgb($alpha,[Convert]::ToInt32($hex.Substring(0,2),16),
        [Convert]::ToInt32($hex.Substring(2,2),16),[Convert]::ToInt32($hex.Substring(4,2),16))
}
function DrawFrame([string]$path,$spec) {
    if((Test-Path $path) -and !$Rebuild){return}
    $w=360;$h=504
    $b=[Drawing.Bitmap]::new($w,$h,[Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g=[Drawing.Graphics]::FromImage($b)
    try {
        $g.SmoothingMode=[Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $g.Clear((ColorOf $spec.dark))
        $full=[Drawing.Rectangle]::new(0,0,$w,$h)
        $brush=[Drawing.Drawing2D.LinearGradientBrush]::new($full,(ColorOf $spec.base),(ColorOf $spec.dark),90)
        $g.FillRectangle($brush,$full);$brush.Dispose()
        $accent=ColorOf $spec.accent
        $outer=[Drawing.Pen]::new($accent,5)
        $inner=[Drawing.Pen]::new((ColorOf $spec.accent 125),2)
        $g.DrawRectangle($outer,6,6,347,491)
        $g.DrawRectangle($inner,15,15,329,473)
        $g.DrawLine($inner,19,73,341,73)
        $g.DrawLine($inner,19,477,341,477)
        $titleBrush=[Drawing.SolidBrush]::new((ColorOf $spec.dark 170))
        $g.FillRectangle($titleBrush,20,19,320,49)
        $titleBrush.Dispose()
        $artBrush=[Drawing.SolidBrush]::new((ColorOf $spec.dark 110))
        $g.FillRectangle($artBrush,25,80,310,250)
        $artBrush.Dispose()
        $artPen=[Drawing.Pen]::new((ColorOf $spec.accent 180),2)
        $g.DrawRectangle($artPen,24,79,311,252)
        $artPen.Dispose()
        $ruleBrush=[Drawing.SolidBrush]::new((ColorOf $spec.dark 170))
        $g.FillRectangle($ruleBrush,22,341,316,126)
        $ruleBrush.Dispose()
        $g.DrawLine($inner,22,336,338,336)
        # Distinct ornamental silhouettes per type; no text is baked into the frame.
        $motif=[Drawing.Pen]::new((ColorOf $spec.accent 170),3)
        switch($spec.kind) {
            'Commander' {
                $g.DrawLine($motif,57,12,71,27);$g.DrawLine($motif,71,27,85,12)
                $g.DrawLine($motif,85,12,99,27);$g.DrawLine($motif,99,27,114,12)
                $g.DrawLine($motif,58,13,114,13)
            }
            'Creature' {
                $g.DrawArc($motif,18,16,48,54,-40,180)
                $g.DrawArc($motif,295,16,48,54,40,180)
                $g.DrawLine($motif,23,67,46,44)
            }
            'Spell' {
                $g.DrawEllipse($motif,43,29,25,25)
                $g.DrawLine($motif,55,18,55,67)
                $g.DrawLine($motif,31,42,80,42)
            }
            'QuickSpell' {
                $pts=[Drawing.Point[]]@(
                    [Drawing.Point]::new(48,17),[Drawing.Point]::new(34,44),
                    [Drawing.Point]::new(52,44),[Drawing.Point]::new(38,68))
                $g.DrawLines($motif,$pts)
            }
            'ManaSource' {
                $g.DrawEllipse($motif,38,20,42,42)
                $g.DrawEllipse($motif,47,29,24,24)
                $g.DrawEllipse($motif,56,38,6,6)
            }
        }
        $motif.Dispose();$outer.Dispose();$inner.Dispose()
        # Scattered subtle stars, consistent for the generated file.
        $spark=[Drawing.SolidBrush]::new((ColorOf $spec.accent 110))
        for($i=0;$i -lt 22;$i++){
            $x=28+(($i*79+41)%303);$y=80+(($i*61+17)%381)
            $g.FillEllipse($spark,$x,$y,1.5,1.5)
        }
        $spark.Dispose()
        $b.Save($path,[Drawing.Imaging.ImageFormat]::Png)
    } finally {$g.Dispose();$b.Dispose()}
}
$specs=@(
    [pscustomobject]@{kind='Commander';slug='commander';base='#55422A';dark='#191B20';accent='#ECC67E';artY=103;artH=194;typeY=307;rulesY=340;rulesH=87;flavorY=433;flavorH=18;stats=$true},
    [pscustomobject]@{kind='Creature';slug='creature';base='#294F40';dark='#101F23';accent='#92D4B0';artY=97;artH=209;typeY=312;rulesY=347;rulesH=80;flavorY=433;flavorH=18;stats=$true},
    [pscustomobject]@{kind='Spell';slug='spell';base='#313F67';dark='#171D30';accent='#B4BAFF';artY=97;artH=222;typeY=329;rulesY=361;rulesH=92;flavorY=458;flavorH=17;stats=$false},
    [pscustomobject]@{kind='QuickSpell';slug='quickspell';base='#603329';dark='#26171B';accent='#FFC19A';artY=107;artH=178;typeY=294;rulesY=329;rulesH=124;flavorY=457;flavorH=18;stats=$false},
    [pscustomobject]@{kind='ManaSource';slug='manasource';base='#205A60';dark='#0E252A';accent='#8CDFF0';artY=101;artH=231;typeY=340;rulesY=373;rulesH=82;flavorY=459;flavorH=15;stats=$false}
)
$block=Get-Content $blockFile -Raw -Encoding UTF8|ConvertFrom-Json
$created=0;$assigned=0
foreach($spec in $specs){
    $id='ar-layout-'+$spec.slug
    $file=Join-Path $layouts ($id+'.json')
    $framePath=Join-Path $frameDir ('front-'+$spec.slug+'.png')
    DrawFrame $framePath $spec
    if(!(Test-Path $file) -or $Rebuild){
        $layout=Get-Content $baseFile -Raw -Encoding UTF8|ConvertFrom-Json
        $layout.id=$id
        $layout.name=($spec.kind+' - Astral Rift Front')
        $layout.layoutType=($spec.kind+' portrait')
        $layout.backgroundAssetPath=('Assets/CardForgeStudio/CardForgeData/demo/astral-rift/frames/front-'+$spec.slug+'.png')
        $layout.backgroundColor=$spec.dark
        $layout.templates=@($layout.templates)
        foreach($template in $layout.templates){
            $template.backgroundColor=if($template.id -eq 'ar-template-title') {$spec.base+'DD'}else{$spec.dark+'E8'}
            # Separate template IDs per front so they can be independently edited.
            $template.id=$template.id+'-'+$spec.slug
        }
        foreach($zone in $layout.zones){
            if($zone.templateId){$zone.templateId+="-"+$spec.slug}
            switch($zone.id) {
                'ar-zone-art' {$zone.y=$spec.artY;$zone.height=$spec.artH}
                'ar-zone-type' {$zone.y=$spec.typeY;$zone.fontSize=14;$zone.color=$spec.accent}
                'ar-zone-affinity' {$zone.y=$spec.typeY}
                'ar-zone-rules' {$zone.y=$spec.rulesY;$zone.height=$spec.rulesH;$zone.fontSize=14}
                'ar-zone-flavor' {$zone.y=$spec.flavorY;$zone.height=$spec.flavorH;$zone.fontSize=11}
                'ar-zone-power' {$zone.visible=[bool]$spec.stats}
                'ar-zone-toughness' {$zone.visible=[bool]$spec.stats}
                'ar-zone-title' {$zone.color='#FFF3D9';$zone.fontSize=22}
                'ar-zone-cost' {$zone.color=$spec.accent}
            }
        }
        # Keeps the type identity as an editable static-text zone, never flattened into artwork.
        $layout.zones=@($layout.zones)+@([ordered]@{
            locked=$false;id=('ar-zone-kind-'+$spec.slug);name='Card family label';
            kind=4;fieldId='';staticText=$spec.kind.ToUpperInvariant();
            staticAssetPath='';x=110;y=77;width=140;height=16;order=12;
            offsetX=0;offsetY=0;fontSize=11;fontAssetPath='';
            color=$spec.accent;backgroundColor='#00000000';templateId='';
            bold=$true;visible=$true;alignment='MiddleCenter';layers=@();variants=@()
        })
        WriteJson $file $layout
        $created++
    }
    if((Test-Path $file) -and !$Rebuild){
        $existing=Get-Content $file -Raw -Encoding UTF8|ConvertFrom-Json
        if($existing.name.Contains([char]0x00C2)){
            $existing.name=$spec.kind+' - Astral Rift Front'
            WriteJson $file $existing
        }
    }
    if($id -notin @($block.layoutIds)){$block.layoutIds=@($block.layoutIds)+@($id)}
}
$mapping=@{}
foreach($s in $specs){$mapping[$s.kind]='ar-layout-'+$s.slug}
foreach($id in @($block.cardIds)){
    $path=Join-Path $cards ($id+'.json')
    if(!(Test-Path $path)){continue}
    $card=Get-Content $path -Raw -Encoding UTF8|ConvertFrom-Json
    $cardType=[string]$card.cardType
    if(!$mapping.ContainsKey($cardType)){continue}
    $expected=$mapping[$cardType]
    if($card.layoutId -ne $expected){
        $card.layoutId=$expected;WriteJson $path $card;$assigned++
    }
}
WriteJson $blockFile $block
Write-Output "TYPE_LAYOUTS_OK: $($specs.Count) front layouts, $created created/refreshed, $assigned card bindings updated."
