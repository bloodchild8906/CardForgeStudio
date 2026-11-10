using System;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace CardForge.Editor
{
    public sealed partial class CardForgeDesignerView
    {
        string _flowId, _flowNodeId, _actionId, _abilityId;
        CardForgeWorkflowCanvas _flowCanvas;

        TurnFlowDocument Flow => _flowId != null && _store.TurnFlows.TryGetValue(_flowId, out var flow) ? flow : null;
        ActionDocument Action => _actionId != null && _store.Actions.TryGetValue(_actionId, out var action) ? action : null;
        AbilityDocument Ability => _abilityId != null && _store.Abilities.TryGetValue(_abilityId, out var ability) ? ability : null;
        TriggerDocument Trigger => _triggerId != null && _store.Triggers.TryGetValue(_triggerId, out var trigger) ? trigger : null;
        System.Collections.Generic.IEnumerable<System.Collections.Generic.KeyValuePair<string,string>> AllPhaseOptions()
        {
            return _store.TurnFlows.Values.OrderBy(f => f.name)
                .SelectMany(f => (f.nodes ?? new System.Collections.Generic.List<TurnFlowNode>())
                    .Select(n => new System.Collections.Generic.KeyValuePair<string,string>(n.id, f.name + " / " + n.name)));
        }

        void RenderTriggers()
        {
            if (!_triggerEditorOpen) { RenderTriggerIndex(); return; }
            RenderDocumentList("TRIGGER SYSTEM", "Triggers", _store.Triggers.Values.Select(x => new[] { x.id, x.name, x.eventType }),
                () => { var trigger = new TriggerDocument(); _store.SaveTrigger(trigger); _triggerId = trigger.id; Render(); },
                id => { _triggerId = id; Render(); }, Trigger == null ? "Select a trigger" : Trigger.name, RenderTriggerInspector);
        }

        void RenderTriggerIndex()
        {
            var left = Pane("sidebar", 258); _main.Add(left); var list = Scroll(left);
            list.Add(Kicker("TRIGGER SYSTEM")); list.Add(Title("Triggers"));
            list.Add(Note("Define state conditions that open a response window and restrict which actions may execute."));
            list.Add(Btn("+ Create trigger", () => { var trigger = new TriggerDocument(); _store.SaveTrigger(trigger); _triggerId = trigger.id; _triggerEditorOpen = true; Render(); }, "primary-button"));
            foreach (var trigger in _store.Triggers.Values.OrderBy(x => x.name)) AddListButton(list, trigger.name, trigger.id == _triggerId, () => { _triggerId = trigger.id; _triggerEditorOpen = true; Render(); }, trigger.eventType);
            var center = Pane("center-pane"); _main.Add(center); center.Add(Title("Trigger definitions")); center.Add(Note("Open a trigger to author its condition and response actions.")); _main.Add(Pane("inspector", 330));
        }

        void RenderTriggerInspector(VisualElement pane)
        {
            pane.Clear(); var scroll = Scroll(pane); if (Trigger == null) { scroll.Add(Note("Select a trigger to define its condition.")); return; }
            scroll.Add(Title("Trigger definition"));
            RenderValidation(scroll, "Trigger", _store.ValidateTrigger(Trigger));
            TextBox(scroll, "Name", Trigger.name, v => { Trigger.name = v; SaveTrigger(); });
            Choice(scroll, "Event type", new[] { "State changed", "Stat changed", "Card cast", "Card moved", "Card oriented", "Card defeated", "Card entered zone", "Card left zone", "Phase changed", "Turn started", "Turn ended", "Priority passed", "Custom event" }.Select(x => new System.Collections.Generic.KeyValuePair<string,string>(x, x)), Trigger.eventType, v => { Trigger.eventType = v; SaveTrigger(); Render(); });
            TextBox(scroll, "Event source", Trigger.source, v => { Trigger.source = v; SaveTrigger(); });
            TextBox(scroll, "From zone", Trigger.fromZone, v => { Trigger.fromZone = v; SaveTrigger(); });
            TextBox(scroll, "To zone", Trigger.toZone, v => { Trigger.toZone = v; SaveTrigger(); });
            Choice(scroll, "Orientation", new[] { new System.Collections.Generic.KeyValuePair<string,string>("", "Any orientation") }.Concat(new[] { "Face up", "Face down", "Rotated 90", "Rotated 180", "Rotated 270" }.Select(x => new System.Collections.Generic.KeyValuePair<string,string>(x, x))), Trigger.orientation, v => { Trigger.orientation = v; SaveTrigger(); });
            var rules = Group("TRIGGER RULES"); scroll.Add(rules); rules.Add(Note("All rules must pass after the selected event occurs."));
            if (Trigger.rules == null) Trigger.rules = new System.Collections.Generic.List<TriggerRule>();
            foreach (var rule in Trigger.rules.ToList())
            {
                var local = rule; var editor = new Foldout { text = (local.subject ?? "State") + "." + (local.property ?? "property"), value = false }; rules.Add(editor);
                TextBox(editor, "Subject", local.subject, v => { local.subject = v; SaveTrigger(); Render(); });
                TextBox(editor, "Property", local.property, v => { local.property = v; SaveTrigger(); Render(); });
                Choice(editor, "Comparison", new[] { "Equals", "Not equals", "Contains", "Less than", "Less than or equal", "Greater than", "Greater than or equal" }.Select(x => new System.Collections.Generic.KeyValuePair<string,string>(x, x)), local.comparison, v => { local.comparison = v; SaveTrigger(); });
                TextBox(editor, "Expected value", local.expectedValue, v => { local.expectedValue = v; SaveTrigger(); });
                TextBox(editor, "Rule condition", local.condition, v => { local.condition = v; SaveTrigger(); }, true);
                editor.Add(Btn("Remove rule", () => { Trigger.rules.Remove(local); SaveTrigger(); Render(); }, "small-danger"));
            }
            rules.Add(Btn("+ Add trigger rule", () => { Trigger.rules.Add(new TriggerRule()); SaveTrigger(); Render(); }, "small-button"));
            TextBox(scroll, "Additional condition", Trigger.condition, v => { Trigger.condition = v; SaveTrigger(); }, true);
            TextBox(scroll, "Response window", Trigger.responseWindow, v => { Trigger.responseWindow = v; SaveTrigger(); });
            IntBox(scroll, "Priority", Trigger.priority, v => { Trigger.priority = v; SaveTrigger(); });
            var stack = Group("RESPONSE STACK RULES"); scroll.Add(stack);
            stack.Add(Note("Every player may respond to this trigger. Allowed response actions are added to a last-in-first-out stack. The next item resolves only after every player passes; after each resolution, all players may respond again."));
            stack.Add(new Label("ALL PLAYERS MAY RESPOND · LIFO · ALL PLAYERS MUST PASS") { name = "success-text" });
            var sources = Group("EVENT SOURCES"); scroll.Add(sources); sources.Add(Note("Choose actions that are known to produce this event. Leave empty for any matching game-state change."));
            if (Trigger.causeActionIds == null) Trigger.causeActionIds = new System.Collections.Generic.List<string>();
            foreach (var id in Trigger.causeActionIds.ToList())
            {
                var local = id; var row = Row("requirement-row"); sources.Add(row); row.Add(new Label(_store.Actions.TryGetValue(local, out var action) ? action.name : "Missing action"));
                row.Add(Btn("Remove", () => { Trigger.causeActionIds.Remove(local); SaveTrigger(); Render(); }, "small-danger"));
            }
            var sourceOptions = _store.Actions.Values.Where(action => !Trigger.causeActionIds.Contains(action.id));
            if (sourceOptions.Any()) Choice(sources, "Add source action", new[] { new System.Collections.Generic.KeyValuePair<string,string>("", "Select action") }.Concat(Options(sourceOptions, x => x.id, x => x.name)), "", id => { if (string.IsNullOrEmpty(id)) return; Trigger.causeActionIds.Add(id); SaveTrigger(); Render(); });
            if (Trigger.causeAbilityIds == null) Trigger.causeAbilityIds = new System.Collections.Generic.List<string>();
            foreach (var id in Trigger.causeAbilityIds.ToList())
            {
                var local = id; var row = Row("requirement-row"); sources.Add(row); row.Add(new Label(_store.Abilities.TryGetValue(local, out var ability) ? ability.name : "Missing ability"));
                row.Add(Btn("Remove", () => { Trigger.causeAbilityIds.Remove(local); SaveTrigger(); Render(); }, "small-danger"));
            }
            var abilitySourceOptions = _store.Abilities.Values.Where(ability => !Trigger.causeAbilityIds.Contains(ability.id));
            if (abilitySourceOptions.Any()) Choice(sources, "Add source ability", new[] { new System.Collections.Generic.KeyValuePair<string,string>("", "Select ability") }.Concat(Options(abilitySourceOptions, x => x.id, x => x.name)), "", id => { if (string.IsNullOrEmpty(id)) return; Trigger.causeAbilityIds.Add(id); SaveTrigger(); Render(); });
            var responses = Group("ALLOWED RESPONSE ACTIONS"); scroll.Add(responses); responses.Add(Note("Only these actions may be called while this trigger is resolving."));
            foreach (var id in (Trigger.responseActionIds ?? new System.Collections.Generic.List<string>()).ToList())
            {
                var local = id; var row = Row("requirement-row"); responses.Add(row); row.Add(new Label(_store.Actions.TryGetValue(local, out var action) ? action.name : "Missing action"));
                row.Add(Btn("Remove", () => { Trigger.responseActionIds.Remove(local); SaveTrigger(); Render(); }, "small-danger"));
            }
            var options = _store.Actions.Values.Where(x => !(Trigger.responseActionIds ?? new System.Collections.Generic.List<string>()).Contains(x.id));
            Choice(responses, "Add response action", new[] { new System.Collections.Generic.KeyValuePair<string,string>("", "Select action") }.Concat(Options(options, x => x.id, x => x.name)), "", id => { if (string.IsNullOrEmpty(id)) return; Trigger.responseActionIds.Add(id); SaveTrigger(); Render(); });
            if (!embedded) scroll.Add(Btn("Delete trigger", () => { _store.DeleteTrigger(Trigger.id); _triggerId = null; _triggerEditorOpen = false; Render(); }, "danger-button"));
        }

        void SaveTrigger() { if (Trigger != null) _store.SaveTrigger(Trigger); TrackHistory(); _notification = "Trigger saved"; }

        void RenderTurnFlow()
        {
            if (!embedded) {
            var left = Pane("sidebar", 238); _main.Add(left);
            var list = Scroll(left);
            list.Add(Kicker("TURN SYSTEM")); list.Add(Title("Turn flows"));
            list.Add(Note("Build phases, checks, and the schedules that make actions available."));
            list.Add(Btn("+ New turn flow", () => { var f = new TurnFlowDocument { name = "New turn flow" }; _store.SaveTurnFlow(f); _flowId = f.id; _flowNodeId = null; Render(); }, "primary-button"));
            foreach (var flow in _store.TurnFlows.Values.OrderBy(x => x.name))
                AddListButton(list, flow.name, flow.id == _flowId, () => { _flowId = flow.id; _flowNodeId = null; Render(); }, flow.nodes.Count + " phases / " + flow.nodes.Sum(node => (node.actionRules ?? new System.Collections.Generic.List<PhaseActionRule>()).Count) + " schedules");

            }
            var center = Pane("center-pane"); _main.Add(center);
            if (Flow == null) { center.Add(Title("Select a turn flow")); _main.Add(Pane("inspector", 330)); return; }
            if (!embedded) {
            var bar = Row("content-bar"); center.Add(bar); bar.Add(Title(Flow.name)); bar.Add(Flex());
            bar.Add(Btn("+ Phase", AddFlowNode, "primary-button"));
            bar.Add(Btn("Validate", () => { _notification = ValidateFlow(); Render(); }, "small-button"));
            bar.Add(Btn("Auto arrange", AutoArrangeFlow, "small-button"));
            }
            var canvasWrap = Col("workflow-wrap"); center.Add(canvasWrap);
            canvasWrap.Add(Note("Drag phases to position them. Drag from a phase's right dot onto another phase to connect them; click a line and press Delete to remove it."));
            _flowCanvas = new CardForgeWorkflowCanvas {
                ActionLabel = id => _store.Actions.TryGetValue(id, out var action) ? action.name : "Missing action",
                ActionRuleLabel = rule => {
                    var name = _store.Actions.TryGetValue(rule.actionId, out var action) ? action.name : "Missing action";
                    var before = rule.beforeTriggerIds == null ? 0 : rule.beforeTriggerIds.Count;
                    var after = rule.afterTriggerIds == null ? 0 : rule.afterTriggerIds.Count;
                    var triggers = before == 0 && after == 0 ? "" : " · " + before + " before / " + after + " after";
                    return name + " / " + (string.IsNullOrWhiteSpace(rule.availability) ? "unscheduled" : rule.availability) + triggers;
                }
            }; canvasWrap.Add(_flowCanvas); WireFlowCanvas(_flowCanvas);
            _flowCanvas.Draw(Flow, _flowNodeId, node => { _flowNodeId = node.id; RenderFlowInspector(_inspector); }, node => { SaveFlow(); });
            _inspector = Pane("inspector", 330); _main.Add(_inspector); RenderFlowInspector(_inspector);
        }

        void AutoArrangeFlow()
        {
            if (Flow == null) return;
            var links = Flow.links.Select(link => new System.Collections.Generic.KeyValuePair<string, string>(link.from, link.to)).ToList();
            var placements = CardForge.DesignLogic.GraphLayout.Arrange(Flow.nodes.Select(node => node.id).ToList(), links, new CardForge.DesignLogic.GraphLayoutSettings { NodeHeight = 170f });
            foreach (var placement in placements)
            {
                var node = Flow.nodes.First(candidate => candidate.id == placement.Id);
                node.x = placement.X; node.y = placement.Y;
            }
            SaveFlow(); RefreshFlowCanvas(); _flowCanvas?.FitToContent();
        }

        void RefreshFlowCanvas()
        {
            if (_flowCanvas != null && Flow != null)
                _flowCanvas.Draw(Flow, _flowNodeId, node => { _flowNodeId = node.id; RenderFlowInspector(_inspector); }, node => SaveFlow());
        }
        void SaveFlow() { if (Flow != null) _store.SaveTurnFlow(Flow); TrackHistory(); _notification = "Turn flow saved"; }
        void AddFlowNode()
        {
            if (Flow == null) return;
            var node = new TurnFlowNode { name = "New phase", phase = "Main", x = 70 + (Flow.nodes.Count % 3) * 260, y = 80 + (Flow.nodes.Count / 3) * 185 };
            if (Flow.nodes.Count > 0) Flow.links.Add(new TurnFlowLink { from = Flow.nodes[Flow.nodes.Count - 1].id, to = node.id, label = "continue" });
            Flow.nodes.Add(node); _flowNodeId = node.id; SaveFlow(); Render();
        }
        string ValidateFlow()
        {
            if (Flow == null || Flow.nodes.Count == 0) return "Flow needs at least one phase";
            if (Flow.nodes.Any(x => string.IsNullOrWhiteSpace(x.name))) return "A phase is missing a name";
            var issues = _store.ValidateFlow(Flow);
            return issues.Count == 0 ? "Turn flow valid" : string.Join("\n", issues);
        }
        void RenderFlowInspector(VisualElement pane)
        {
            pane.Clear(); var scroll = Scroll(pane);
            if (Flow == null) { scroll.Add(Note("Select a flow to edit its structure.")); return; }
            var settings = new Foldout { text = "Flow settings", value = _flowNodeId == null }; scroll.Add(settings);
            TextBox(settings, "Flow name", Flow.name, v => { Flow.name = v; SaveFlow(); });
            TextBox(settings, "Description", Flow.description, v => { Flow.description = v; SaveFlow(); }, true);
            var node = Flow.nodes.FirstOrDefault(x => x.id == _flowNodeId);
            if (node == null) { scroll.Add(Title("Phase structure")); scroll.Add(Note("Select a phase card to edit its checks.")); return; }
            scroll.Add(Title("Selected phase"));
            TextBox(scroll, "Phase name", node.name, v => { node.name = v; SaveFlow(); RefreshFlowCanvas(); });
            TextBox(scroll, "Phase label", node.phase, v => { node.phase = v; SaveFlow(); RefreshFlowCanvas(); });
            TextBox(scroll, "Base check", node.check, v => { node.check = v; SaveFlow(); RefreshFlowCanvas(); }, true);
            TextBox(scroll, "Resolution order", node.resolution, v => { node.resolution = v; SaveFlow(); RefreshFlowCanvas(); });
            ToggleBox(scroll, "Optional phase", node.optional, v => { node.optional = v; SaveFlow(); RefreshFlowCanvas(); });
            RenderPhaseActions(scroll, node);
            string destination = Flow.nodes.FirstOrDefault(n=>n.id!=node.id)?.id;
            Choice(scroll, "Connect to", Options(Flow.nodes.Where(n=>n.id!=node.id),n=>n.id,n=>n.name),destination,v=>destination=v);
            scroll.Add(Btn("Add connection", () => { if(destination!=null && !Flow.links.Any(l=>l.from==node.id&&l.to==destination)){Flow.links.Add(new TurnFlowLink{from=node.id,to=destination});SaveFlow();Render();} }, "ghost-button"));
            foreach(var edge in Flow.links.Where(l=>l.from==node.id).ToList()) {
                var local=edge;var g=Group("TO "+(Flow.nodes.FirstOrDefault(n=>n.id==local.to)?.name ?? "Missing phase"));scroll.Add(g);
                TextBox(g,"Label",local.label,v=>{local.label=v;SaveFlow();RefreshFlowCanvas();});
                g.Add(Btn("Remove connection",()=>{Flow.links.Remove(local);SaveFlow();Render();},"small-danger"));
            }
            scroll.Add(Btn("Duplicate phase",()=>{var copy=CardForgeData.Copy(node);copy.id=CardForgeData.NewId();copy.name+=" copy";copy.x+=30;copy.y+=160;Flow.nodes.Add(copy);_flowNodeId=copy.id;SaveFlow();Render();},"ghost-button"));
            if (!embedded) {
            scroll.Add(Btn("Connect to next phase", () =>
            {
                var next = Flow.nodes.SkipWhile(x => x.id != node.id).Skip(1).FirstOrDefault();
                if (next != null && !Flow.links.Any(x => x.from == node.id && x.to == next.id)) Flow.links.Add(new TurnFlowLink { from = node.id, to = next.id });
                SaveFlow(); Render();
            }, "ghost-button"));
            }
            scroll.Add(Btn("Remove phase", () => { if(_store.Abilities.Values.Any(a=>a.phaseId==node.id) || node.actionRules.Any(rule=>rule!=null&& !string.IsNullOrEmpty(rule.actionId))){UnityEditor.EditorUtility.DisplayDialog("Phase in use","Unlink actions and abilities from this phase before deleting it.","OK");return;} Flow.nodes.Remove(node); Flow.links.RemoveAll(x => x.from == node.id || x.to == node.id); _flowNodeId = null; SaveFlow(); Render(); }, "danger-button"));
        }

        void RenderPhaseActions(VisualElement parent, TurnFlowNode node)
        {
            NormalizePhaseActions(node);
            var group = Group("ACTIONS AVAILABLE IN THIS PHASE"); parent.Add(group);
            group.Add(Note(node.actionRules.Count + " action" + (node.actionRules.Count == 1 ? "" : "s") + " can occur here. Actions define what happens; this phase defines when they are available and how they resolve."));
            foreach (var schedule in node.actionRules.ToList())
            {
                var local = schedule;
                var actionName = _store.Actions.TryGetValue(local.actionId, out var action) ? action.name : "Missing action";
                var editor = new Foldout { text = "Action · " + actionName, value = false }; group.Add(editor);
                var options = new[] { new System.Collections.Generic.KeyValuePair<string,string>("", "Select action") }
                    .Concat(Options(_store.Actions.Values.Where(candidate => candidate.id == local.actionId || !node.actionRules.Any(rule => rule != local && rule.actionId == candidate.id)), x => x.id, x => x.name));
                Choice(editor, "Action", options, local.actionId, id => {
                    if (string.IsNullOrEmpty(id) || node.actionRules.Any(rule => rule != local && rule.actionId == id)) return;
                    local.actionId = id; SaveFlow(); Render();
                });
                RenderPhaseTriggerList(editor, "TRIGGERS BEFORE ACTION", "These triggers resolve before the action becomes available.", local.beforeTriggerIds);
                TextBox(editor, "When it can occur", local.availability, value => { local.availability = value; SaveFlow(); RefreshFlowCanvas(); });
                TextBox(editor, "Phase condition", local.condition, value => { local.condition = value; SaveFlow(); });
                IntBox(editor, "Priority", local.priority, value => { local.priority = value; SaveFlow(); });
                TextBox(editor, "Resolution order", local.resolutionOrder, value => { local.resolutionOrder = value; SaveFlow(); });
                ToggleBox(editor, "Optional action", local.optional, value => { local.optional = value; SaveFlow(); });
                RenderPhaseTriggerList(editor, "TRIGGERS AFTER ACTION", "These triggers resolve after the action completes.", local.afterTriggerIds);
                editor.Add(Btn("Remove schedule", () => { node.actionRules.Remove(local); SaveFlow(); Render(); }, "small-danger"));
            }
            var available = _store.Actions.Values.Where(action => !node.actionRules.Any(rule => rule.actionId == action.id));
            if (!available.Any()) group.Add(Note("All actions are already scheduled in this phase."));
            else Choice(group, "Add action to phase", new[] { new System.Collections.Generic.KeyValuePair<string,string>("", "Select action") }.Concat(Options(available, action => action.id, action => action.name)), "", id => {
                if (string.IsNullOrEmpty(id)) return;
                node.actionRules.Add(new PhaseActionRule { actionId = id }); SaveFlow(); Render();
            });
        }

        void RenderPhaseTriggerList(VisualElement parent, string title, string note, System.Collections.Generic.List<string> triggerIds)
        {
            if (triggerIds == null) return;
            var group = Group(title); parent.Add(group);
            group.Add(Note(note));
            foreach (var id in triggerIds.ToList())
            {
                var local = id; var row = Row("requirement-row"); group.Add(row);
                row.Add(new Label(_store.Triggers.TryGetValue(local, out var trigger) ? trigger.name : "Missing trigger"));
                row.Add(Btn("Remove", () => { triggerIds.Remove(local); SaveFlow(); Render(); }, "small-danger"));
            }
            var options = _store.Triggers.Values.Where(trigger => !triggerIds.Contains(trigger.id));
            Choice(group, "Add trigger", new[] { new System.Collections.Generic.KeyValuePair<string,string>("", "Select trigger") }.Concat(Options(options, x => x.id, x => x.name)), "", id => {
                if (string.IsNullOrEmpty(id)) return;
                triggerIds.Add(id); SaveFlow(); Render();
            });
        }

        void NormalizePhaseActions(TurnFlowNode node)
        {
            if (node.actionRules == null) node.actionRules = new System.Collections.Generic.List<PhaseActionRule>();
            node.actionRules.RemoveAll(rule => rule == null || string.IsNullOrEmpty(rule.actionId));
        }

        void RenderActions()
        {
            if (!_actionEditorOpen) { RenderActionIndex(); return; }
            RenderDocumentList("ACTIONS", "Actions", _store.Actions.Values.Select(x => new[] { x.id, x.name, (x.graphNodes ?? new System.Collections.Generic.List<ResolutionGraphNode>()).Count + " graph steps" }),
                () => { var a = new ActionDocument(); _store.SaveAction(a); _actionId = a.id; Render(); },
                id => { _actionId = id; Render(); }, Action == null ? "Select an action" : Action.name, RenderActionInspector);
        }
        void RenderAbilities()
        {
            if (_keywordEditorOpen) { RenderKeywordEditor(); return; }
            if (!_abilityEditorOpen) { RenderAbilityIndex(); return; }
            RenderDocumentList("ABILITY SYSTEM", "Abilities", _store.Abilities.Values.Select(x => new[] { x.id, x.name, string.IsNullOrEmpty(x.cardId) ? "Reusable ability" : "Card-bound ability" }),
                () => { var a = new AbilityDocument(); _store.SaveAbility(a); _abilityId = a.id; Render(); },
                id => { _abilityId = id; Render(); }, Ability == null ? "Select an ability" : Ability.name, RenderAbilityInspector);
        }
        void RenderDocumentList(string kicker, string title, System.Collections.Generic.IEnumerable<string[]> rows, Action create, Action<string> select, string emptyTitle, Action<VisualElement> inspector)
        {
            if (!embedded) {
            var left = Pane("sidebar", 258); _main.Add(left); var list = Scroll(left);
            list.Add(Kicker(kicker)); list.Add(Title(title)); list.Add(Note("Define reusable rules once, then link them into the turn system.")); list.Add(Btn("+ Create", create, "primary-button"));
            foreach (var row in rows) AddListButton(list, row[1], (title == "Actions" ? _actionId : title == "Abilities" ? _abilityId : _triggerId) == row[0], () => select(row[0]), row[2]);
            }
            var center = Pane("center-pane"); _main.Add(center);
            if (!embedded) {
            var bar = Row("content-bar"); center.Add(bar); bar.Add(Title(emptyTitle)); bar.Add(Flex());
            bar.Insert(1, CardForgeUi.Back("Library", () =>
            {
                if (title == "Actions") _actionEditorOpen = false;
                else if (title == "Abilities") _abilityEditorOpen = false;
                else _triggerEditorOpen = false;
                Render();
            }));
            }
            if (title == "Actions" && Action != null) RenderActionGraphDesigner(center, Action);
            if (title == "Abilities" && Ability != null) RenderAbilityGraphDesigner(center, Ability);
            _inspector = Pane("inspector", 330); _main.Add(_inspector); inspector(_inspector);
        }
        void RenderActionGraphDesigner(VisualElement center, ActionDocument action)
        {
            EnsureActionGraph(action);
            var body = Col("workflow-wrap"); center.Add(body);
            body.Add(Title("Action resolution graph"));
            body.Add(Note("Define the action's checks, costs, targets, orientation changes, effects, and completion. Only steps marked as state changes or trigger emissions interact with the rules engine."));
            var canvas = new CardForgeResolutionGraphCanvas { ViewKey = "action:" + action.id }; body.Add(canvas);
            ShowResolutionGraph(canvas, action.graphNodes, action.graphLinks, SaveAction, () => RenderActionInspector(_inspector));
        }

        void RenderAbilityGraphDesigner(VisualElement center, AbilityDocument ability)
        {
            EnsureAbilityGraph(ability);
            var body = Col("workflow-wrap"); center.Add(body);
            body.Add(Title("Ability modification graph"));
            body.Add(Note("Connect keyword intent to conditions, linked action or phase, operation, and effect."));
            var canvas = new CardForgeResolutionGraphCanvas { ViewKey = "ability:" + ability.id }; body.Add(canvas);
            ShowResolutionGraph(canvas, ability.graphNodes, ability.graphLinks, SaveAbility, () => RenderAbilityInspector(_inspector));
        }

        static void EnsureActionGraph(ActionDocument action)
        {
            if (action.graphNodes == null) action.graphNodes = new System.Collections.Generic.List<ResolutionGraphNode>();
            if (action.graphLinks == null) action.graphLinks = new System.Collections.Generic.List<ResolutionGraphLink>();
            if (action.graphNodes != null && action.graphNodes.Count > 0) return;
            string[] titles = { "Viability trigger", "Declare action", "Check condition", "Pay cost", "Choose targets", "Orient card", "Apply effect", "Emit trigger outputs", "Complete" };
            string[] details = { string.IsNullOrEmpty(action.triggerId) ? "No trigger gate" : action.triggerId, action.name, action.condition, action.cost, action.targets, action.orientationChange == CardOrientationChange.None ? "No orientation change" : action.orientationChange + " · " + action.orientationTarget, action.effect, action.causeTriggerIds == null ? "No trigger outputs" : action.causeTriggerIds.Count + " triggers", action.resolution };
            for (int i = 0; i < titles.Length; i++) action.graphNodes.Add(new ResolutionGraphNode { title = titles[i], detail = details[i], tone = i == 0 ? "trigger" : i == 3 || i == 7 ? "effect" : "process", causesStateChange = i == 6, emitsTriggers = i == 7, x = 50 + i * 250, y = 110 });
            for (int i = 1; i < action.graphNodes.Count; i++) action.graphLinks.Add(new ResolutionGraphLink { from = action.graphNodes[i - 1].id, to = action.graphNodes[i].id });
        }

        static void EnsureAbilityGraph(AbilityDocument ability)
        {
            if (ability.graphNodes == null) ability.graphNodes = new System.Collections.Generic.List<ResolutionGraphNode>();
            if (ability.graphLinks == null) ability.graphLinks = new System.Collections.Generic.List<ResolutionGraphLink>();
            if (ability.graphNodes != null && ability.graphNodes.Count > 0) return;
            string[] titles = { "Activation trigger", "Condition", "Target scope", "State/card rule", "Response actions", "Trigger outputs", "Resolution" };
            string[] details = { string.IsNullOrEmpty(ability.activationTriggerId) ? "No trigger gate" : ability.activationTriggerId, ability.condition, ability.scope, ability.rules == null ? "No rules" : ability.rules.Count + " rules", ability.responseActionIds == null ? "No response actions" : ability.responseActionIds.Count + " actions", ability.causeTriggerIds == null ? "No trigger outputs" : ability.causeTriggerIds.Count + " triggers", ability.responseResolution };
            for (int i = 0; i < titles.Length; i++) ability.graphNodes.Add(new ResolutionGraphNode { title = titles[i], detail = details[i], tone = i == 0 ? "trigger" : i == 4 ? "effect" : "process", causesStateChange = i == 3, emitsTriggers = i == 5, x = 50 + i * 250, y = 110 });
            for (int i = 1; i < ability.graphNodes.Count; i++) ability.graphLinks.Add(new ResolutionGraphLink { from = ability.graphNodes[i - 1].id, to = ability.graphNodes[i].id });
        }

        void RenderActionInspector(VisualElement pane)
        {
            pane.Clear(); var scroll = Scroll(pane); if (Action == null) { scroll.Add(Note("Select an action to edit its executable rules.")); return; }
            scroll.Add(Title("Action definition"));
            RenderValidation(scroll, "Action", _store.ValidateAction(Action));
            scroll.Add(Note("Actions are reusable and timing-neutral. Assign them to phase schedules in the Turn Flow designer."));
            TextBox(scroll, "Name", Action.name, v => { Action.name = v; SaveAction(); });
            Choice(scroll, "Viability trigger", new[] { new System.Collections.Generic.KeyValuePair<string,string>("", "No trigger gate") }.Concat(Options(_store.Triggers.Values, x => x.id, x => x.name)), Action.triggerId, v => { Action.triggerId = v; SaveAction(); });
            RenderActionTriggerOutputs(scroll, Action);
            TextBox(scroll, "Action condition", Action.condition, v => { Action.condition = v; SaveAction(); }, true);
            TextBox(scroll, "Cost", Action.cost, v => { Action.cost = v; SaveAction(); });
            TextBox(scroll, "Targets", Action.targets, v => { Action.targets = v; SaveAction(); });
            Choice(scroll, "Card orientation", Enum.GetNames(typeof(CardOrientationChange)).Select(x => new System.Collections.Generic.KeyValuePair<string,string>(x, x)), Action.orientationChange.ToString(), v => { Enum.TryParse(v, out Action.orientationChange); SaveAction(); });
            TextBox(scroll, "Orientation target", Action.orientationTarget, v => { Action.orientationTarget = v; SaveAction(); });
            TextBox(scroll, "Effect", Action.effect, v => { Action.effect = v; SaveAction(); }, true);
            TextBox(scroll, "Resolution order", Action.resolution, v => { Action.resolution = v; SaveAction(); });
            RenderActionGraphControls(scroll, Action);
            if (!embedded) scroll.Add(Btn("Delete action", () => { try { _store.DeleteAction(Action.id); _actionId = null; Render(); } catch(Exception e) { Error("Action is in use",e); } }, "danger-button"));
        }

        void RenderActionGraphControls(VisualElement parent, ActionDocument action)
        {
            EnsureActionGraph(action);
            var group = Group("ACTION GRAPH STEPS"); parent.Add(group);
            group.Add(Note("The graph is the complete executable action. Cost, checks, and targeting can be non-state-changing; mark only real results and trigger boundaries below."));
            group.Add(Btn("+ Add graph step", () => { action.graphNodes.Add(new ResolutionGraphNode { title = "New rule step", x = 100 + action.graphNodes.Count * 30, y = 300 }); SaveAction(); Render(); }, "small-button"));
            foreach (var node in action.graphNodes.ToList())
            {
                var local = node; var editor = new Foldout { text = local.title, value = local.id == _graphNodeId }; group.Add(editor);
                TextBox(editor, "Step", local.title, v => { local.title = v; SaveAction(); Render(); });
                TextBox(editor, "Rule", local.detail, v => { local.detail = v; SaveAction(); });
                Choice(editor, "Tone", new[] { "trigger", "process", "effect" }.Select(x => new System.Collections.Generic.KeyValuePair<string,string>(x, x)), local.tone, v => { local.tone = v; SaveAction(); });
                ToggleBox(editor, "Causes state change", local.causesStateChange, v => { local.causesStateChange = v; SaveAction(); });
                ToggleBox(editor, "Emits triggers", local.emitsTriggers, v => { local.emitsTriggers = v; SaveAction(); });
                editor.Add(Btn("Remove step", () => { action.graphNodes.Remove(local); action.graphLinks.RemoveAll(x => x.from == local.id || x.to == local.id); SaveAction(); Render(); }, "small-danger"));
            }
            RenderGraphConnections(group, action.graphNodes, action.graphLinks, SaveAction);
            if (action.graphNodes.Count > 1)
            {
                var from = action.graphNodes[0].id; var to = action.graphNodes[1].id;
                Choice(group, "Connect from", Options(action.graphNodes, x => x.id, x => x.title), from, v => from = v);
                Choice(group, "Connect to", Options(action.graphNodes, x => x.id, x => x.title), to, v => to = v);
                group.Add(Btn("+ Add graph connection", () => { if (from != to && !action.graphLinks.Any(x => x.from == from && x.to == to)) action.graphLinks.Add(new ResolutionGraphLink { from = from, to = to }); SaveAction(); Render(); }, "ghost-button"));
            }
        }
        void RenderActionTriggerOutputs(VisualElement parent, ActionDocument action)
        {
            if (action.causeTriggerIds == null) action.causeTriggerIds = new System.Collections.Generic.List<string>();
            var group = Group("TRIGGER OUTPUTS"); parent.Add(group);
            group.Add(Note("Only a graph step marked Emits triggers dispatches these result events. Paying a cost, checking a condition, or choosing targets remains a non-state-changing step."));
            foreach (var id in action.causeTriggerIds.ToList())
            {
                var local = id; var row = Row("requirement-row"); group.Add(row);
                row.Add(new Label(_store.Triggers.TryGetValue(local, out var trigger) ? trigger.name : "Missing trigger"));
                row.Add(Btn("Remove", () => { action.causeTriggerIds.Remove(local); SaveAction(); Render(); }, "small-danger"));
            }
            var options = _store.Triggers.Values.Where(x => !action.causeTriggerIds.Contains(x.id));
            Choice(group, "Add trigger output", new[] { new System.Collections.Generic.KeyValuePair<string,string>("", "Select trigger") }.Concat(Options(options, x => x.id, x => x.name)), "", id => { if (string.IsNullOrEmpty(id)) return; action.causeTriggerIds.Add(id); SaveAction(); Render(); });
        }
        void RenderAbilityInspector(VisualElement pane)
        {
            pane.Clear(); var scroll = Scroll(pane); if (Ability == null) { scroll.Add(Note("Select an ability to define its modification.")); return; }
            scroll.Add(Title("Ability definition"));
            RenderValidation(scroll, "Ability", _store.ValidateAbility(Ability));
            TextBox(scroll, "Name", Ability.name, v => { Ability.name = v; SaveAbility(); });
            Choice(scroll, "Card binding", new[] { new System.Collections.Generic.KeyValuePair<string,string>("", "Reusable / no card") }.Concat(Options(_store.Cards.Values, card => card.id, card => card.cardNumber + " · " + card.name)), Ability.cardId, v => { Ability.cardId = v; SaveAbility(); });
            Choice(scroll, "Activation trigger", new[] { new System.Collections.Generic.KeyValuePair<string,string>("", "No trigger gate") }.Concat(Options(_store.Triggers.Values, x => x.id, x => x.name)), Ability.activationTriggerId, v => { Ability.activationTriggerId = v; SaveAbility(); });
            TextBox(scroll, "Response timing", Ability.responseTiming, v => { Ability.responseTiming = v; SaveAbility(); });
            Choice(scroll, "Linked action", new[] { new System.Collections.Generic.KeyValuePair<string,string>("", "No action") }.Concat(Options(_store.Actions.Values, x => x.id, x => x.name)), Ability.actionId, v => { Ability.actionId = v; SaveAbility(); });
            Choice(scroll, "Linked phase", new[] { new System.Collections.Generic.KeyValuePair<string,string>("", "No phase") }.Concat(AllPhaseOptions()), Ability.phaseId, v => { Ability.phaseId = v; SaveAbility(); });
            TextBox(scroll, "Condition", Ability.condition, v => { Ability.condition = v; SaveAbility(); }, true);
            TextBox(scroll, "Operation", Ability.operation, v => { Ability.operation = v; SaveAbility(); });
            TextBox(scroll, "Effect", Ability.effect, v => { Ability.effect = v; SaveAbility(); }, true);
            IntBox(scroll, "Priority", Ability.priority, v => { Ability.priority = v; SaveAbility(); });
            Choice(scroll, "Modification scope", new[] { "Game state", "Cards", "Actions", "Phase" }.Select(x => new System.Collections.Generic.KeyValuePair<string,string>(x, x)), Ability.scope, v => { Ability.scope = v; SaveAbility(); });
            TextBox(scroll, "Response resolution", Ability.responseResolution, v => { Ability.responseResolution = v; SaveAbility(); });
            RenderAbilityResponses(scroll, Ability);
            RenderAbilityTriggerOutputs(scroll, Ability);
            RenderAbilityRules(scroll, Ability);
            RenderAbilityGraphControls(scroll, Ability);
            if (!embedded) scroll.Add(Btn("Delete ability", () => { _store.DeleteAbility(Ability.id); _abilityId = null; Render(); }, "danger-button"));
        }

        void RenderGraphConnections(VisualElement parent,
            System.Collections.Generic.List<ResolutionGraphNode> nodes,
            System.Collections.Generic.List<ResolutionGraphLink> links, System.Action save)
        {
            var group = Group("RESOLUTION CONNECTIONS"); parent.Add(group);
            group.Add(Note("Define the order between graph steps. Links can branch or rejoin."));
            foreach (var edge in links.ToList())
            {
                var link = edge;
                string from = nodes.FirstOrDefault(n => n.id == link.from)?.title ?? "Missing";
                string to = nodes.FirstOrDefault(n => n.id == link.to)?.title ?? "Missing";
                var foldout = new Foldout { text = from + "  →  " + to, value = false };
                group.Add(foldout);
                TextBox(foldout, "Connection label", link.label, v => { link.label = v; save(); });
                foldout.Add(Btn("Remove connection", () => { links.Remove(link); save(); Render(); }, "small-danger"));
            }
        }

        void RenderAbilityGraphControls(VisualElement parent, AbilityDocument ability)
        {
            EnsureAbilityGraph(ability);
            var group = Group("ABILITY GRAPH STEPS"); parent.Add(group);
            group.Add(Note("Author a reusable visual description of how this ability modifies the game."));
            group.Add(Btn("+ Add graph step", () =>
            {
                ability.graphNodes.Add(new ResolutionGraphNode {
                    title = "New ability step", x = 100 + ability.graphNodes.Count * 28, y = 300 });
                SaveAbility(); Render();
            }, "small-button"));
            foreach (var node in ability.graphNodes.ToList())
            {
                var step = node;
                var editor = new Foldout { text = step.title, value = step.id == _graphNodeId }; group.Add(editor);
                TextBox(editor, "Step", step.title, v => { step.title = v; SaveAbility(); });
                TextBox(editor, "Details", step.detail, v => { step.detail = v; SaveAbility(); }, true);
                Choice(editor, "Type", new[] { "trigger", "process", "effect" }
                    .Select(x => new System.Collections.Generic.KeyValuePair<string,string>(x, x)),
                    step.tone, v => { step.tone = v; SaveAbility(); Render(); });
                ToggleBox(editor, "Causes state change", step.causesStateChange, v => { step.causesStateChange = v; SaveAbility(); });
                ToggleBox(editor, "Emits triggers", step.emitsTriggers, v => { step.emitsTriggers = v; SaveAbility(); });
                editor.Add(Btn("Remove step", () => {
                    ability.graphNodes.Remove(step);
                    ability.graphLinks.RemoveAll(l => l.from == step.id || l.to == step.id);
                    SaveAbility(); Render();
                }, "small-danger"));
            }
            RenderGraphConnections(group, ability.graphNodes, ability.graphLinks, SaveAbility);
            if (ability.graphNodes.Count > 1)
            {
                string from = ability.graphNodes[0].id, to = ability.graphNodes[1].id;
                Choice(group, "Connect from", Options(ability.graphNodes, n => n.id, n => n.title),
                    from, id => from = id);
                Choice(group, "Connect to", Options(ability.graphNodes, n => n.id, n => n.title),
                    to, id => to = id);
                group.Add(Btn("+ Add connection", () => {
                    if (from != to && !ability.graphLinks.Any(l => l.from == from && l.to == to))
                    {
                        ability.graphLinks.Add(new ResolutionGraphLink { from = from, to = to });
                        SaveAbility(); Render();
                    }
                }, "ghost-button"));
            }
        }

        void RenderAbilityRules(VisualElement parent, AbilityDocument ability)
        {
            if (ability.rules == null) ability.rules = new System.Collections.Generic.List<AbilityRule>();
            var group = Group("ABILITY RULES"); parent.Add(group);
            group.Add(Note("Define exactly how this ability changes state, cards, actions, or a phase."));
            foreach (var rule in ability.rules.ToList())
            {
                var local = rule; var row = Col("requirement-row"); group.Add(row);
                Choice(row, "Target", new[] { "Game state", "Card", "Action", "Phase" }.Select(x => new System.Collections.Generic.KeyValuePair<string,string>(x, x)), local.target, v => { local.target = v; SaveAbility(); });
                TextBox(row, "Property", local.property, v => { local.property = v; SaveAbility(); });
                Choice(row, "Operation", new[] { "Set", "Add", "Subtract", "Multiply", "Enable", "Disable" }.Select(x => new System.Collections.Generic.KeyValuePair<string,string>(x, x)), local.operation, v => { local.operation = v; SaveAbility(); });
                TextBox(row, "Value", local.value, v => { local.value = v; SaveAbility(); });
                TextBox(row, "Rule condition", local.condition, v => { local.condition = v; SaveAbility(); }, true);
                row.Add(Btn("Remove rule", () => { ability.rules.Remove(local); SaveAbility(); Render(); }, "small-danger"));
            }
            group.Add(Btn("+ Add ability rule", () => { ability.rules.Add(new AbilityRule()); SaveAbility(); Render(); }, "small-button"));
        }

        void RenderAbilityResponses(VisualElement parent, AbilityDocument ability)
        {
            if (ability.responseActionIds == null) ability.responseActionIds = new System.Collections.Generic.List<string>();
            var group = Group("RESPONSES TO ACTIVATION"); parent.Add(group);
            group.Add(Note("These actions are invoked by the ability after its activation trigger and timing checks."));
            foreach (var id in ability.responseActionIds.ToList())
            {
                var local = id; var row = Row("requirement-row"); group.Add(row);
                row.Add(new Label(_store.Actions.TryGetValue(local, out var action) ? action.name : "Missing action"));
                row.Add(Btn("Remove", () => { ability.responseActionIds.Remove(local); SaveAbility(); Render(); }, "small-danger"));
            }
            var options = _store.Actions.Values.Where(x => !ability.responseActionIds.Contains(x.id));
            Choice(group, "Add response action", new[] { new System.Collections.Generic.KeyValuePair<string,string>("", "Select action") }.Concat(Options(options, x => x.id, x => x.name)), "", id => { if (string.IsNullOrEmpty(id)) return; ability.responseActionIds.Add(id); SaveAbility(); Render(); });
        }
        void RenderAbilityTriggerOutputs(VisualElement parent, AbilityDocument ability)
        {
            if (ability.causeTriggerIds == null) ability.causeTriggerIds = new System.Collections.Generic.List<string>();
            var group = Group("TRIGGER OUTPUTS"); parent.Add(group);
            group.Add(Note("Only an ability graph step marked Emits triggers dispatches these events after its rules resolve. Natural events such as phase changes are authored directly in the trigger definition."));
            foreach (var id in ability.causeTriggerIds.ToList())
            {
                var local = id; var row = Row("requirement-row"); group.Add(row);
                row.Add(new Label(_store.Triggers.TryGetValue(local, out var trigger) ? trigger.name : "Missing trigger"));
                row.Add(Btn("Remove", () => { ability.causeTriggerIds.Remove(local); SaveAbility(); Render(); }, "small-danger"));
            }
            var options = _store.Triggers.Values.Where(x => !ability.causeTriggerIds.Contains(x.id));
            Choice(group, "Add trigger output", new[] { new System.Collections.Generic.KeyValuePair<string,string>("", "Select trigger") }.Concat(Options(options, x => x.id, x => x.name)), "", id => { if (string.IsNullOrEmpty(id)) return; ability.causeTriggerIds.Add(id); SaveAbility(); Render(); });
        }
        void RenderValidation(VisualElement parent, string title, System.Collections.Generic.List<string> issues)
        {
            var group = Group("VALIDATION"); parent.Add(group);
            if (issues == null || issues.Count == 0)
            {
                group.Add(new Label(title + " is ready") { name = "success-text" });
                return;
            }
            foreach (var issue in issues) group.Add(new Label("• " + issue) { name = "warning-text" });
        }
        void SaveAction() { if (Action != null) _store.SaveAction(Action); TrackHistory(); _notification = "Action saved"; }
        void SaveAbility() { if (Ability != null) _store.SaveAbility(Ability); TrackHistory(); _notification = "Ability saved"; }
    }
}
