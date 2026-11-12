$ErrorActionPreference = 'Stop'
$path = 'D:\CardForgeStudio\Assets\CardForgeStudio\CardForgeClient\Runtime\UI\CardForgeArenaPolish.cs'
$s = [IO.File]::ReadAllText($path)
function Swap([string]$before,[string]$after,[string]$label) {
  $count = ([regex]::Matches($script:s,[regex]::Escape($before))).Count
  if($count -ne 1){throw "Expected one $label anchor, found $count"}
  $script:s = $script:s.Replace($before,$after)
  Write-Output "Updated: $label"
}
Swap @'
            var heroRow = Flex(true);
            heroRow.style.alignItems = Align.Center;
'@ @'
            var heroRow = Flex(true);
            heroRow.style.flexWrap = Wrap.Wrap;
            heroRow.style.alignItems = Align.Center;
'@ 'Responsive hero row'
Swap @'
            var art = Flex(true);
            art.style.justifyContent = Justify.FlexEnd;
'@ @'
            var art = Flex(true);
            art.style.flexWrap = Wrap.Wrap;
            art.style.justifyContent = Justify.FlexEnd;
'@ 'Responsive hero illustrations'
Swap @'
            right.Add(Label("Select a card to add it to the draft. Hover to read its full ability.", 12, Soft));
'@ @'
            right.Add(Label("Select a card to inspect it. Use ADD to include an owned copy in your draft.", 12, Soft));
'@ 'Clarify deck card actions'
$before=@'
            var browse = new ScrollView(ScrollViewMode.Vertical);
            browse.style.flexGrow = 1;
            browse.style.minHeight = 0;
            right.Add(browse);
            var gallery = Flex(true);
            gallery.style.flexWrap = Wrap.Wrap;
            browse.Add(gallery);
'@
$after=@'
            var galleryWorkspace = Flex(true);
            galleryWorkspace.style.flexGrow = 1;
            galleryWorkspace.style.minHeight = 0;
            galleryWorkspace.style.marginTop = 8;
            right.Add(galleryWorkspace);
            var browse = new ScrollView(ScrollViewMode.Vertical);
            browse.style.flexGrow = 1;
            browse.style.flexShrink = 1;
            browse.style.minWidth = 0;
            browse.style.minHeight = 0;
            galleryWorkspace.Add(browse);
            var gallery = Flex(true);
            gallery.style.flexWrap = Wrap.Wrap;
            browse.Add(gallery);
            var cardPreview = UiSurface();
            cardPreview.style.width = 258;
            cardPreview.style.flexShrink = 0;
            cardPreview.style.marginLeft = 8;
            cardPreview.Add(Label("CARD DETAILS", 14, CommandTone));
            cardPreview.Add(Label("Hover or select an owned card to read its full rules.", 11, Soft));
            galleryWorkspace.Add(cardPreview);
'@
Swap $before $after 'Two-column card browser with persistent inspector'
Swap @'
            Label preview = null;
            loadMore = UiButton("LOAD MORE CARDS", () =>
'@ @'
            loadMore = UiButton("LOAD MORE CARDS", () =>
'@ 'Remove truncated hover preview'
Swap @'
            preview = Label("HOVER A CARD  /  FULL RULES AND OWNED COPIES", 11, Soft);
            preview.style.minHeight = 34;
            right.Add(preview);
'@ '' 'Remove old preview label'
$anchor=@'
            void RenderGallery()
            {
'@
$method=@'
            void InspectBrowserCard(CardDefinitionDto definition, int ownedCount)
            {
                cardPreview.Clear();
                cardPreview.Add(Label("CARD DETAILS", 14, CommandTone));
                var category = Label(CardKind(definition) + "  •  " + definition.type, 10,
                    TypeColor(CardKind(definition)));
                category.style.marginBottom = 6;
                cardPreview.Add(category);
                cardPreview.Add(MakeCardFace(definition, 207, null));
                cardPreview.Add(Label("OWNED " + ownedCount + " COPY/COPIES", 11, CommandTone));
                var rulesScroll = new ScrollView(ScrollViewMode.Vertical);
                rulesScroll.style.flexGrow = 1;
                rulesScroll.style.minHeight = 0;
                var fullText = Label(definition.name.ToUpperInvariant() + "\n\n" +
                    (definition.text ?? "No additional rules text."), 12, Pale);
                fullText.style.whiteSpace = WhiteSpace.Normal;
                rulesScroll.Add(fullText);
                cardPreview.Add(rulesScroll);
            }
            void RenderGallery()
            {
'@
Swap $anchor $method 'Deck preview renderer'
$start=$s.IndexOf('                    var cardFace = MakeCardFace(def, 137, () =>')
$end=$s.IndexOf('                    gallery.Add(tile);',$start)
if($start -lt 0 -or $end -lt 0){throw 'Deck gallery card block anchors missing'}
$end += '                    gallery.Add(tile);'.Length
$replacement=@'
                    var cardFace = MakeCardFace(def, 137, () => InspectBrowserCard(def, ownedCount));
                    cardFace.RegisterCallback<PointerEnterEvent>(_ =>
                        InspectBrowserCard(def, ownedCount));
                    tile.Add(cardFace);
                    tile.Add(Label("OWNED  " + ownedCount, 10, CommandTone));
                    var add = UiButton("ADD TO DECK  +", () =>
                    {
                        if (draft.Count(id => string.Equals(id, cardId, StringComparison.OrdinalIgnoreCase)) >= ownedCount)
                        {
                            SetNotice("No more owned copies of " + def.name + ".");
                            return;
                        }
                        if (!ClientDeckLegality.CanAdd(rules, draft, cardId, out var reason))
                        {
                            SetNotice(reason);
                            return;
                        }
                        draft.Add(cardId);
                        InspectBrowserCard(def, ownedCount);
                        RefreshDraft();
                    }, true);
                    add.style.minWidth = 135;
                    add.style.height = 33;
                    add.style.fontSize = 11;
                    tile.Add(add);
                    gallery.Add(tile);
'@
$s=$s.Substring(0,$start)+$replacement+$s.Substring($end)
Write-Output 'Updated: deck gallery uses explicit inspect and add actions'
Swap @'
                resultsLabel.text = groups.Count + " MATCHES  /  SHOWING " + Math.Min(visibleLimit, groups.Count);
'@ @'
                resultsLabel.text = groups.Count + " MATCHES  /  SHOWING " + Math.Min(visibleLimit, groups.Count);
                if (groups.Count > 0)
                {
                    var first = groups[0];
                    InspectBrowserCard(definitions[first.id], first.owned);
                }
                else
                {
                    cardPreview.Clear();
                    cardPreview.Add(Label("NO MATCHING CARDS", 13, Soft));
                }
'@ 'Preview first matching card in each filtered view'
[IO.File]::WriteAllText($path,$s,[Text.UTF8Encoding]::new($false))
Write-Output "Deck UI updated in $path"
