$ErrorActionPreference='Stop'
$root='D:\CardForgeStudio'
$path=Join-Path $root 'Assets\CardForgeStudio\CardForgeClient\Runtime\UI\CardForgeArenaPresentation.cs'
$s=[IO.File]::ReadAllText($path)
function Change([string]$old,[string]$new,[string]$name){
  if(!$script:s.Contains($old)){throw "Missing edit anchor: $name"}
  $script:s=$script:s.Replace($old,$new)
  Write-Output "Changed: $name"
}
Change '        private static VisualElement Flex(bool horizontal = false)' @'
        private CardDefinitionDto hoveredArenaCard;
        private VisualElement arenaInspector;

        private static VisualElement Flex(bool horizontal = false)
'@ 'Inspector references'
Change @'
            var body = Flex();
            body.style.height = miniature ? 0 : small ? 23 : 63;
'@ @'
            var body = Flex();
            body.style.height = miniature ? 0 : small ? 23 : 63;
            if (miniature) body.style.display = DisplayStyle.None;
'@ 'Compact card artwork area'
Change @'
            face.tooltip = definition.name + " / " + definition.type + "\n" +
                (definition.text ?? "") + "\nClick to inspect or select.";
'@ @'
            face.tooltip = definition.name + " / " + definition.type + "\n" +
                (definition.text ?? "") + "\nClick to inspect or select.";
            face.RegisterCallback<PointerEnterEvent>(_ =>
            {
                if (arenaInspector == null || arenaInspector.panel == null) return;
                hoveredArenaCard = definition;
                RenderArenaInspector();
            });
            face.RegisterCallback<PointerLeaveEvent>(_ =>
            {
                if (arenaInspector == null || arenaInspector.panel == null ||
                    hoveredArenaCard != definition) return;
                hoveredArenaCard = null;
                RenderArenaInspector();
            });
'@ 'Hover inspector from face-up cards'
Change @'
            panel.style.minHeight = seatCount > 2 ? 99 : 149;
'@ @'
            panel.style.minHeight = seatCount >= 4 ? 96 : seatCount == 3 ? 109 : 149;
'@ 'Four-opponent compact seats'
Change @'
            var faceWidth = seatCount >= 3 ? 61 : seatCount == 2 ? 70 : 77;
            var lanes = Flex(true);
            lanes.style.flexGrow = 1;
            lanes.style.flexShrink = 1;
            lanes.style.alignItems = Align.Center;
            ArenaZone(lanes, player, "command", "Commander", faceWidth);
            ArenaZone(lanes, player, "mana-source", "Mana source", faceWidth);
            ArenaZone(lanes, player, "battlefield", "Frontline", faceWidth);
            ArenaZone(lanes, player, "deck", "Library", 43, true);
            if (opponent) ArenaZone(lanes, player, "hand", "Hand", 43, true);
            ArenaZone(lanes, player, "discard", "Discard", faceWidth, false, true);
            ArenaZone(lanes, player, "void", "Exile", faceWidth, false, true);
'@ @'
            var faceWidth = seatCount >= 4 ? 49 : seatCount == 3 ? 56 : seatCount == 2 ? 70 : 77;
            // Never allow growing battlefields to push other players out of view.
            // Scrolling is per player's lane; every opponent seat remains visible.
            var lanes = new ScrollView(ScrollViewMode.Horizontal);
            lanes.style.flexGrow = 1;
            lanes.style.flexShrink = 1;
            lanes.style.minWidth = 0;
            lanes.style.height = seatCount >= 4 ? 96 : seatCount == 3 ? 107 : 138;
            lanes.horizontalScrollerVisibility = ScrollerVisibility.Auto;
            lanes.verticalScrollerVisibility = ScrollerVisibility.Hidden;
            lanes.contentContainer.style.flexDirection = FlexDirection.Row;
            lanes.contentContainer.style.alignItems = Align.Center;
            ArenaZone(lanes.contentContainer, player, "command", "Commander", faceWidth);
            ArenaZone(lanes.contentContainer, player, "mana-source", "Mana source", faceWidth);
            ArenaZone(lanes.contentContainer, player, "battlefield", "Frontline", faceWidth);
            ArenaZone(lanes.contentContainer, player, "deck", "Library", 43, true);
            if (opponent) ArenaZone(lanes.contentContainer, player, "hand", "Hand", 43, true);
            ArenaZone(lanes.contentContainer, player, "discard", "Discard", faceWidth, false, true);
            ArenaZone(lanes.contentContainer, player, "void", "Exile", faceWidth, false, true);
'@ 'Horizontal zone scrolling'
Change @'
            var hand = Flex(true);
            hand.style.alignItems = Align.Center;
            var handWidth = opponents.Length >= 3 ? 73 : 88;
            foreach (var card in ownedHand)
