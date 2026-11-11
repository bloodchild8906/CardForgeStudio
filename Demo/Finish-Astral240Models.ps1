$ErrorActionPreference='Stop'
$root='D:\CardForgeStudio'
$p=Join-Path $root 'Assets\CardForgeStudio\CardForge\Runtime\CardForgeModels.cs'
$s=[IO.File]::ReadAllText($p)
$zone='(?s)(public class GameZoneDocument\s*\{.*?public int capacity = 0;)(\s*\})'
if([regex]::Matches($s,$zone).Count -ne 1){throw 'Expected exactly one GameZoneDocument'}
$s=[regex]::Replace($s,$zone,'$1' + [Environment]::NewLine +
 '        public List<string> allowedCardTypes = new List<string>();' + [Environment]::NewLine +
 '        public List<string> initialCardIds = new List<string>();' + '$2')
$deck='(?s)(public class GameDeckDocument\s*\{.*?public List<string> cardIds = new List<string>\(\);)(\s*\})'
if([regex]::Matches($s,$deck).Count -ne 1){throw 'Expected exactly one GameDeckDocument'}
$s=[regex]::Replace($s,$deck,'$1' + [Environment]::NewLine +
 '        public string strategy = "";' + [Environment]::NewLine +
 '        public List<GameDeckZoneSeed> startingZones = new List<GameDeckZoneSeed>();' + '$2')
$anchor='    /// <summary>Normalized board rectangle'
$seed=@'
    [Serializable]
    public class GameDeckZoneSeed
    {
        public string zoneId = "";
        public string cardId = "";
        public int count = 1;
    }

'@
if(!$s.Contains($anchor)){throw 'GameBoardZonePlacement insertion point missing'}
$s=$s.Replace($anchor,$seed+$anchor)
[IO.File]::WriteAllText($p,$s,[Text.UTF8Encoding]::new($false))
$project=Join-Path $root 'CardForge.Editor.csproj'
$csproj=[IO.File]::ReadAllText($project)
if(!$csproj.Contains('CardForgeDemoGenerator.Expanded.cs')){
  $old='<Compile Include="Assets\CardForgeStudio\CardForge\Editor\CardForgeDemoGenerator.cs" />'
  $new=$old+[Environment]::NewLine+'    <Compile Include="Assets\CardForgeStudio\CardForge\Editor\CardForgeDemoGenerator.Expanded.cs" />'
  if(!$csproj.Contains($old)){throw 'Generated editor project line absent'}
  [IO.File]::WriteAllText($project,$csproj.Replace($old,$new),[Text.UTF8Encoding]::new($false))
}
Write-Output 'Updated authoring game zone/deck schema and verification project.'
