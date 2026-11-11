using System;
using System.Collections.Generic;
using System.Linq;
using CardForge.Editor;
using CardForge.EditorUI.Grid;
using UnityEngine.UIElements;

namespace CardForge.EditorUI.Screens
{
    /// <summary>All library rows are projections of the authoritative JSON store.</summary>
    public sealed class LiveCatalog
    {
        public readonly CardForgeStore Store = new CardForgeStore();
        public string BlockId;
        public BlockDocument Block => BlockId != null && Store.Blocks.TryGetValue(BlockId,out var b) ? b : null;
        public LiveCatalog() { Reload(); }
        public void Reload() { Store.Reload(); if(Block==null) BlockId=Store.Blocks.Values.OrderBy(b=>b.name).Select(b=>b.id).FirstOrDefault(); }
        public IReadOnlyList<GridScreenDefinition> Definitions()
        {
            var defs = new[] {
                Define("layouts", "01 Layouts", "Layouts", "layout", new[]{Text("type","Side"), Text("zones","Zones"),Text("usedBy","Cards"),Pill("status","Status")}, "Open designer"),
                Define("cards", "02 Cards", "Card Database", "card", new[]{Text("number","Number"),Text("type","Type"),Text("layout","Front"),Text("back","Back"),Text("keywords","Keywords"),Text("abilities","Abilities"),Pill("status","Status")}, "Edit card"),
                Define("blocks", "03 Blocks", "Blocks & Editions", "block", new[]{Text("code","Code"),Text("cards","Cards"),Text("layouts","Layouts"),Pill("status","Validation")}, "Edit block"),
                Define("turnflow", "04 Turn Flow", "Turn Flows", "flow", new[]{Text("nodes","Phases"),Text("links","Connections"),Pill("status","Validation")}, "Open designer"),
                Define("triggers", "05 Triggers", "Triggers", "trigger", new[]{Text("event","Event"),Text("rules","Rules"),Text("responses","Responses")}, "Edit trigger"),
                Define("actions", "06 Actions", "Actions", "action", new[]{Text("trigger","Viability trigger"),Text("outputs","Trigger outputs"),Text("graph","Graph"),Text("schedules","Phase schedules"),Text("usedBy","Abilities")}, "Edit action"),
                Define("abilities", "07 Abilities", "Abilities", "ability", new[]{Text("binding","Card binding"),Text("bundles","Keyword bundles"),Text("trigger","Activation trigger"),Text("outputs","Trigger outputs"),Text("action","Action"),Text("phase","Phase"),Pill("status","References")}, "Edit ability"),
                Define("icons", "08 Icons", "Icon Workshop", "icon", new[]{Text("layers","Layers"),Text("asset","PNG asset")}, "Open designer")
            };
            foreach(var d in defs) {
                string screen=d.Id; d.RowSource=new LiveRows(()=>Rows(screen));
                d.SummaryStyle=SummaryStyle.None;
                d.BuildPreview=row=>Preview(screen,row.Id);
            }
            return defs;
        }
        static GridColumnDefinition Text(string id,string title)=>GridColumnDefinition.Text(id,title,110);
        static GridColumnDefinition Pill(string id,string title)=>GridColumnDefinition.Pill(id,title,150);
        static GridCell IssueCell(int issueCount,string readyText)
        {
            if(issueCount==0) return GridCell.Ok(readyText);
            return GridCell.Warning(issueCount==1 ? "1 issue" : issueCount+" issues");
        }
        static GridScreenDefinition Define(string id,string tab,string title,string noun,GridColumnDefinition[] columns,string edit)
        {
            return new GridScreenDefinition { Id=id,TabLabel=tab,Title=title,ItemNoun=noun,AddButtonLabel="+ New "+noun,
                Columns=new[]{GridColumnDefinition.Primary("Name")}.Concat(columns).ToArray(),
                FilterGroups=columns.Where(c=>c.Id=="status"||c.Id=="type").Select(c=>new FilterGroupDefinition(c.Header,c.Id)).ToArray(),
                InspectorActions=id=="blocks"?new[]{edit,"Activate","Validate","Build bundle","Duplicate","Delete"}:new[]{edit,"Duplicate","Export","Delete"} };
        }
        public IEnumerable<GridRowViewModel> Rows(string screen)
        {
            switch(screen) {
                case "layouts": return (Block?.layoutIds ?? new List<string>()).Where(Store.Layouts.ContainsKey).Select(id=>Store.Layouts[id]).Select(l=>GridRowViewModel.Create(l.id,l.name,
                    ("type",l.isBack?"Back":"Front"),("zones",l.zones.Count.ToString()),("usedBy",Store.Cards.Values.Count(c=>c.layoutId==l.id||c.backLayoutId==l.id).ToString()),
                    ("status",l.width>0&&l.height>0?GridCell.Ok("Ready"):GridCell.Danger("Invalid size"))));
                case "cards": return (Block?.cardIds ?? new List<string>()).Where(Store.Cards.ContainsKey).Select(id=>Store.Cards[id]).Select(c=>GridRowViewModel.Create(c.id,c.name,
                    ("number",c.cardNumber),("type",c.cardType),("layout",LayoutName(c.layoutId)),("back",LayoutName(c.backLayoutId)),("keywords",(c.keywordIds??new List<string>()).Count.ToString()),("abilities",(c.abilityIds??new List<string>()).Count.ToString()),
                    ("status",Store.Layouts.ContainsKey(c.layoutId ?? "")?GridCell.Ok("Ready"):GridCell.Danger("Missing front"))));
                case "blocks": return Store.Blocks.Values.Select(b=>GridRowViewModel.Create(b.id,b.name,("code",b.code),("cards",b.cardIds.Count.ToString()),("layouts",b.layoutIds.Count.ToString()),
                    ("status",IssueCell(Store.Validate(b).Count,"Ready"))));
                case "turnflow": return Store.TurnFlows.Values.Select(f=>GridRowViewModel.Create(f.id,f.name,("nodes",f.nodes.Count.ToString()),("links",f.links.Count.ToString()),
                    ("status",IssueCell(Store.ValidateFlow(f).Count,"Valid"))));
                case "triggers": return Store.Triggers.Values.Select(t=>GridRowViewModel.Create(t.id,t.name,("event",t.eventType),("rules",(t.rules??new List<TriggerRule>()).Count.ToString()),("responses",(t.responseActionIds??new List<string>()).Count.ToString())));
                case "actions": return Store.Actions.Values.Select(a=>GridRowViewModel.Create(a.id,a.name,("trigger",TriggerName(a.triggerId)),("outputs",(a.causeTriggerIds??new List<string>()).Count.ToString()),("graph",((a.graphNodes??new List<ResolutionGraphNode>()).Count+" steps")),("schedules",ActionScheduleCount(a.id).ToString()),("usedBy",Store.Abilities.Values.Count(x=>x.actionId==a.id).ToString())));
                case "abilities": return Store.Abilities.Values.Select(a=>GridRowViewModel.Create(a.id,a.name,("binding",string.IsNullOrEmpty(a.cardId)?"Reusable":(Store.Cards.TryGetValue(a.cardId,out var card)?card.name:"Missing card")),("bundles",Store.Keywords.Values.Count(keyword=>(keyword.abilityIds??new List<string>()).Contains(a.id)).ToString()),
                    ("trigger",TriggerName(a.activationTriggerId)),("outputs",(a.causeTriggerIds??new List<string>()).Count.ToString()),
                    ("action",Store.Actions.TryGetValue(a.actionId ?? "",out var ac)?ac.name:"—"),
                    ("phase",Store.TurnFlows.Values.SelectMany(f=>f.nodes).FirstOrDefault(n=>n.id==a.phaseId)?.name ?? "—"),("priority",a.priority.ToString()),
                    ("status",(!string.IsNullOrEmpty(a.actionId)&&!Store.Actions.ContainsKey(a.actionId))||(!string.IsNullOrEmpty(a.phaseId)&&!Store.TurnFlows.Values.Any(f=>f.nodes.Any(n=>n.id==a.phaseId)))?GridCell.Danger("Broken link"):GridCell.Ok("Ready"))));
                case "icons": return Store.Icons.Values.Select(i=>GridRowViewModel.Create(i.id,i.name,("layers",i.layers.Count.ToString()),("asset",i.assetPath)));
                default: return Enumerable.Empty<GridRowViewModel>();
            }
        }
        string LayoutName(string id)=>Store.Layouts.TryGetValue(id ?? "",out var l)?l.name:"—";
        string TriggerName(string id)=>Store.Triggers.TryGetValue(id ?? "",out var trigger)?trigger.name:"No trigger gate";
        int ActionScheduleCount(string id)=>Store.TurnFlows.Values.SelectMany(flow=>flow.nodes??new List<TurnFlowNode>()).Sum(node=>(node.actionRules??new List<PhaseActionRule>()).Count(rule=>rule!=null&&rule.actionId==id));
        VisualElement Preview(string screen,string id)
        {
            var box = new VisualElement();
            box.AddToClassList("cf-live-preview");
            CardDocument card=null; LayoutDocument layout=null;
            if(screen=="cards" && Store.Cards.TryGetValue(id,out card)) Store.Layouts.TryGetValue(card.layoutId ?? "",out layout);
            if(screen=="layouts") Store.Layouts.TryGetValue(id,out layout);
            if(layout!=null) {
                var canvas=new CardForgeCanvas { ShowGrid=false, CompactPreview=true, pickingMode=PickingMode.Ignore };
                canvas.focusable=false; box.Add(canvas);
                box.RegisterCallback<GeometryChangedEvent>(e => {
                    if(e.newRect.width < 20 || e.newRect.height < 20) return;
                    float scale=UnityEngine.Mathf.Min((e.newRect.width-24)/UnityEngine.Mathf.Max(1,layout.width),(e.newRect.height-24)/UnityEngine.Mathf.Max(1,layout.height));
                    canvas.Draw(layout,card,Store.Schema,Store.Icons,scale,false,null,null,null);
                    foreach(var child in canvas.Query<VisualElement>().ToList()) { child.pickingMode=PickingMode.Ignore; child.focusable=false; }
                });
            } else if(screen=="icons" && Store.Icons.TryGetValue(id,out var icon)) {
                var image=new Image { scaleMode=UnityEngine.ScaleMode.ScaleToFit, pickingMode=PickingMode.Ignore };
                image.AddToClassList("cf-icon-preview"); box.Add(image);
                UnityEngine.Texture2D generated=null;
                image.RegisterCallback<AttachToPanelEvent>(_ => {
                    if (icon.layers.Count == 0 && !string.IsNullOrEmpty(icon.assetPath))
                        image.image = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Texture2D>(icon.assetPath);
                    else { if(generated==null) generated=CardForgeIconPainter.Render(icon); image.image=generated; }
                });
                image.RegisterCallback<DetachFromPanelEvent>(_ => { image.image=null; if(generated!=null) UnityEngine.Object.DestroyImmediate(generated); generated=null; });
            } else {
                box.Add(new Label(screen=="cards" ? "Assign a front layout to see a preview" : "No preview available") { pickingMode=PickingMode.Ignore });
            }
            return box;
        }
        public string Create(string screen)
        {
            switch(screen) {
                case "blocks": var b=Store.CreateBlock("New block","NEW");BlockId=b.id;return b.id;
                case "layouts": RequireBlock();return Store.CreateLayout(Block,"New front",false).id;
                case "cards": RequireBlock();var front=Block.layoutIds.Where(Store.Layouts.ContainsKey).Select(id=>Store.Layouts[id]).FirstOrDefault(l=>!l.isBack);
                    if(front==null) throw new InvalidOperationException("Create a front layout in this block first.");return Store.CreateCard(Block,front.id).id;
                case "turnflow":var f=new TurnFlowDocument { name="New turn flow" };Store.SaveTurnFlow(f);return f.id;
                case "triggers":var t=new TriggerDocument();Store.SaveTrigger(t);return t.id;
                case "actions":var a=new ActionDocument();Store.SaveAction(a);return a.id;
                case "abilities":var ab=new AbilityDocument();Store.SaveAbility(ab);return ab.id;
                case "icons":var i=new IconDocument();i.layers.Add(new IconLayer());Store.SaveIcon(i);return i.id;
            }
            return null;
        }
        void RequireBlock() { if(Block==null) throw new InvalidOperationException("Create or activate a block first."); }
        public void Duplicate(string screen,string id)
        {
            switch(screen) {
                case "blocks": var b=Store.Blocks[id];Store.CloneBlock(b,b.name+" copy",b.code+"-COPY",true);break;
                case "layouts": RequireBlock();var l=Store.ImportLayout(Block,Store.Layouts[id]);l.name=Store.Layouts[id].name+" copy";Store.SaveLayout(BlockId,l);break;
                case "cards":RequireBlock();var c=CardForgeData.Copy(Store.Cards[id]);c.id=CardForgeData.NewId();c.name+=" copy";c.cardNumber+="-COPY";Block.cardIds.Add(c.id);Store.SaveCard(BlockId,c);Store.SaveBlock(Block);break;
                case "turnflow":var f=CardForgeData.Copy(Store.TurnFlows[id]);f.id=CardForgeData.NewId();f.name+=" copy";var map=new Dictionary<string,string>();foreach(var n in f.nodes){var old=n.id;n.id=CardForgeData.NewId();map[old]=n.id;}foreach(var e in f.links){e.from=map[e.from];e.to=map[e.to];}Store.SaveTurnFlow(f);break;
                case "triggers":var t=CardForgeData.Copy(Store.Triggers[id]);t.id=CardForgeData.NewId();t.name+=" copy";Store.SaveTrigger(t);break;
                case "actions":var a=CardForgeData.Copy(Store.Actions[id]);a.id=CardForgeData.NewId();a.name+=" copy";Store.SaveAction(a);break;
                case "abilities":var ab=CardForgeData.Copy(Store.Abilities[id]);ab.id=CardForgeData.NewId();ab.name+=" copy";Store.SaveAbility(ab);break;
                case "icons":var i=CardForgeData.Copy(Store.Icons[id]);i.id=CardForgeData.NewId();i.name+=" copy";i.assetPath="";Store.SaveIcon(i);break;
            }
        }
        public void Delete(string screen,string id)
        {
            switch(screen) {
                case "blocks":Store.DeleteBlock(id);break;case "layouts":Store.DeleteLayout(Block,Store.Layouts[id]);break;
                case "cards":Store.DeleteCard(Block,Store.Cards[id]);break;case "actions":Store.DeleteAction(id);break;
                case "abilities":Store.DeleteAbility(id);break;case "turnflow":Store.DeleteFlow(id);break;case "triggers":Store.DeleteTrigger(id);break;case "icons":Store.DeleteIcon(id);break;
            }
        }
        sealed class LiveRows : IGridRowSource
        {
            readonly Func<IEnumerable<GridRowViewModel>> load;
            public LiveRows(Func<IEnumerable<GridRowViewModel>> load) { this.load=load; }
            public IReadOnlyList<GridRowViewModel> LoadRows()=>load().OrderBy(r=>r.DisplayName,StringComparer.OrdinalIgnoreCase).ToList();
        }
    }
}
