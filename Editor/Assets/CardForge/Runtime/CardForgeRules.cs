using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
namespace CardForge
{
    /// <summary>Shared preview/runtime interpretation of requirements and conditional zone variants.</summary>
    public static class CardForgeRules
    {
        public static bool Matches(IEnumerable<RequirementDefinition> requirements,CardDocument card,SchemaDocument schema)
        {
            foreach(var r in requirements ?? Enumerable.Empty<RequirementDefinition>()) {
                var f=schema?.fields?.FirstOrDefault(x=>x.id==r.fieldId);if(f==null)return false;
                string value=CardForgeData.Value(card,f),expected=r.expectedValue??"";bool match;
                switch(r.comparison) {
                    case "Not equals":match=!string.Equals(value,expected,StringComparison.OrdinalIgnoreCase);break;
                    case "Contains":match=value.IndexOf(expected,StringComparison.OrdinalIgnoreCase)>=0;break;
                    case "Greater than":case "Greater than or equal":case "Less than":case "Less than or equal":
                        match=double.TryParse(value,NumberStyles.Float,CultureInfo.InvariantCulture,out var a)&&double.TryParse(expected,NumberStyles.Float,CultureInfo.InvariantCulture,out var b)
                            &&(r.comparison=="Greater than"?a>b:r.comparison=="Greater than or equal"?a>=b:r.comparison=="Less than"?a<b:a<=b);break;
                    default:match=string.Equals(value,expected,StringComparison.OrdinalIgnoreCase);break;
                }
                if(!match)return false;
            }
            return true;
        }
        public static ZoneDefinition Resolve(ZoneDefinition source,CardDocument card,SchemaDocument schema,out float opacity)
        {
            opacity=1;var variant=(source.variants??new List<ZoneVariant>()).FirstOrDefault(v=>v.id==card?.variantId);
            if(variant==null)variant=(source.variants??new List<ZoneVariant>()).FirstOrDefault(v=>v.requirements!=null&&v.requirements.Count>0&&Matches(v.requirements,card,schema));
            if(variant==null)return source;
            var zone=CardForgeData.Copy(source);zone.staticText=variant.staticText;zone.staticAssetPath=variant.staticAssetPath;zone.x=variant.x;zone.y=variant.y;zone.width=variant.width;zone.height=variant.height;zone.fontSize=variant.fontSize;zone.color=variant.color;zone.visible=source.visible&&variant.visible;
            var layer=source.layers?.FirstOrDefault(l=>l.id==variant.layerId);
            if(layer!=null){zone.visible&=layer.visible;opacity=layer.opacity;}return zone;
        }

        public static bool LayerMatches(ZoneLayer layer, CardDocument card, SchemaDocument schema)
        {
            if (layer == null || !layer.visible) return false;
            if (!string.IsNullOrEmpty(layer.requiredCardType) && !string.Equals(layer.requiredCardType, card?.cardType, StringComparison.OrdinalIgnoreCase)) return false;
            return Matches(layer.requirements, card, schema);
        }

        public static RuleStateMachineDefinition BuildStateMachine(TurnFlowDocument flow, IEnumerable<TriggerDocument> triggers, IEnumerable<ActionDocument> actions, IEnumerable<AbilityDocument> abilities, IEnumerable<KeywordDocument> keywords)
        {
            var machine = new RuleStateMachineDefinition();
            if (flow == null) return machine;
            var triggerList = (triggers ?? Enumerable.Empty<TriggerDocument>()).ToList();
            var actionList = (actions ?? Enumerable.Empty<ActionDocument>()).ToList();
            var abilityList = (abilities ?? Enumerable.Empty<AbilityDocument>()).ToList();
            var keywordList = (keywords ?? Enumerable.Empty<KeywordDocument>()).ToList();
            machine.triggers.AddRange(triggerList.Select(CardForgeData.Copy));
            machine.actions.AddRange(actionList.Select(CardForgeData.Copy));
            machine.abilities.AddRange(abilityList.Select(CardForgeData.Copy));
            machine.keywords.AddRange(keywordList.Select(CardForgeData.Copy));
            machine.compiledActions.AddRange(actionList.Select(CompileAction));
            foreach (var phase in flow.nodes ?? new List<TurnFlowNode>())
            {
                var state = new RuleStateDefinition { id = phase.id, name = phase.name };
                var schedules = phase.actionRules ?? new List<PhaseActionRule>();
                foreach (var schedule in schedules)
                    if (schedule != null && actionList.Any(action => action.id == schedule.actionId)) state.actionRules.Add(CardForgeData.Copy(schedule));
                state.abilityIds.AddRange(abilityList.Where(ability => ability.phaseId == phase.id).Select(ability => ability.id));
                machine.states.Add(state);
            }
            machine.initialStateId = machine.states.FirstOrDefault()?.id ?? "";
            foreach (var link in flow.links ?? new List<TurnFlowLink>())
            {
                var state = machine.states.FirstOrDefault(x => x.id == link.from);
                if (state != null) state.transitions.Add(new RuleStateTransition { targetStateId = link.to, resolution = link.label });
            }
            return machine;
        }

        public static RuleCompiledAction CompileAction(ActionDocument action)
        {
            var compiled = new RuleCompiledAction { id = action?.id ?? "", name = action?.name ?? "" };
            if (action == null) return compiled;
            var nodes = (action.graphNodes ?? new List<ResolutionGraphNode>()).Where(node => node != null && !string.IsNullOrEmpty(node.id)).ToList();
            var links = (action.graphLinks ?? new List<ResolutionGraphLink>()).Where(link => link != null).ToList();
            var incoming = new HashSet<string>(links.Select(link => link.to));
            var visited = new HashSet<string>();
            foreach (var root in nodes.Where(node => !incoming.Contains(node.id))) AppendGraphNode(root, nodes, links, visited, compiled);
            foreach (var node in nodes) AppendGraphNode(node, nodes, links, visited, compiled);
            compiled.causedTriggerIds.AddRange((action.causeTriggerIds ?? new List<string>()).Distinct());
            if (compiled.triggerBoundaryStepIds.Count == 0) compiled.causedTriggerIds.Clear();
            return compiled;
        }

        static void AppendGraphNode(ResolutionGraphNode node, List<ResolutionGraphNode> nodes, List<ResolutionGraphLink> links, HashSet<string> visited, RuleCompiledAction compiled)
        {
            if (node == null || !visited.Add(node.id)) return;
            compiled.orderedGraphStepIds.Add(node.id);
            if (node.causesStateChange) compiled.stateChangeStepIds.Add(node.id);
            if (node.emitsTriggers) compiled.triggerBoundaryStepIds.Add(node.id);
            foreach (var link in links.Where(link => link.from == node.id))
                AppendGraphNode(nodes.FirstOrDefault(candidate => candidate.id == link.to), nodes, links, visited, compiled);
        }
    }
}
