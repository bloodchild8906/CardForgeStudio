$ErrorActionPreference='Stop'
$root='D:\CardForgeStudio'
$path=Join-Path $root 'Assets\CardForgeStudio\CardForgeDedicatedServer\Runtime\Simulation\Simulation.Engine.cs'
$s=[IO.File]::ReadAllText($path)
function Swap($a,$b,$name){
 if(-not $script:s.Contains($a)){throw "Missing engine patch: $name"}
 $script:s=$script:s.Replace($a,$b)
}
Swap 'public sealed class SimulationEngine' 'public sealed partial class SimulationEngine' 'partial class'
Swap 'if (originZone?.immovable == true)' 'if (originZone?.immovable == true && !Same(action.id, "ar-level-mana"))' 'immovable level exception'
$start=$s.IndexOf('            var extraEnergyCost = 0L;', $s.IndexOf('public void Execute('))
$end=$s.IndexOf('            Pay(pack, game, actorId, action.costs);', $start)
if($start -lt 0 -or $end -lt 0){throw 'Extra cost region missing'}
$replacement=@'
            var extraEnergyCost = 0L;
            var baseEnergy = action.costs.Where(cost => Same(cost.resourceId, "energy"))
                .Sum(cost => cost.amount);
            if (source != null && Same(pack.id, "astral-rift"))
                extraEnergyCost = AstralExtraCost(pack, game, actorId, action, source, request);
            else if (source != null && Same(action.sourceZone, "hand") &&
                Same(action.destinationZone, "battlefield"))
            {
                var card = pack.cards.FirstOrDefault(entry => Same(entry.id, source.cardId));
                extraEnergyCost = Math.Max(0, (card?.cost ?? 0) - baseEnergy);
            }
            if (extraEnergyCost > 0 &&
                FindResource(game, actorId, "energy").value < baseEnergy + extraEnergyCost)
                throw new InvalidOperationException("Insufficient mana to perform " + action.name + ".");

'@
$s=$s.Substring(0,$start)+$replacement+$s.Substring($end)
Swap @'
            if (home != null && home.returnOnLeave && Same(from, "battlefield") &&
                !Same(to, "battlefield"))
                to = home.id;
'@ @'
            var commanderReturned = home != null && home.returnOnLeave &&
                Same(from, "battlefield") && !Same(to, "battlefield");
            if (commanderReturned) to = home.id;
'@ 'commander return'
Swap @'
            source.cards.Remove(card);
            dest.cards.Add(card);
            if (home != null && home.returnOnLeave && Same(to, home.id))
                Emit(game, "commander_returned", definition.name + " returned to Command.", owner);
'@ @'
            source.cards.Remove(card);
            dest.cards.Add(card);
            // Damage is reset when a card changes zones.
            PutState(game, "damage:" + card.instanceId, 0);
            if (Same(to, "battlefield"))
                PutState(game, "entered:" + card.instanceId, game.turn);
            if (commanderReturned)
            {
                var next = StateNumber(game, "tax:" + card.instanceId) + 1;
                PutState(game, "tax:" + card.instanceId, next);
                Emit(game, "commander_returned", definition.name +
                    " returned to Command; future cast cost increased by +" + (next * 2) + ".", owner);
            }
