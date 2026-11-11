using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;

namespace CardForge.Editor
{
    public sealed partial class CardForgeStore
    {
        public static void RenewZone(ZoneDefinition z)
        {
            z.id = CardForgeData.NewId();
            var map = new Dictionary<string,string>();
            z.layers = z.layers ?? new List<ZoneLayer>(); z.variants = z.variants ?? new List<ZoneVariant>();
            foreach(var layer in z.layers) { if(layer==null) continue; var old=layer.id; layer.id=CardForgeData.NewId();map[old]=layer.id; }
            foreach(var variant in z.variants) { if(variant==null) continue; variant.id=CardForgeData.NewId(); if(map.TryGetValue(variant.layerId ?? "",out var id)) variant.layerId=id;
                foreach(var r in variant.requirements ?? new List<RequirementDefinition>()) if(r!=null) r.id=CardForgeData.NewId(); }
        }
        static void DeleteJson(string path)
        {
            if(!AssetDatabase.DeleteAsset(path) && File.Exists(DiskPath(path))) File.Delete(DiskPath(path));
        }
        public void DeleteAction(string id)
        {
            if(Abilities.Values.Any(a=>a.actionId==id)) throw new InvalidOperationException("This action is linked to an ability. Unlink it first.");
            if(Abilities.Values.Any(a=>(a.responseActionIds??new List<string>()).Contains(id))) throw new InvalidOperationException("This action is used as an ability response. Unlink it first.");
            if(Triggers.Values.Any(t=>(t.responseActionIds??new List<string>()).Contains(id))) throw new InvalidOperationException("This action is allowed by a trigger. Unlink it first.");
            if(Triggers.Values.Any(t=>(t.causeActionIds??new List<string>()).Contains(id))) throw new InvalidOperationException("This action is a trigger source. Unlink it first.");
            if(Actions.TryGetValue(id,out var action) && (action.causeTriggerIds??new List<string>()).Count>0) throw new InvalidOperationException("This action produces trigger events. Unlink them first.");
            if(TurnFlows.Values.Any(f=>(f.nodes??new List<TurnFlowNode>()).Any(n=>(n.actionRules??new List<PhaseActionRule>()).Any(rule=>rule.actionId==id)))) throw new InvalidOperationException("This action is assigned to a turn phase. Unlink it first.");
            DeleteJson(ActionPath(id)); Actions.Remove(id);
        }
        public void DeleteAbility(string id)
        {
            if (Triggers.Values.Any(trigger => (trigger.causeAbilityIds ?? new List<string>()).Contains(id))) throw new InvalidOperationException("This ability is a trigger source. Unlink it first.");
            if (Keywords.Values.Any(keyword => (keyword.abilityIds ?? new List<string>()).Contains(id))) throw new InvalidOperationException("This ability is part of a keyword. Unlink it first.");
            if (Cards.Values.Any(card => (card.abilityIds ?? new List<string>()).Contains(id))) throw new InvalidOperationException("This ability is assigned to a card. Unlink it first.");
            DeleteJson(AbilityPath(id)); Abilities.Remove(id);
        }
        public void DeleteKeyword(string id)
        {
            if (Cards.Values.Any(card => (card.keywordIds ?? new List<string>()).Contains(id))) throw new InvalidOperationException("This keyword is assigned to a card. Unlink it first.");
            DeleteJson(KeywordPath(id)); Keywords.Remove(id);
        }
        public void DeleteTrigger(string id)
        {
            if (Actions.Values.Any(action => action.triggerId == id)) throw new InvalidOperationException("This trigger is linked to actions. Unlink it first.");
            if (Actions.Values.Any(action => (action.causeTriggerIds ?? new List<string>()).Contains(id))) throw new InvalidOperationException("This trigger is produced by an action. Unlink it first.");
            if (Abilities.Values.Any(ability => ability.activationTriggerId == id)) throw new InvalidOperationException("This trigger gates an ability. Unlink it first.");
            if (Abilities.Values.Any(ability => (ability.causeTriggerIds ?? new List<string>()).Contains(id))) throw new InvalidOperationException("This trigger is produced by an ability. Unlink it first.");
            if (TurnFlows.Values.Any(flow => (flow.nodes ?? new List<TurnFlowNode>()).Any(node => (node.actionRules ?? new List<PhaseActionRule>()).Any(rule => (rule.beforeTriggerIds ?? new List<string>()).Contains(id) || (rule.afterTriggerIds ?? new List<string>()).Contains(id))))) throw new InvalidOperationException("This trigger is scheduled around a phase action. Unlink it first.");
            DeleteJson(TriggerPath(id)); Triggers.Remove(id);
        }
        public void DeleteFlow(string id)
        {
            if(TurnFlows.TryGetValue(id,out var f) && (Abilities.Values.Any(a=>(f.nodes??new List<TurnFlowNode>()).Any(n=>n.id==a.phaseId)) || (f.nodes??new List<TurnFlowNode>()).Any(n=>(n.actionRules??new List<PhaseActionRule>()).Any(rule=>Actions.ContainsKey(rule.actionId)))) )
                throw new InvalidOperationException("This flow has phases linked to actions or abilities. Unlink them first.");
            DeleteJson(TurnFlowPath(id)); TurnFlows.Remove(id);
        }
        public void DeleteIcon(string id)
        {
            if(!Icons.TryGetValue(id,out var icon)) return;
            string asset=icon.assetPath;
            bool used=!string.IsNullOrEmpty(asset) && (
                Schema.fields.Any(f=>f.defaultValue==asset) ||
                Cards.Values.Any(c=>c.values.Any(v=>v.value==asset) || c.zoneOverrides.Any(o=>o.assetPathOverride==asset)) ||
                Layouts.Values.Any(l=>l.backgroundAssetPath==asset || l.zones.Any(z=>z.staticAssetPath==asset || z.variants.Any(v=>v.staticAssetPath==asset))));
            if(used) throw new InvalidOperationException("This icon is in use. Remove its bindings first.");
            DeleteJson(IconPath(id));
            // Only delete artwork generated for this icon; an external/custom asset remains owned by its importer.
            if(string.Equals(asset,IconImagePath(id),StringComparison.OrdinalIgnoreCase)) AssetDatabase.DeleteAsset(asset);
            Icons.Remove(id);
        }
        public void DeleteBlock(string id)
        {
            if(!Blocks.TryGetValue(id,out var b)) return;
            foreach(var cid in b.cardIds.ToList()) if(Cards.TryGetValue(cid,out var c)) DeleteCard(b,c);
            foreach(var lid in b.layoutIds.ToList()) if(Layouts.TryGetValue(lid,out var l)) DeleteLayout(b,l);
            DeleteJson(BlockPath(id)); Blocks.Remove(id);
        }
        public List<string> ValidateFlow(TurnFlowDocument flow)
        {
            var issues=new List<string>();
            if(flow==null) { issues.Add("Select a turn flow."); return issues; }
            flow.nodes=flow.nodes??new List<TurnFlowNode>();flow.links=flow.links??new List<TurnFlowLink>();
            flow.nodes.RemoveAll(n=>n==null);flow.links.RemoveAll(l=>l==null);
            if(flow.nodes.Count==0) issues.Add("Add at least one phase.");
            if(flow.nodes.Select(n=>n.id).Distinct().Count()!=flow.nodes.Count) issues.Add("Duplicate phase IDs.");
            var ids=new HashSet<string>(flow.nodes.Select(n=>n.id));
            if(flow.nodes.Any(n=>n==null||string.IsNullOrWhiteSpace(n.name))) issues.Add("Every phase needs a name.");
            if(flow.links.Any(l=>l==null||!ids.Contains(l.from)||!ids.Contains(l.to))) issues.Add("A connection has a missing endpoint.");
            if(flow.links.Where(l=>l!=null).Select(l=>l.from+"/"+l.to).Distinct().Count()!=flow.links.Count(l=>l!=null)) issues.Add("Duplicate connections.");
            foreach (var node in flow.nodes)
            {
                var schedules = node.actionRules ?? new List<PhaseActionRule>();
                var assigned = schedules.Where(rule=>rule!=null).Select(rule=>rule.actionId).ToList();
                if (assigned.Count != assigned.Distinct().Count()) issues.Add("Duplicate actions assigned to " + node.name + ".");
                foreach (string id in assigned)
                {
                    if (!Actions.ContainsKey(id)) issues.Add(node.name + " refers to missing action " + id + ".");
                }
                if (schedules.Any(rule=>rule!=null&&string.IsNullOrWhiteSpace(rule.availability))) issues.Add("Every scheduled action in " + node.name + " needs an availability window.");
                if (schedules.Any(rule=>rule!=null&&string.IsNullOrWhiteSpace(rule.resolutionOrder))) issues.Add("Every scheduled action in " + node.name + " needs a resolution order.");
                foreach (var schedule in schedules.Where(rule => rule != null))
                {
                    var before = schedule.beforeTriggerIds ?? new List<string>();
                    var after = schedule.afterTriggerIds ?? new List<string>();
                    if (before.Distinct().Count() != before.Count || after.Distinct().Count() != after.Count) issues.Add("Trigger schedules around " + node.name + " contain duplicates.");
                    foreach (var triggerId in before.Concat(after).Distinct()) if (!Triggers.ContainsKey(triggerId)) issues.Add(node.name + " refers to missing trigger " + triggerId + ".");
                }
            }
            if(flow.nodes.Count>1) {
                var seen=new HashSet<string>(); var queue=new Queue<string>(); queue.Enqueue(flow.nodes[0].id);
                while(queue.Count>0) { var id=queue.Dequeue();if(!seen.Add(id))continue; foreach(var link in flow.links.Where(l=>l.from==id && ids.Contains(l.to))) queue.Enqueue(link.to); }
                if(seen.Count!=ids.Count) issues.Add("Some phases are unreachable from the first phase.");
            }
            return issues;
        }
        public List<string> ValidateTrigger(TriggerDocument trigger)
        {
            var issues=new List<string>();
            if(trigger==null) { issues.Add("Select a trigger."); return issues; }
            if(string.IsNullOrWhiteSpace(trigger.name)) issues.Add("Name the trigger.");
            if(string.IsNullOrWhiteSpace(trigger.eventType)) issues.Add("Choose the game-state event that starts the trigger.");
            var rules = trigger.rules ?? new List<TriggerRule>();
            if(rules.Count == 0) issues.Add("Add at least one rule to define when the event qualifies.");
            foreach(var rule in rules)
            {
                if(rule == null || string.IsNullOrWhiteSpace(rule.subject)) issues.Add("Every trigger rule needs a subject.");
                else if(string.IsNullOrWhiteSpace(rule.property)) issues.Add("Every trigger rule needs a property.");
            }
            var actionIds=trigger.responseActionIds??new List<string>();
            if(actionIds.Distinct().Count()!=actionIds.Count) issues.Add("Response actions contain duplicates.");
            foreach(var actionId in actionIds) if(!Actions.ContainsKey(actionId)) issues.Add("A response action is missing: " + actionId);
            var causeActionIds=trigger.causeActionIds??new List<string>();
            if(causeActionIds.Distinct().Count()!=causeActionIds.Count) issues.Add("Trigger sources contain duplicates.");
            foreach(var actionId in causeActionIds) if(!Actions.ContainsKey(actionId)) issues.Add("A trigger source action is missing: " + actionId);
            var causeAbilityIds=trigger.causeAbilityIds??new List<string>();
            if(causeAbilityIds.Distinct().Count()!=causeAbilityIds.Count) issues.Add("Trigger source abilities contain duplicates.");
            foreach(var abilityId in causeAbilityIds) if(!Abilities.ContainsKey(abilityId)) issues.Add("A trigger source ability is missing: " + abilityId);
            if (string.IsNullOrWhiteSpace(trigger.responseWindow)) issues.Add("Choose a response window.");
            return issues;
        }
        public List<string> ValidateAction(ActionDocument action)
        {
            var issues=new List<string>();
            if(action==null) { issues.Add("Select an action."); return issues; }
            if(string.IsNullOrWhiteSpace(action.name)) issues.Add("Name the action.");
            if(!string.IsNullOrEmpty(action.triggerId)&&!Triggers.ContainsKey(action.triggerId)) issues.Add("The linked viability trigger is missing.");
            var causeTriggerIds = action.causeTriggerIds ?? new List<string>();
            if(causeTriggerIds.Distinct().Count()!=causeTriggerIds.Count) issues.Add("Produced triggers contain duplicates.");
            foreach(var triggerId in causeTriggerIds) if(!Triggers.ContainsKey(triggerId)) issues.Add("A produced trigger is missing: " + triggerId);
            if(causeTriggerIds.Count > 0 && !(action.graphNodes ?? new List<ResolutionGraphNode>()).Any(node => node != null && node.emitsTriggers)) issues.Add("Mark a graph step as the trigger-emission boundary for the produced triggers.");
            if(action.orientationChange!=CardOrientationChange.None&&string.IsNullOrWhiteSpace(action.orientationTarget)) issues.Add("Define the card affected by the orientation change.");
            if (string.IsNullOrWhiteSpace(action.resolution)) issues.Add("Define the resolution order.");
            issues.AddRange(ValidateResolutionGraph(action.graphNodes,action.graphLinks));
            return issues;
        }
        public List<string> ValidateAbility(AbilityDocument ability)
        {
            var issues=new List<string>();
            if(ability==null) { issues.Add("Select an ability."); return issues; }
            if(string.IsNullOrWhiteSpace(ability.name)) issues.Add("Name the ability.");
            if(!string.IsNullOrEmpty(ability.cardId)&&!Cards.ContainsKey(ability.cardId)) issues.Add("The bound card is missing.");
            if(!string.IsNullOrEmpty(ability.activationTriggerId)&&!Triggers.ContainsKey(ability.activationTriggerId)) issues.Add("The activation trigger is missing.");
            if(!string.IsNullOrEmpty(ability.actionId)&&!Actions.ContainsKey(ability.actionId)) issues.Add("The linked action is missing.");
            if(!string.IsNullOrEmpty(ability.phaseId)&&!TurnFlows.Values.Any(f=>(f.nodes??new List<TurnFlowNode>()).Any(n=>n.id==ability.phaseId))) issues.Add("The linked phase is missing.");
            var responseIds = ability.responseActionIds ?? new List<string>();
            if (responseIds.Distinct().Count() != responseIds.Count) issues.Add("Response actions contain duplicates.");
            foreach(var actionId in responseIds) if(!Actions.ContainsKey(actionId)) issues.Add("A response action is missing: " + actionId);
            var causeTriggerIds = ability.causeTriggerIds ?? new List<string>();
            if(causeTriggerIds.Distinct().Count()!=causeTriggerIds.Count) issues.Add("Produced triggers contain duplicates.");
            foreach(var triggerId in causeTriggerIds) if(!Triggers.ContainsKey(triggerId)) issues.Add("A produced trigger is missing: " + triggerId);
            if(causeTriggerIds.Count > 0 && !(ability.graphNodes ?? new List<ResolutionGraphNode>()).Any(node => node != null && node.emitsTriggers)) issues.Add("Mark an ability graph step as the trigger-emission boundary for the produced triggers.");
            foreach(var rule in ability.rules??new List<AbilityRule>()) if(rule==null||string.IsNullOrWhiteSpace(rule.property)) issues.Add("Every ability rule needs a property.");
            issues.AddRange(ValidateResolutionGraph(ability.graphNodes,ability.graphLinks));
            return issues;
        }
        public List<string> ValidateKeyword(KeywordDocument keyword)
        {
            var issues = new List<string>();
            if (keyword == null) { issues.Add("Select a keyword."); return issues; }
            if (string.IsNullOrWhiteSpace(keyword.name)) issues.Add("Name the keyword.");
            var abilityIds = keyword.abilityIds ?? new List<string>();
            if (abilityIds.Count == 0) issues.Add("Add at least one ability to this keyword.");
            if (abilityIds.Distinct().Count() != abilityIds.Count) issues.Add("Keyword abilities contain duplicates.");
            foreach (var abilityId in abilityIds) if (!Abilities.ContainsKey(abilityId)) issues.Add("A keyword ability is missing: " + abilityId);
            return issues;
        }
        static List<string> ValidateResolutionGraph(List<ResolutionGraphNode> nodes,List<ResolutionGraphLink> links)
        {
            var issues=new List<string>();nodes=nodes??new List<ResolutionGraphNode>();links=links??new List<ResolutionGraphLink>();
            if(nodes.Count==0) issues.Add("Add at least one graph step.");
            if(nodes.Any(n=>n==null||string.IsNullOrWhiteSpace(n.title))) issues.Add("Every graph step needs a title.");
            var ids=new HashSet<string>(nodes.Where(n=>n!=null).Select(n=>n.id));
            if(links.Any(l=>l==null||!ids.Contains(l.from)||!ids.Contains(l.to))) issues.Add("A graph connection has a missing endpoint.");
            if(links.Where(l=>l!=null).Select(l=>l.from+"/"+l.to).Distinct().Count()!=links.Count(l=>l!=null)) issues.Add("Graph connections contain duplicates.");
            if (ids.Count != nodes.Count) issues.Add("Graph step IDs are missing or duplicated.");
            if (nodes.Count > 1 && !links.Any(l => l != null && ids.Contains(l.from) && ids.Contains(l.to)))
                issues.Add("Connect your graph steps to describe resolution order.");
            return issues;
        }
    }
}
