using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace TcgServer.Client
{
    // Runtime UI Toolkit presentation. Only the server executes game rules.
    public sealed partial class CardForgeToolkitClient
    {
        private static readonly Color ArenaInk = ColorFrom("#090F1D");
        private static readonly Color ArenaPanel = ColorFrom("#131E32");
        private static readonly Color ArenaTrim = ColorFrom("#30425B");
        private static readonly Color CreatureTone = ColorFrom("#5AB58F");
        private static readonly Color SpellTone = ColorFrom("#8A91F2");
        private static readonly Color QuickTone = ColorFrom("#F0A565");
        private static readonly Color ManaTone = ColorFrom("#5BC7E7");
        private static readonly Color CommandTone = ColorFrom("#E7BD68");

        private static VisualElement Flex(bool horizontal = false)
        {
            return new VisualElement { style = { flexDirection = horizontal ? FlexDirection.Row : FlexDirection.Column } };
        }

        private static void Frame(VisualElement view, Color color, int thickness = 1)
        {
            view.style.borderTopWidth = thickness;
            view.style.borderBottomWidth = thickness;
            view.style.borderLeftWidth = thickness;
            view.style.borderRightWidth = thickness;
            view.style.borderTopColor = color;
            view.style.borderBottomColor = color;
            view.style.borderLeftColor = color;
            view.style.borderRightColor = color;
        }

        private static void Round(VisualElement view, float radius = 8)
        {
            view.style.borderTopLeftRadius = radius;
            view.style.borderBottomLeftRadius = radius;
            view.style.borderTopRightRadius = radius;
            view.style.borderBottomRightRadius = radius;
        }

        private void BuildArenaShell()
        {
            root.Clear();
            var shell = Flex();
            shell.style.flexGrow = 1;
            shell.style.backgroundColor = ArenaInk;
            root.Add(shell);
            header = Flex(true);
            header.style.height = 69;
            header.style.flexShrink = 0;
            header.style.alignItems = Align.Center;
            header.style.backgroundColor = ArenaPanel;
            header.style.paddingLeft = 22;
            header.style.paddingRight = 16;
            Frame(header, ArenaTrim);
            shell.Add(header);

            var brand = Flex();
            brand.style.width = 180;
            brand.Add(Label("CARDFORGE", 22, CommandTone));
            brand.Add(Label("ASTRAL RIFT", 10, Soft));
            header.Add(brand);
            sidebar = Flex(true);
            sidebar.style.alignItems = Align.Center;
            sidebar.style.flexGrow = 1;
            header.Add(sidebar);
            foreach (var tab in new[] { "Play", "Decks", "Inventory", "Store", "Profile" })
            {
                var name = tab;
                var button = new Button(() => Show(name)) { text = tab.ToUpperInvariant() };
                button.AddToClassList("cf-nav");
                sidebar.Add(button);
                navigation[name] = button;
            }
            var right = Flex();
            right.style.width = 238;
            right.style.alignItems = Align.FlexEnd;
            identity = Label("SIGNING IN", 12, CommandTone);
            right.Add(identity);
            notice = Label("Connecting...", 10, Soft);
            notice.style.unityTextAlign = TextAnchor.MiddleRight;
            notice.style.maxWidth = 225;
            right.Add(notice);
            var reconnect = UiButton("RETRY", () => StartCoroutine(ConnectDemo()));
            reconnect.style.minWidth = 74;
            reconnect.style.height = 26;
            reconnect.style.fontSize = 10;
            right.Add(reconnect);
            header.Add(right);

            content = Flex();
            content.style.flexGrow = 1;
            content.style.flexShrink = 1;
            content.style.paddingTop = 10;
            content.style.paddingBottom = 10;
            content.style.paddingLeft = 12;
            content.style.paddingRight = 12;
            content.style.overflow = Overflow.Hidden;
            shell.Add(content);
        }

        // Same card back across all types. Card identity and artwork are never used here.
        private VisualElement MakeCommonBack(int width)
        {
            int height = (int)(width * 1.46f);
            var outer = Flex();
            outer.style.width = width;
            outer.style.height = height;
            outer.style.flexShrink = 0;
            outer.style.paddingTop = 5;
            outer.style.paddingBottom = 5;
            outer.style.paddingLeft = 5;
            outer.style.paddingRight = 5;
            outer.style.backgroundColor = ColorFrom("#1D2B48");
            Frame(outer, CommandTone, 2);
            Round(outer, 7);
            outer.AddToClassList("cf-card-back");

            var inside = Flex();
            inside.style.flexGrow = 1;
            inside.style.alignItems = Align.Center;
            inside.style.justifyContent = Justify.Center;
            inside.style.backgroundColor = ColorFrom("#101A32");
            Frame(inside, ColorFrom("#65718D"));
            Round(inside, 5);
            outer.Add(inside);
            var emblem = Label("✦", Mathf.Max(25, width / 3), CommandTone);
            emblem.style.unityTextAlign = TextAnchor.MiddleCenter;
            inside.Add(emblem);
            var name = Label("CARDFORGE", Mathf.Max(8, width / 9), Pale);
            name.style.unityTextAlign = TextAnchor.MiddleCenter;
            inside.Add(name);
            var subtitle = Label("ASTRAL RIFT", Mathf.Max(7, width / 12), Soft);
            subtitle.style.unityTextAlign = TextAnchor.MiddleCenter;
            inside.Add(subtitle);
            return outer;
        }

        private static string CardKind(CardDefinitionDto definition)
        {
            var type = definition?.type?.Trim() ?? "";
            if (type.StartsWith("ManaSource", StringComparison.OrdinalIgnoreCase)) return "MANASOURCE";
            if (type.StartsWith("Commander", StringComparison.OrdinalIgnoreCase) ||
                type.StartsWith("Command", StringComparison.OrdinalIgnoreCase)) return "COMMANDER";
            if (type.StartsWith("QuickSpell", StringComparison.OrdinalIgnoreCase) ||
                type.StartsWith("Event", StringComparison.OrdinalIgnoreCase)) return "QUICKSPELL";
            if (type.StartsWith("Creature", StringComparison.OrdinalIgnoreCase) ||
                type.StartsWith("Unit", StringComparison.OrdinalIgnoreCase)) return "CREATURE";
            if (type.StartsWith("Spell", StringComparison.OrdinalIgnoreCase) ||
                type.StartsWith("Relic", StringComparison.OrdinalIgnoreCase)) return "SPELL";
            return type.ToUpperInvariant();
        }

        private static Color TypeColor(string type)
        {
            switch (type)
            {
                case "CREATURE": return CreatureTone;
                case "SPELL": return SpellTone;
                case "QUICKSPELL": return QuickTone;
                case "MANASOURCE": return ManaTone;
                case "COMMANDER": return CommandTone;
                default: return Soft;
            }
        }

        private void SetCardArtwork(Image art, CardDefinitionDto definition)
        {
            if (definition == null || string.IsNullOrWhiteSpace(definition.id)) return;
            var local = Resources.Load<Texture2D>("AstralRift/Illustrations/" + definition.id);
            if (local != null) { art.image = local; return; }
            var assetId = string.IsNullOrWhiteSpace(definition.assetId)
                ? "astral-rift.card." + definition.id : definition.assetId;
            remoteAssets?.RequestTexture(assetId, texture =>
            {
                if (texture != null) art.image = texture;
            });
        }

        // 5 distinctive face templates, all based on the same real card illustration.
        private VisualElement MakeCardFace(CardDefinitionDto definition, int width, Action click, bool chosen = false)
        {
            if (definition == null) return MakeCommonBack(width);
            var kind = CardKind(definition);
            var color = TypeColor(kind);
            var small = width < 120;
            var miniature = width < 100;
            var height = (int)(width * 1.47f);
            var face = Flex();
            face.style.width = width;
            face.style.height = height;
            face.style.flexShrink = 0;
            face.style.paddingTop = 3;
            face.style.paddingLeft = 4;
            face.style.paddingRight = 4;
            face.style.paddingBottom = 3;
            face.style.marginRight = 5;
            face.style.marginBottom = 3;
            face.style.backgroundColor = chosen ? ColorFrom("#334967") : ArenaInk;
            Frame(face, chosen ? Pale : color, chosen ? 3 : 2);
            Round(face, kind == "COMMANDER" ? 12 : 6);
            face.AddToClassList("cf-card-face");
            face.tooltip = definition.name + " / " + definition.type + "\n" +
                (definition.text ?? "") + "\nClick to inspect or select.";
            if (click != null)
            {
                face.focusable = true;
                face.RegisterCallback<ClickEvent>(_ => click());
                face.RegisterCallback<KeyDownEvent>(evt =>
                {
                    if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.Space)
                    {
                        click();
                        evt.StopPropagation();
                    }
                });
            }

            var head = Flex(true);
            head.style.alignItems = Align.Center;
            head.style.height = miniature ? 16 : small ? 19 : 29;
            head.style.flexShrink = 0;
            head.style.paddingLeft = 4;
            head.style.backgroundColor = kind == "COMMANDER" ? ColorFrom("#534023") :
                kind == "QUICKSPELL" ? ColorFrom("#47302A") : ArenaPanel;
            var title = Label(definition.name, miniature ? 7 : small ? 9 : 15, Pale);
            title.style.flexGrow = 1;
            title.style.whiteSpace = WhiteSpace.NoWrap;
            title.style.overflow = Overflow.Hidden;
            head.Add(title);
            var cost = Label(kind == "MANASOURCE" ? "∞" : definition.cost.ToString(), small ? 10 : 15, color);
            cost.style.marginRight = 4;
            head.Add(cost);
            face.Add(head);

            if (kind == "COMMANDER" || kind == "QUICKSPELL")
            {
                var banner = Label(kind == "COMMANDER" ? "★  COMMAND ZONE  •  LEGENDARY" : "⚡  FAST RESPONSE",
                    small ? 7 : 10, color);
                banner.style.height = miniature ? 9 : small ? 12 : 18;
                banner.style.unityTextAlign = TextAnchor.MiddleCenter;
                face.Add(banner);
            }

            var art = new Image { scaleMode = ScaleMode.ScaleAndCrop };
            art.style.flexGrow = 1;
            art.style.flexShrink = 1;
            art.style.minHeight = miniature ? 16 : small ? 40 : 93;
            art.style.backgroundColor = ColorFrom("#26334B");
            art.style.marginTop = 2;
            art.style.marginBottom = 2;
            face.Add(art);
            SetCardArtwork(art, definition);

            var typeLine = Flex(true);
            typeLine.style.backgroundColor = color;
            typeLine.style.height = miniature ? 12 : small ? 14 : 21;
            typeLine.style.alignItems = Align.Center;
            typeLine.style.paddingLeft = 5;
            var typeName = Label(kind == "MANASOURCE" ? "MANA SOURCE" :
                kind == "QUICKSPELL" ? "QUICKSPELL" : kind, small ? 8 : 11, ArenaInk);
            typeName.style.flexGrow = 1;
            typeLine.Add(typeName);
            var affinity = definition.type?.Split('-').LastOrDefault()?.Trim() ?? "";
            if (!miniature) typeLine.Add(Label(affinity.ToUpperInvariant(), small ? 7 : 10, ArenaInk));
            face.Add(typeLine);

            var body = Flex();
            body.style.height = miniature ? 0 : small ? 23 : 63;
            body.style.flexShrink = 0;
            body.style.paddingTop = 3;
            body.style.paddingLeft = 5;
            body.style.paddingRight = 4;
            body.style.backgroundColor = kind == "SPELL" ? ColorFrom("#242343") :
                kind == "MANASOURCE" ? ColorFrom("#143847") : ArenaPanel;
            var rules = definition.text ?? "";
            if (small && rules.Length > 65) rules = rules.Substring(0, 62) + "...";
            body.Add(Label(rules, small ? 8 : 11, Pale));
            face.Add(body);
            var foot = Flex(true);
            foot.style.height = miniature ? 14 : small ? 16 : 26;
            foot.style.alignItems = Align.Center;
            foot.style.justifyContent = Justify.SpaceBetween;
            foot.style.paddingLeft = 5;
            foot.style.paddingRight = 5;
            switch (kind)
            {
                case "CREATURE":
                    foot.Add(Label("UNIT", small ? 8 : 11, color));
                    foot.Add(Label(definition.power + " / " + definition.toughness, small ? 10 : 14, Pale));
                    break;
                case "COMMANDER":
                    foot.Add(Label(miniature ? "CMD" : "LEGENDARY", small ? 8 : 11, color));
                    foot.Add(Label(definition.power + " / " + definition.toughness, small ? 10 : 14, Pale));
                    break;
                case "MANASOURCE":
                    foot.Add(Label("PASSIVE • LEVELS", small ? 8 : 11, color));
                    foot.Add(Label("I  II  III", small ? 8 : 11, Pale));
                    break;
                case "QUICKSPELL":
                    foot.Add(Label(miniature ? "FAST" : "ANY RESPONSE WINDOW", small ? 8 : 11, color));
                    break;
                case "SPELL":
                    foot.Add(Label(miniature ? "SPELL" : "MAIN PHASE", small ? 8 : 11, color));
                    break;
                default:
                    foot.Add(Label("PERMANENT", small ? 8 : 11, color));
                    break;
            }
            face.Add(foot);
            return face;
        }

        private CardDefinitionDto DefinitionFor(CardInstanceDto instance) =>
            rulePack?.cards?.FirstOrDefault(c => c.id == instance?.cardId);

        private void PickArenaCard(CardInstanceDto instance)
        {
            if (instance == null || string.IsNullOrEmpty(instance.cardId)) return;
            if (targetAction != null && targetAction.target != "target_player")
            {
                Submit(targetAction, selected?.instanceId, instance.instanceId, null);
                return;
            }
            selected = instance;
            ShowMatch();
        }

        private void ArenaZone(VisualElement row, string owner, string zoneId, string label,
            int cardWidth = 77, bool concealed = false, bool pile = false)
        {
            var cards = (match.zones ?? Array.Empty<ClientGameZoneDto>())
                .FirstOrDefault(z => z.playerId == owner && z.zoneId == zoneId)?.cards ??
                Array.Empty<CardInstanceDto>();
            var cell = Flex();
            cell.AddToClassList("cf-zone");
            cell.style.marginRight = 12;
            var heading = Label(label.ToUpperInvariant() + " " + cards.Length, 10, Soft);
            heading.style.marginBottom = 4;
            cell.Add(heading);
            var list = Flex(true);
            list.style.alignItems = Align.Center;
            cell.Add(list);
            if (concealed)
            {
                if (cards.Length > 0)
                {
                    list.Add(MakeCommonBack(cardWidth));
                    list.Add(Label("x" + cards.Length, 12, CommandTone));
                }
            }
            else if (pile)
            {
                if (cards.Length > 0)
                {
                    var latest = cards[cards.Length - 1];
                    list.Add(!string.IsNullOrEmpty(latest.cardId)
                        ? MakeCardFace(DefinitionFor(latest), cardWidth,
                            () => PickArenaCard(latest), selected?.instanceId == latest.instanceId)
                        : MakeCommonBack(cardWidth));
                }
                else
                {
                    var emptyPile = Label("EMPTY", 9, Soft);
                    emptyPile.style.height = 22;
                    emptyPile.style.width = Mathf.Max(45, cardWidth);
                    list.Add(emptyPile);
                }
            }
            else
            {
                foreach (var card in cards)
                {
                    var current = card;
                    var known = !string.IsNullOrWhiteSpace(card.cardId);
                    list.Add(known
                        ? MakeCardFace(DefinitionFor(card), cardWidth, () => PickArenaCard(current),
                            selected?.instanceId == current.instanceId)
                        : MakeCommonBack(cardWidth));
                }
                if (cards.Length == 0)
                {
                    var empty = Label("EMPTY", 9, ColorFrom("#60738B"));
                    empty.style.width = Mathf.Max(65, cardWidth);
                    empty.style.height = 24;
                    empty.style.unityTextAlign = TextAnchor.MiddleCenter;
                    Frame(empty, ArenaTrim);
                    list.Add(empty);
                }
            }
            row.Add(cell);
        }

        private void ArenaSeat(VisualElement parent, string player, string title, bool opponent, int seatCount)
        {
            var panel = Flex(true);
            panel.AddToClassList("cf-seat");
            panel.style.flexGrow = 1;
            panel.style.flexShrink = 1;
            panel.style.minHeight = seatCount > 2 ? 99 : 149;
            panel.style.alignItems = Align.Center;
            panel.style.marginBottom = 4;
            panel.style.backgroundColor = opponent ? ColorFrom("#1D2537") : ColorFrom("#172D32");
            Frame(panel, opponent ? ArenaTrim : CreatureTone);
            Round(panel, 8);
            var stats = Flex();
            stats.style.width = 148;
            stats.style.flexShrink = 0;
            stats.style.paddingLeft = 10;
            stats.style.paddingRight = 8;
            stats.Add(Label(title, 14, opponent ? QuickTone : CreatureTone));
            foreach (var resource in (match.resources ?? Array.Empty<ClientGameResourceDto>()).Where(r => r.playerId == player))
            {
                var isHealth = resource.resourceId == "life";
                var isMana = resource.resourceId == "energy";
                var status = Label(resource.resourceId.ToUpperInvariant() + "  " + resource.value,
                    isHealth ? 18 : isMana ? 13 : 10, isHealth ? Pale : isMana ? ManaTone : Soft);
                if (isHealth) status.AddToClassList("cf-heading");
                stats.Add(status);
            }
            if (targetAction != null && targetAction.target == "target_player")
                stats.Add(Action("TARGET", () => Submit(targetAction, selected?.instanceId, null, player)));
            panel.Add(stats);
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
            panel.Add(lanes);
            parent.Add(panel);
        }

        private void ShowArenaMatch()
        {
            content.Clear();
            var titleBar = Flex(true);
            titleBar.style.height = 43;
            titleBar.style.flexShrink = 0;
            titleBar.style.alignItems = Align.Center;
            titleBar.Add(Label("ASTRAL RIFT", 22, CommandTone));
            var stateText = match == null ? "CONNECTING TO ARENA..." :
                "  |  TURN " + match.turn + "  |  " + match.currentPhaseId +
                (match.activePlayerId == session.User?.id ? "  |  YOUR TURN" : "  |  OPPONENT TURN");
            var state = Label(stateText, 13, Pale);
            state.style.flexGrow = 1;
            state.style.marginLeft = 14;
            titleBar.Add(state);
            titleBar.Add(Action("EXIT MATCH", () =>
            {
                matchId = null; match = null; selected = null; targetAction = null;
                if (polling != null) StopCoroutine(polling);
                Show("Play");
            }));
            content.Add(titleBar);
            if (match == null) { content.Add(Label("Waiting for server game state...", 15)); return; }
            if (match.finished)
            {
                content.Add(Label(match.winnerPlayerId == session.User?.id ? "VICTORY" : "DEFEAT", 45, CommandTone));
                return;
            }

            var layout = Flex(true);
            layout.style.flexGrow = 1;
            layout.style.minHeight = 0;
            content.Add(layout);
            var field = Flex();
            field.style.flexGrow = 1;
            field.style.flexShrink = 1;
            field.style.minWidth = 0;
            field.style.marginRight = 10;
            layout.Add(field);
            var opponents = (match.players ?? Array.Empty<string>())
                .Where(id => id != session.User?.id).ToArray();
            for (var i = 0; i < opponents.Length; i++)
                ArenaSeat(field, opponents[i], "OPPONENT " + (i + 1), true, opponents.Length);
            var centerLine = Flex(true);
            centerLine.style.height = opponents.Length >= 3 ? 19 : 26;
            centerLine.style.flexShrink = 0;
            centerLine.style.alignItems = Align.Center;
            centerLine.style.justifyContent = Justify.Center;
            centerLine.style.backgroundColor = ColorFrom("#26233B");
            centerLine.Add(Label("BATTLEFIELD  •  SELECT CREATURES TO ATTACK  •  REACT WITH QUICKSPELLS", 10, SpellTone));
            field.Add(centerLine);
            ArenaSeat(field, session.User?.id, "YOU", false, opponents.Length);
            var handLane = Flex();
            handLane.style.flexGrow = 0;
            handLane.style.flexShrink = 0;
            handLane.style.paddingTop = 6;
            handLane.style.paddingLeft = 8;
            handLane.style.paddingBottom = 5;
            handLane.style.backgroundColor = ColorFrom("#101B2C");
            Frame(handLane, ArenaTrim);
            Round(handLane, 8);
            var ownedHand = (match.zones ?? Array.Empty<ClientGameZoneDto>())
                .FirstOrDefault(z => z.playerId == session.User?.id && z.zoneId == "hand")?.cards
                ?? Array.Empty<CardInstanceDto>();
            handLane.Add(Label("YOUR HAND  •  " + ownedHand.Length + " CARDS  •  CLICK TO INSPECT / PLAY", 10, CommandTone));
            var hand = Flex(true);
            hand.style.alignItems = Align.Center;
            var handWidth = opponents.Length >= 3 ? 73 : 88;
            foreach (var card in ownedHand)
            {
                var local = card;
                hand.Add(string.IsNullOrWhiteSpace(card.cardId) ? MakeCommonBack(handWidth) :
                    MakeCardFace(DefinitionFor(card), handWidth, () => PickArenaCard(local),
                        selected?.instanceId == local.instanceId));
            }
            handLane.Add(hand);
            field.Add(handLane);

            var inspector = Flex();
            inspector.style.width = opponents.Length >= 3 ? 238 : 278;
            inspector.style.flexShrink = 0;
            inspector.style.backgroundColor = ArenaPanel;
            inspector.style.paddingTop = 12;
            inspector.style.paddingLeft = 10;
            inspector.style.paddingRight = 10;
            inspector.style.paddingBottom = 10;
            Frame(inspector, ArenaTrim);
            Round(inspector);
            layout.Add(inspector);
            inspector.Add(Label("CARD INSPECTOR", 15, CommandTone));
            inspector.Add(Label("All visible zones are public. Opponent hands and decks remain concealed.", 10, Soft));
            if (selected != null)
            {
                var def = DefinitionFor(selected);
                if (def != null)
                {
                    inspector.Add(MakeCardFace(def, opponents.Length >= 3 ? 206 : 244, null, true));
                    var ruleText = new ScrollView(ScrollViewMode.Vertical);
                    ruleText.style.flexGrow = 1;
                    ruleText.style.minHeight = 0;
                    ruleText.Add(Label(def.name + "\n" + (def.text ?? ""), 12, Pale));
                    inspector.Add(ruleText);
                }
                else inspector.Add(Label("Card definition unavailable.", 12, Soft));
            }
            else
            {
                var hint = Label("SELECT A FACE-UP CARD\n\nInspect creatures, commanders, spells and mana sources here. Your legal actions appear below the battlefield.", 14, Soft);
                hint.style.marginTop = 35;
                inspector.Add(hint);
                inspector.Add(Label("Creature  •  Power / toughness\nSpell  •  Main phase\nQuickspell  •  Fast response\nMana source  •  Passive levels\nCommander  •  Command zone", 11, Pale));
            }

            actionBar = Flex(true);
            actionBar.style.flexWrap = Wrap.Wrap;
            actionBar.style.minHeight = 52;
            actionBar.style.maxHeight = 103;
            actionBar.style.flexShrink = 0;
            actionBar.style.alignItems = Align.Center;
            actionBar.style.backgroundColor = ColorFrom("#23344B");
            actionBar.style.paddingTop = 5;
            actionBar.style.paddingLeft = 10;
            content.Add(actionBar);
            DrawActions();
            var recent = match.events?.LastOrDefault();
            if (recent != null)
            {
                var eventLine = Label("MATCH LOG  •  " + recent.message, 11, Soft);
                eventLine.style.height = 18;
                eventLine.style.flexShrink = 0;
                content.Add(eventLine);
            }
        }
    }
}
