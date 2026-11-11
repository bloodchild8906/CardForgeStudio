using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using CardForge.Editor;
using CardForge.Editor.Importing;
using UnityEditor;
using UnityEngine;

namespace CardForge.EditorUI.Screens
{
    [Serializable] public sealed class StudioPackage
    {
        public string format="CardForge";
        public int version=1;
        public SchemaDocument schema;
        public List<BlockDocument> blocks=new List<BlockDocument>();
        public List<LayoutDocument> layouts=new List<LayoutDocument>();
        public List<CardDocument> cards=new List<CardDocument>();
        public List<IconDocument> icons=new List<IconDocument>();
        public List<TurnFlowDocument> flows=new List<TurnFlowDocument>();
        public List<TriggerDocument> triggers=new List<TriggerDocument>();
        public List<ActionDocument> actions=new List<ActionDocument>();
        public List<AbilityDocument> abilities=new List<AbilityDocument>();
        public List<KeywordDocument> keywords=new List<KeywordDocument>();
    }
    public sealed class ImportPlan
    {
        public readonly List<object> Documents=new List<object>();
        public readonly List<string> Errors=new List<string>();
        public int Created,Updated,Skipped;
        public string Screen;
    }
    public static class DataTransfer
    {
        static readonly string[] reserved={"version","sourceBlockId","sourceCardId"};
        public static Type DocumentType(string screen)
        {
            switch(screen){case "layouts":return typeof(LayoutDocument);case "cards":return typeof(CardDocument);case "blocks":return typeof(BlockDocument);case "turnflow":return typeof(TurnFlowDocument);case "triggers":return typeof(TriggerDocument);case "actions":return typeof(ActionDocument);case "abilities":return typeof(AbilityDocument);case "keywords":return typeof(KeywordDocument);case "icons":return typeof(IconDocument);default:throw new InvalidOperationException("Unknown library.");}
        }
        public static List<string> Fields(LiveCatalog catalog,string screen)
        {
            var result=DocumentType(screen).GetFields().Where(f=>!reserved.Contains(f.Name)&&(f.FieldType==typeof(string)||f.FieldType==typeof(int)||f.FieldType==typeof(float)||f.FieldType==typeof(bool))).Select(f=>f.Name).ToList();
            if(screen=="cards")result.AddRange(catalog.Store.Schema.fields.Select(f=>"field:"+f.key));return result;
        }
        public static bool ValidId(string id)=>!string.IsNullOrWhiteSpace(id)&&id.Length<=128&&id.All(c=>char.IsLetterOrDigit(c)||c=='-'||c=='_');
        static string Id(object doc)=>(string)doc.GetType().GetField("id").GetValue(doc);
        static bool Exists(LiveCatalog c,string screen,string id) {
            switch(screen) {case "cards":return c.Store.Cards.ContainsKey(id);case "layouts":return c.Store.Layouts.ContainsKey(id);case "blocks":return c.Store.Blocks.ContainsKey(id);case "turnflow":return c.Store.TurnFlows.ContainsKey(id);case "triggers":return c.Store.Triggers.ContainsKey(id);case "actions":return c.Store.Actions.ContainsKey(id);case "abilities":return c.Store.Abilities.ContainsKey(id);case "keywords":return c.Store.Keywords.ContainsKey(id);case "icons":return c.Store.Icons.ContainsKey(id);default:return false;}
        }
        static object Current(LiveCatalog c,string screen,string id)
        {
            switch(screen){case "cards":return c.Store.Cards[id];case "layouts":return c.Store.Layouts[id];case "blocks":return c.Store.Blocks[id];case "turnflow":return c.Store.TurnFlows[id];case "triggers":return c.Store.Triggers[id];case "actions":return c.Store.Actions[id];case "abilities":return c.Store.Abilities[id];case "keywords":return c.Store.Keywords[id];case "icons":return c.Store.Icons[id];default:return null;}
        }
        public static ImportPlan Plan(LiveCatalog catalog,string screen,ImportTable table,List<string> mapping,int conflict)
        {
            var plan=new ImportPlan{Screen=screen};var type=DocumentType(screen);var mapped=mapping.Where(x=>x!="— Ignore —").ToList();
            if(mapped.Count==0)plan.Errors.Add("Map at least one column.");
            if(mapped.Distinct().Count()!=mapped.Count)plan.Errors.Add("Each destination field can be mapped only once.");
            var seen=new HashSet<string>();
            for(int r=0;r<table.Rows.Count;r++) {
                try {
                    var values=new Dictionary<string,string>();for(int col=0;col<mapping.Count;col++)if(mapping[col]!="— Ignore —")values[mapping[col]]=table.Rows[r][col];
                    values.TryGetValue("id",out var incoming);bool existing=!string.IsNullOrEmpty(incoming)&&Exists(catalog,screen,incoming);
                    if(existing&&conflict==2){plan.Skipped++;continue;}
                    bool updating=existing&&conflict==1;
                    if((screen=="cards"||screen=="layouts")&&catalog.Block==null)throw new FormatException("Choose an active block.");
                    if(updating&&((screen=="cards"&&!catalog.Block.cardIds.Contains(incoming))||(screen=="layouts"&&!catalog.Block.layoutIds.Contains(incoming))))throw new FormatException("The matching ID belongs to another block. Activate that block or create new IDs.");
                    var doc=updating?JsonUtility.FromJson(JsonUtility.ToJson(Current(catalog,screen,incoming)),type):Activator.CreateInstance(type);
                    string id=conflict==0?CardForgeData.NewId():string.IsNullOrEmpty(incoming)?CardForgeData.NewId():incoming;
                    if(!ValidId(id))throw new FormatException("Invalid ID.");if(!seen.Add(id))throw new FormatException("Duplicate record ID in source: "+id);
                    foreach(var pair in values) {
                        if(pair.Key=="id")continue;
                        if(pair.Key.StartsWith("field:",StringComparison.Ordinal)) {
                            var field=catalog.Store.Schema.fields.First(f=>f.key==pair.Key.Substring(6));ValidateValue(field.kind,pair.Value);CardForgeData.SetValue((CardDocument)doc,field.id,pair.Value);continue;
                        }
                        var f=type.GetField(pair.Key);if(f==null)throw new FormatException("Unknown field: "+pair.Key);
                        object value=pair.Value;
                        if(f.FieldType==typeof(int)) { if(!int.TryParse(pair.Value,NumberStyles.Integer,CultureInfo.InvariantCulture,out var n))throw new FormatException(pair.Key+" must be an integer.");value=n; }
                        if(f.FieldType==typeof(float)) { if(!float.TryParse(pair.Value,NumberStyles.Float,CultureInfo.InvariantCulture,out var n)||float.IsNaN(n)||float.IsInfinity(n))throw new FormatException(pair.Key+" must be a finite number.");value=n; }
                        if(f.FieldType==typeof(bool)) { if(!bool.TryParse(pair.Value,out var b))throw new FormatException(pair.Key+" must be true or false.");value=b; }
                        f.SetValue(doc,value);
                    }
                    type.GetField("id").SetValue(doc,id);
                    if(string.IsNullOrWhiteSpace((string)type.GetField("name").GetValue(doc)))throw new FormatException("Name cannot be blank.");
                    if(doc is CardDocument card) {
                        if(catalog.Block==null)throw new FormatException("Choose an active block.");
                        if(!values.ContainsKey("layoutId")&&!updating)card.layoutId=catalog.Block.layoutIds.Where(catalog.Store.Layouts.ContainsKey).FirstOrDefault(l=>!catalog.Store.Layouts[l].isBack)??"";
                        if(!values.ContainsKey("backLayoutId")&&!updating)card.backLayoutId=catalog.Block.defaultBackLayoutId;
                        if(!catalog.Block.layoutIds.Contains(card.layoutId)||!catalog.Store.Layouts.ContainsKey(card.layoutId)||catalog.Store.Layouts[card.layoutId].isBack)throw new FormatException("Choose a valid front layout in the active block.");
                        if(!string.IsNullOrEmpty(card.backLayoutId)&&(!catalog.Block.layoutIds.Contains(card.backLayoutId)||!catalog.Store.Layouts.TryGetValue(card.backLayoutId,out var back)||!back.isBack))throw new FormatException("Back layout is missing or is not a back in this block.");
                    }
                    if(doc is LayoutDocument layout&&(layout.width<50||layout.height<50))throw new FormatException("Layout dimensions must be at least 50 pixels.");
                    if(doc is AbilityDocument ability)ValidateAbility(catalog.Store,ability);
                    plan.Documents.Add(doc);if(existing&&conflict==1)plan.Updated++;else plan.Created++;
                } catch(Exception e){plan.Errors.Add("Row "+(r+2)+": "+e.Message);}
            }
            if((screen=="cards"||screen=="layouts")&&catalog.Block==null)plan.Errors.Add("Select an active block first.");
            if(table.Rows.Count==0)plan.Errors.Add("There are no data rows.");return plan;
        }
        static void ValidateValue(FieldKind kind,string value)
        {
            if(kind==FieldKind.Number&&(!double.TryParse(value,NumberStyles.Float,CultureInfo.InvariantCulture,out var number)||double.IsNaN(number)||double.IsInfinity(number)))throw new FormatException("A numeric card field is invalid.");
            if(kind==FieldKind.Boolean&&!bool.TryParse(value,out _))throw new FormatException("Boolean card fields must be true or false.");
        }
        static void ValidateAbility(CardForgeStore store,AbilityDocument ability)
        {
            if(!string.IsNullOrEmpty(ability.actionId)&&!store.Actions.ContainsKey(ability.actionId))throw new FormatException("Linked action does not exist.");
            if(!string.IsNullOrEmpty(ability.phaseId)&&!store.TurnFlows.Values.Any(f=>f.nodes.Any(n=>n.id==ability.phaseId)))throw new FormatException("Linked phase does not exist.");
        }
        public static void Apply(LiveCatalog catalog,ImportPlan plan)
        {
            if(plan.Errors.Count>0)throw new InvalidOperationException("Resolve all import errors first.");
            Transaction(catalog,()=> { foreach(var doc in plan.Documents) Save(catalog,doc); });
        }
        static void Save(LiveCatalog c,object doc)
        {
            var s=c.Store;
            if(doc is BlockDocument b)s.SaveBlock(b);
            else if(doc is LayoutDocument l){if(c.Block==null)throw new InvalidOperationException("Select a block first.");if(!c.Block.layoutIds.Contains(l.id))c.Block.layoutIds.Add(l.id);s.SaveLayout(c.BlockId,l);s.SaveBlock(c.Block);}
            else if(doc is CardDocument card){if(c.Block==null)throw new InvalidOperationException("Select a block first.");if(!c.Block.cardIds.Contains(card.id))c.Block.cardIds.Add(card.id);s.SaveCard(c.BlockId,card);s.SaveBlock(c.Block);}
            else if(doc is IconDocument i)s.SaveIcon(i);
            else if(doc is TurnFlowDocument f)s.SaveTurnFlow(f);
            else if(doc is TriggerDocument t)s.SaveTrigger(t);
            else if(doc is ActionDocument a)s.SaveAction(a);
            else if(doc is AbilityDocument ab)s.SaveAbility(ab);
            else if(doc is KeywordDocument keyword)s.SaveKeyword(keyword);
        }
        static void Transaction(LiveCatalog catalog,System.Action action)
        {
            string root=Path.GetFullPath(CardForgeStore.DataRoot);Directory.CreateDirectory(root);
            var before=Directory.GetFiles(root,"*.json",SearchOption.AllDirectories).ToDictionary(p=>p,File.ReadAllBytes);
            try { action();AssetDatabase.Refresh(); }
            catch {
                foreach(var file in Directory.GetFiles(root,"*.json",SearchOption.AllDirectories))if(!before.ContainsKey(file)){File.Delete(file);if(File.Exists(file+".meta"))File.Delete(file+".meta");}
                foreach(var pair in before){Directory.CreateDirectory(Path.GetDirectoryName(pair.Key));File.WriteAllBytes(pair.Key,pair.Value);}AssetDatabase.Refresh();catalog.Reload();throw;
            }
        }
        public static StudioPackage ReadPackage(string text)
        {
            var parsed=JsonReader.Parse(text) as Dictionary<string,object>;
            if(parsed==null||!parsed.TryGetValue("format",out var format)||!Equals(format,"CardForge"))return null;
            var p=JsonUtility.FromJson<StudioPackage>(text);if(p.version!=1)throw new FormatException("Unsupported CardForge package version.");
            p.blocks=p.blocks??new List<BlockDocument>();p.layouts=p.layouts??new List<LayoutDocument>();p.cards=p.cards??new List<CardDocument>();p.icons=p.icons??new List<IconDocument>();p.flows=p.flows??new List<TurnFlowDocument>();p.triggers=p.triggers??new List<TriggerDocument>();p.actions=p.actions??new List<ActionDocument>();p.abilities=p.abilities??new List<AbilityDocument>();p.keywords=p.keywords??new List<KeywordDocument>();
            var docs=p.blocks.Cast<object>().Concat(p.layouts).Concat(p.cards).Concat(p.icons).Concat(p.flows).Concat(p.triggers).Concat(p.actions).Concat(p.abilities).Concat(p.keywords).ToList();
            if(docs.Count==0)throw new FormatException("The package has no records.");
            if(docs.Any(d=>d==null||!ValidId(Id(d))))throw new FormatException("The package contains an invalid document ID.");
            if(docs.Select(Id).Distinct().Count()!=docs.Count)throw new FormatException("Duplicate document IDs in package.");return p;
        }
        [Serializable] sealed class Documents<T> { public List<T> items=new List<T>(); }
        public static StudioPackage ReadNativePackage(string screen,string text)
        {
            var value=JsonReader.Parse(text);var objects=value is List<object> list?list:new List<object>{value};
            if(!objects.Any(o=>o is Dictionary<string,object> d && d.Values.Any(v=>v is List<object>)))return null;
            string wrapped="{\"items\":"+(value is List<object>?text:"["+text+"]")+"}";var p=new StudioPackage();
            switch(screen) {
                case "blocks":p.blocks=JsonUtility.FromJson<Documents<BlockDocument>>(wrapped).items;break;
                case "layouts":p.layouts=JsonUtility.FromJson<Documents<LayoutDocument>>(wrapped).items;break;
                case "cards":p.cards=JsonUtility.FromJson<Documents<CardDocument>>(wrapped).items;break;
                case "icons":p.icons=JsonUtility.FromJson<Documents<IconDocument>>(wrapped).items;break;
                case "turnflow":p.flows=JsonUtility.FromJson<Documents<TurnFlowDocument>>(wrapped).items;break;
                case "triggers":p.triggers=JsonUtility.FromJson<Documents<TriggerDocument>>(wrapped).items;break;
                case "keywords":p.keywords=JsonUtility.FromJson<Documents<KeywordDocument>>(wrapped).items;break;
                default:throw new FormatException("This library expects flat JSON records.");
            }
            // Reuse package validation, including stable ID and version checks.
            return ReadPackage(JsonUtility.ToJson(p));
        }
        public static string PackageSummary(StudioPackage p)=>$"{p.blocks.Count} blocks, {p.layouts.Count} layouts, {p.cards.Count} cards, {p.icons.Count} icons, {p.flows.Count} flows, {p.triggers.Count} triggers, {p.actions.Count} actions, {p.abilities.Count} abilities, {p.keywords.Count} keywords";
        public static void ImportPackage(LiveCatalog catalog,StudioPackage p,bool matchSchemaKeys=false)
        {
            p=CardForgeData.Copy(p);
            var fieldMap=new Dictionary<string,string>();
            foreach(var field in p.schema?.fields ?? new List<FieldDefinition>()) {
                var sameId=catalog.Store.Schema.fields.FirstOrDefault(f=>f.id==field.id);
                if(sameId!=null && (sameId.key!=field.key || sameId.kind!=field.kind))throw new FormatException("Schema ID conflicts with a different field: "+field.key);
                var sameKey=catalog.Store.Schema.fields.FirstOrDefault(f=>string.Equals(f.key,field.key,StringComparison.OrdinalIgnoreCase));
                if(sameId==null && sameKey!=null) {
                    if(!matchSchemaKeys)throw new FormatException("Schema key collision: "+field.key+". Enable matching fields by key and type to import between projects.");
                    if(sameKey.kind!=field.kind)throw new FormatException("Schema field types differ: "+field.key);
                    fieldMap[field.id]=sameKey.id;field.id=sameKey.id;
                }
            }
            System.Action<List<RequirementDefinition>> mapRequirements=items=>{foreach(var r in items??new List<RequirementDefinition>())if(fieldMap.TryGetValue(r.fieldId??"",out var id))r.fieldId=id;};
            foreach(var layout in p.layouts) {
                mapRequirements(layout.requirements);
                foreach(var zone in layout.zones??new List<ZoneDefinition>()) {
                    if(fieldMap.TryGetValue(zone.fieldId??"",out var id))zone.fieldId=id;
                    foreach(var variant in zone.variants??new List<ZoneVariant>())mapRequirements(variant.requirements);
                }
            }
            foreach(var card in p.cards) {foreach(var value in card.values??new List<CardFieldValue>())if(fieldMap.TryGetValue(value.fieldId??"",out var id))value.fieldId=id;mapRequirements(card.requirements);}
            // Packages preserve stable IDs. Existing records are skipped; table import offers explicit updates.
            Transaction(catalog,()=> {
                var s=catalog.Store;
                if(p.schema!=null) { foreach(var f in p.schema.fields??new List<FieldDefinition>()) {
                    if(!ValidId(f.id)||string.IsNullOrWhiteSpace(f.key))throw new FormatException("Invalid schema field.");
                    if(s.Schema.fields.Any(x=>x.id==f.id))continue;
                    if(s.Schema.fields.Any(x=>string.Equals(x.key,f.key,StringComparison.OrdinalIgnoreCase)))throw new FormatException("Schema key collision: "+f.key+". Rename the incoming key before import.");
                    s.Schema.fields.Add(f);
                }s.SaveSchema();}
                var availableLayouts=new HashSet<string>(s.Layouts.Keys.Concat(p.layouts.Select(l=>l.id)));
                foreach(var b in p.blocks) {
                    b.layoutIds=b.layoutIds??new List<string>();b.cardIds=b.cardIds??new List<string>();
                    if(b.layoutIds.Any(id=>!availableLayouts.Contains(id)))throw new FormatException("Block "+b.name+" refers to a missing layout.");
                    if(!s.Blocks.ContainsKey(b.id))s.SaveBlock(b);
                }
                foreach(var l in p.layouts) {
                    if(s.Layouts.ContainsKey(l.id))continue;l.zones=l.zones??new List<ZoneDefinition>();l.requirements=l.requirements??new List<RequirementDefinition>();
                    var owner=s.Blocks.Values.FirstOrDefault(b=>b.layoutIds.Contains(l.id))??catalog.Block;
                    if(owner==null)throw new FormatException("Create a block to receive layouts.");
                    foreach(var z in l.zones){z.layers=z.layers??new List<ZoneLayer>();z.variants=z.variants??new List<ZoneVariant>();}
                    if(!owner.layoutIds.Contains(l.id))owner.layoutIds.Add(l.id);s.SaveLayout(owner.id,l);s.SaveBlock(owner);
                }
                foreach(var c in p.cards) {
                    if(s.Cards.ContainsKey(c.id))continue;c.values=c.values??new List<CardFieldValue>();c.zoneOverrides=c.zoneOverrides??new List<ZoneOverride>();c.requirements=c.requirements??new List<RequirementDefinition>();
                    var owner=s.Blocks.Values.FirstOrDefault(b=>b.cardIds.Contains(c.id))??catalog.Block;
                    if(owner==null||!owner.layoutIds.Contains(c.layoutId)||!s.Layouts.ContainsKey(c.layoutId))throw new FormatException("A card's front layout is unavailable in its destination block.");
                    if(!owner.cardIds.Contains(c.id))owner.cardIds.Add(c.id);s.SaveCard(owner.id,c);s.SaveBlock(owner);
                }
                foreach(var t in p.triggers)if(!s.Triggers.ContainsKey(t.id)){t.responseActionIds=t.responseActionIds??new List<string>();t.causeActionIds=t.causeActionIds??new List<string>();t.rules=t.rules??new List<TriggerRule>();var triggerActionIds=t.responseActionIds.Concat(t.causeActionIds);if(triggerActionIds.Any(id=>!s.Actions.ContainsKey(id)&&!p.actions.Any(a=>a.id==id)))throw new FormatException("Trigger "+t.name+" refers to a missing action.");s.SaveTrigger(t);}
                foreach(var a in p.actions)if(!s.Actions.ContainsKey(a.id))s.SaveAction(a);
                foreach(var f in p.flows)if(!s.TurnFlows.ContainsKey(f.id)){f.nodes=f.nodes??new List<TurnFlowNode>();f.links=f.links??new List<TurnFlowLink>();if(s.ValidateFlow(f).Count>0)throw new FormatException("Invalid flow: "+f.name);s.SaveTurnFlow(f);}
                foreach(var a in p.abilities)if(!s.Abilities.ContainsKey(a.id)){ValidateAbility(s,a);s.SaveAbility(a);}
                foreach(var k in p.keywords)if(!s.Keywords.ContainsKey(k.id)){k.abilityIds=k.abilityIds??new List<string>();if(k.abilityIds.Any(id=>!s.Abilities.ContainsKey(id)&&!p.abilities.Any(a=>a.id==id)))throw new FormatException("Keyword "+k.name+" refers to a missing ability.");s.SaveKeyword(k);}
                foreach(var i in p.icons)if(!s.Icons.ContainsKey(i.id)){i.layers=i.layers??new List<IconLayer>();if(i.version<2){foreach(var layer in i.layers){layer.visible=true;layer.opacity=1;}i.version=2;}s.SaveIcon(i);}
            });
            catalog.Reload();
        }
        public static void Export(LiveCatalog c,string screen,string[] ids,bool csv,bool includeLinked=true)
        {
            string path=EditorUtility.SaveFilePanel("Export "+screen,"",screen,csv?"csv":"json");if(string.IsNullOrEmpty(path))return;
            var selected=new HashSet<string>(ids);
            if(csv) {
                var fields=Fields(c,screen);var rows=new List<IEnumerable<string>>{fields};
                foreach(var id in ids){var doc=Current(c,screen,id);rows.Add(fields.Select(f=> f.StartsWith("field:",StringComparison.Ordinal)?CardForgeData.Value((CardDocument)doc,c.Store.Schema.fields.First(x=>x.key==f.Substring(6))):Convert.ToString(doc.GetType().GetField(f).GetValue(doc),CultureInfo.InvariantCulture)).ToArray());}
                File.WriteAllText(path,TabularReader.Csv(rows),new System.Text.UTF8Encoding(true));
            } else {
                var p=new StudioPackage();var s=c.Store;
                switch(screen){case "blocks":p.blocks=s.Blocks.Values.Where(x=>selected.Contains(x.id)).ToList();break;case "layouts":p.layouts=s.Layouts.Values.Where(x=>selected.Contains(x.id)).ToList();break;case "cards":p.cards=s.Cards.Values.Where(x=>selected.Contains(x.id)).ToList();break;case "icons":p.icons=s.Icons.Values.Where(x=>selected.Contains(x.id)).ToList();break;case "turnflow":p.flows=s.TurnFlows.Values.Where(x=>selected.Contains(x.id)).ToList();break;case "triggers":p.triggers=s.Triggers.Values.Where(x=>selected.Contains(x.id)).ToList();break;case "actions":p.actions=s.Actions.Values.Where(x=>selected.Contains(x.id)).ToList();break;case "abilities":p.abilities=s.Abilities.Values.Where(x=>selected.Contains(x.id)).ToList();break;case "keywords":p.keywords=s.Keywords.Values.Where(x=>selected.Contains(x.id)).ToList();break;}
                if(includeLinked) {
                    p.schema=s.Schema;
                    var cardIds=new HashSet<string>(p.blocks.SelectMany(b=>b.cardIds).Concat(p.cards.Select(x=>x.id)));p.cards=s.Cards.Values.Where(x=>cardIds.Contains(x.id)).ToList();
                    var layoutIds=new HashSet<string>(p.blocks.SelectMany(b=>b.layoutIds).Concat(p.cards.SelectMany(x=>new[]{x.layoutId,x.backLayoutId})).Concat(p.layouts.Select(x=>x.id)));p.layouts=s.Layouts.Values.Where(x=>layoutIds.Contains(x.id)).ToList();
                    if(p.cards.Count>0||p.layouts.Count>0)p.icons=s.Icons.Values.ToList();
                    var abilityIds=new HashSet<string>(p.abilities.Select(a=>a.id).Concat(p.cards.SelectMany(c=>c.abilityIds??new List<string>())).Concat(p.triggers.SelectMany(t=>s.Abilities.Values.Where(a=>(a.causeTriggerIds??new List<string>()).Contains(t.id)).Select(a=>a.id))));
                    var keywordIds=new HashSet<string>(p.keywords.Select(k=>k.id).Concat(p.cards.SelectMany(c=>c.keywordIds??new List<string>())));p.keywords=s.Keywords.Values.Where(k=>keywordIds.Contains(k.id)).ToList();
                    abilityIds.UnionWith(p.keywords.SelectMany(k=>k.abilityIds??new List<string>()));p.abilities=s.Abilities.Values.Where(a=>abilityIds.Contains(a.id)).ToList();
                    var actionIds=new HashSet<string>(p.abilities.Select(a=>a.actionId).Concat(p.abilities.SelectMany(a=>a.responseActionIds??new List<string>())).Concat(p.actions.Select(a=>a.id)).Concat(p.flows.SelectMany(f=>(f.nodes??new List<TurnFlowNode>()).SelectMany(n=>(n.actionRules??new List<PhaseActionRule>()).Select(r=>r.actionId)))).Concat(p.triggers.SelectMany(t=>(t.responseActionIds??new List<string>()).Concat(t.causeActionIds??new List<string>()))).Concat(p.triggers.SelectMany(t=>s.Actions.Values.Where(a=>(a.causeTriggerIds??new List<string>()).Contains(t.id)).Select(a=>a.id))));p.actions=s.Actions.Values.Where(a=>actionIds.Contains(a.id)).ToList();
                    var triggerIds=new HashSet<string>(p.triggers.Select(t=>t.id).Concat(p.abilities.Where(a=>!string.IsNullOrEmpty(a.activationTriggerId)).Select(a=>a.activationTriggerId)).Concat(p.abilities.SelectMany(a=>a.causeTriggerIds??new List<string>())).Concat(p.actions.SelectMany(a=>a.causeTriggerIds??new List<string>())).Concat(p.actions.Where(a=>!string.IsNullOrEmpty(a.triggerId)).Select(a=>a.triggerId)).Concat(p.flows.SelectMany(f=>(f.nodes??new List<TurnFlowNode>()).SelectMany(n=>(n.actionRules??new List<PhaseActionRule>()).SelectMany(r=>(r.beforeTriggerIds??new List<string>()).Concat(r.afterTriggerIds??new List<string>()))))).Concat(s.Triggers.Values.Where(t=>(t.responseActionIds??new List<string>()).Concat(t.causeActionIds??new List<string>()).Any(actionIds.Contains)).Select(t=>t.id)));p.triggers=s.Triggers.Values.Where(t=>triggerIds.Contains(t.id)).ToList();
                    var phaseIds=new HashSet<string>(p.abilities.Select(a=>a.phaseId).Concat(s.TurnFlows.Values.SelectMany(f=>f.nodes??new List<TurnFlowNode>()).Where(n=>(n.actionRules??new List<PhaseActionRule>()).Any(r=>actionIds.Contains(r.actionId)||(r.beforeTriggerIds??new List<string>()).Concat(r.afterTriggerIds??new List<string>()).Any(triggerIds.Contains))).Select(n=>n.id)));p.flows=p.flows.Concat(s.TurnFlows.Values.Where(f=>f.nodes.Any(n=>phaseIds.Contains(n.id)))).GroupBy(f=>f.id).Select(g=>g.First()).ToList();
                }
                File.WriteAllText(path,JsonUtility.ToJson(p,true));
            }
            EditorUtility.RevealInFinder(path);
        }
    }
}