'@ @'
            var hand = new ScrollView(ScrollViewMode.Horizontal);
            hand.style.maxHeight = opponents.Length >= 4 ? 111 : opponents.Length == 3 ? 125 : 156;
            hand.style.flexShrink = 0;
            hand.horizontalScrollerVisibility = ScrollerVisibility.Auto;
            hand.verticalScrollerVisibility = ScrollerVisibility.Hidden;
            hand.contentContainer.style.flexDirection = FlexDirection.Row;
            hand.contentContainer.style.alignItems = Align.Center;
            var handWidth = opponents.Length >= 4 ? 60 : opponents.Length == 3 ? 70 : 88;
            foreach (var card in ownedHand)
'@ 'Horizontal hand scrolling'
Change @'
                hand.Add(string.IsNullOrWhiteSpace(card.cardId) ? MakeCommonBack(handWidth) :
'@ @'
                hand.contentContainer.Add(string.IsNullOrWhiteSpace(card.cardId) ? MakeCommonBack(handWidth) :
'@ 'Hand content container'
Change @'
            content.Clear();
            var titleBar = Flex(true);
'@ @'
            content.Clear();
            arenaInspector = null;
            hoveredArenaCard = null;
            var titleBar = Flex(true);
'@ 'Clear stale hover on match redraw'
$from='            inspector.Add(Label("CARD INSPECTOR", 15, CommandTone));'
$to='            actionBar = Flex(true);'
$a=$s.IndexOf($from)
$b=$s.IndexOf($to,$a)
if($a -lt 0 -or $b -lt 0){throw 'Inspector block anchors not found'}
$replace=@'
            arenaInspector = inspector;
            RenderArenaInspector();

'@
$s=$s.Substring(0,$a)+$replace+$s.Substring($b)
$anchor='        private void ShowArenaMatch()'
$pos=$s.IndexOf($anchor)
if($pos -lt 0){throw 'Render inspector insert anchor missing'}
$method=@'
        // Only the public, server-authorized card definition is shown here.
        // A hovered card previews temporarily; the click selection stays intact.
        private void RenderArenaInspector()
        {
            if (arenaInspector == null || arenaInspector.panel == null) return;
            arenaInspector.Clear();
            var title = Label("CARD INSPECTOR", 15, CommandTone);
            title.style.marginBottom = 5;
            arenaInspector.Add(title);

            var definition = hoveredArenaCard ?? (selected == null ? null : DefinitionFor(selected));
            if (definition == null)
            {
                arenaInspector.Add(Label(
                    "SELECT OR HOVER A FACE-UP CARD\n\nRead every visible card's full abilities here. " +
                    "Your opponent's concealed cards remain hidden.", 12, Soft));
                arenaInspector.Add(Label(
                    "\nCREATURE  •  Attack / defend\nSPELL  •  Main phase\n" +
                    "QUICKSPELL  •  Any turn\nMANA SOURCE  •  Passive bonuses\n" +
                    "COMMANDER  •  Command zone", 11, Pale));
                return;
            }

            var isSelection = selected != null && DefinitionFor(selected) == definition;
            arenaInspector.Add(Label(
                (hoveredArenaCard != null ? "PREVIEW" : isSelection ? "SELECTED" : "CARD") +
                "  •  " + CardKind(definition), 10, TypeColor(CardKind(definition))));
            int previewWidth = arenaInspector.style.width.value.value <= 240 ? 190 : 208;
            arenaInspector.Add(MakeCardFace(definition, previewWidth, null, isSelection));
            var details = new ScrollView(ScrollViewMode.Vertical);
            details.style.flexGrow = 1;
            details.style.minHeight = 0;
            details.Add(Label(definition.name.ToUpperInvariant(), 16, Pale));
            details.Add(Label("COST " + definition.cost +
                "   •   " + definition.type, 11, CommandTone));
            if (CardKind(definition) == "CREATURE" || CardKind(definition) == "COMMANDER")
                details.Add(Label("POWER " + definition.power +
                    "  /  TOUGHNESS " + definition.toughness, 12, CreatureTone));
            var fullRules = Label(definition.text ?? "No additional rules text.", 12, Pale);
            fullRules.style.marginTop = 8;
            details.Add(fullRules);
            arenaInspector.Add(details);
            if (selected != null)
            {
                var clear = UiButton("CLEAR SELECTION  (ESC)", () =>
                {
                    selected = null;
                    targetAction = null;
                    ShowMatch();
                });
                clear.style.marginTop = 7;
                arenaInspector.Add(clear);
            }
        }

'@
$s=$s.Substring(0,$pos)+$method+$s.Substring($pos)
[IO.File]::WriteAllText($path,$s,[Text.UTF8Encoding]::new($false))
Write-Output "Arena UI changes saved to $path"