'@ 'commander tax'
Swap @'
            switch ((effect.kind ?? "").ToLowerInvariant())
            {
                case "draw":
'@ @'
            switch ((effect.kind ?? "").ToLowerInvariant())
            {
                case "level_mana_source":
                    LevelManaSource(pack, game, actor, request);
                    break;
                case "attack_player":
                    ResolveAttack(pack, game, actor, request, false);
                    break;
                case "attack_creature":
                    ResolveAttack(pack, game, actor, request, true);
                    break;
                case "draw":
'@ 'effect implementation'
Swap @'
            game.activePlayerId = game.players[i];
            game.currentPhaseId = pack.phases[0].id;
            game.turn++;
            ApplyManaSourceTurnStart(pack, game);
'@ @'
            game.activePlayerId = game.players[i];
            game.currentPhaseId = pack.phases[0].id;
            game.turn++;
            foreach (var zone in game.zones.Where(z => Same(z.zoneId, "battlefield")))
                foreach (var card in zone.cards)
                    PutState(game, "damage:" + card.instanceId, 0);
            ApplyManaSourceTurnStart(pack, game);
'@ 'damage reset'
Swap @'
                events = game.events.Skip(Math.Max(0, game.events.Count - 30)).ToList()
'@ @'
                events = game.events.Skip(Math.Max(0, game.events.Count - 30)).ToList(),
                data = game.data.Where(item =>
                    item.key.StartsWith("level:") || item.key.StartsWith("tax:") ||
                    item.key.StartsWith("damage:") || item.key.StartsWith("exh:") ||
                    item.key.StartsWith("entered:"))
                    .Select(item => new SimParameter { key = item.key, value = item.value }).ToList()
'@ 'viewer data'
Swap @'
            if (!game.finished && Same(game.activePlayerId, viewerId))
            {
                var phase = pack.phases.FirstOrDefault(p => Same(p.id, game.currentPhaseId));
                if (phase != null) view.allowedActions.AddRange(phase.allowedActions);
            }
'@ @'
            if (!game.finished)
            {
                var phase = pack.phases.FirstOrDefault(p => Same(p.id, game.currentPhaseId));
                if (phase != null)
                    view.allowedActions.AddRange(phase.allowedActions.Where(id =>
                        Same(game.activePlayerId, viewerId) ||
                        pack.actions.Any(action => Same(action.id, id) && !action.requiresActivePlayer)));
            }
'@ 'reactive actions'
[IO.File]::WriteAllText($path,$s,[Text.UTF8Encoding]::new($false))
$native=Join-Path $root 'Assets\CardForgeStudio\CardForgeDedicatedServer\Runtime\NativeServerApi.cs'
$s=[IO.File]::ReadAllText($native)
$needle='                allowedActions = allowed,'+[Environment]::NewLine+'                events = view.events.Select(e => new'
if(!$s.Contains($needle)){throw 'Native API view serial patch absent'}
$s=$s.Replace($needle,'                allowedActions = allowed,'+[Environment]::NewLine+'                data = view.data.Select(item => new { key = item.key, value = item.value }).ToArray(),'+[Environment]::NewLine+'                events = view.events.Select(e => new')
[IO.File]::WriteAllText($native,$s,[Text.UTF8Encoding]::new($false))
$dto=Join-Path $root 'Assets\CardForgeStudio\CardForgeClient\Runtime\Models\TcgModels.cs'
$s=[IO.File]::ReadAllText($dto)
$needle='        public GameEventDto[] events = Array.Empty<GameEventDto>();'
if(!$s.Contains($needle)){throw 'DTO view field absent'}
$s=$s.Replace($needle,$needle+[Environment]::NewLine+'        public ClientGameDataDto[] data = Array.Empty<ClientGameDataDto>();')
$needle='    [Serializable]'+[Environment]::NewLine+'    public sealed class ExecuteGameActionRequestDto'
if(!$s.Contains($needle)){throw 'DTO class insertion absent'}
$s=$s.Replace($needle,'    [Serializable]'+[Environment]::NewLine+'    public sealed class ClientGameDataDto'+[Environment]::NewLine+'    {'+[Environment]::NewLine+'        public string key, value;'+[Environment]::NewLine+'    }'+[Environment]::NewLine+[Environment]::NewLine+$needle)
[IO.File]::WriteAllText($dto,$s,[Text.UTF8Encoding]::new($false))
$proj=Join-Path $root 'Shared\CardForge.Simulation\CardForge.Simulation.csproj'
$s=[IO.File]::ReadAllText($proj)
if(!$s.Contains('Simulation.Astral.cs')){$s=$s.Replace('<Compile Include="../../Assets/CardForgeStudio/CardForgeDedicatedServer/Runtime/Simulation/Simulation.Engine.cs" Link="Simulation.Engine.cs"/>','<Compile Include="../../Assets/CardForgeStudio/CardForgeDedicatedServer/Runtime/Simulation/Simulation.Engine.cs" Link="Simulation.Engine.cs"/>'+[Environment]::NewLine+'  <Compile Include="../../Assets/CardForgeStudio/CardForgeDedicatedServer/Runtime/Simulation/Simulation.Astral.cs" Link="Simulation.Astral.cs"/>');[IO.File]::WriteAllText($proj,$s)}
$proj=Join-Path $root 'Shared\CardForge.DedicatedServer.Validation\CardForge.DedicatedServer.Validation.csproj'
$s=[IO.File]::ReadAllText($proj)
if(!$s.Contains('Simulation.Astral.cs') -and $s.Contains('Simulation.Engine.cs')){
 $s=[regex]::Replace($s,'(?m)^(\s*<Compile Include="[^"]*Simulation.Engine.cs"[^\r\n]*\r?\n)','$1    <Compile Include="../../Assets/CardForgeStudio/CardForgeDedicatedServer/Runtime/Simulation/Simulation.Astral.cs" Link="Simulation.Astral.cs" />'+[Environment]::NewLine)
 [IO.File]::WriteAllText($proj,$s)
}
$r=Join-Path $root 'Assets\StreamingAssets\CardForgeStudio\CardForge\Rules\astral-rift.json'
$o=Get-Content $r -Raw | ConvertFrom-Json
foreach($v in $o.victoryRules){$v.kind='resource-threshold';$v.outcome='eliminate'}
foreach($a in $o.actions){
 if($a.id -eq 'ar-attack-player'){$a.effects[0]['target']='target_player'}
 if($a.id -eq 'ar-attack-creature'){$a.effects[0]['target']='target_card'}
}
[IO.File]::WriteAllText($r,(ConvertTo-Json $o -Depth 40),[Text.UTF8Encoding]::new($false))
Write-Output 'Astral Rift server rules, state, reactive spells, commander tax, mana leveling and combat installed.'
Set-Location $root
dotnet build Shared\CardForge.Simulation\CardForge.Simulation.csproj --no-restore -v quiet
