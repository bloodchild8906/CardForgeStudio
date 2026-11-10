using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CardForge.Editor.Importing;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UIElements;

namespace CardForge.EditorUI.Screens
{
    /// <summary>Preview and field mapping are required before a source can change project data.</summary>
    public sealed class ImportWizard : EditorWindow
    {
        LiveCatalog catalog;
        string screen,sourcePath="",googleUrl="",error="";
        System.Action completed;
        List<ImportTable> tables;
        readonly List<string> mapping=new List<string>();
        int sheet,conflict;
        StudioPackage package;
        ImportPlan plan;
        VisualElement mappingHost,previewHost;
        Button importButton;
        Label report;
        UnityWebRequest request;
        bool loading,closed,matchSchemaKeys;
        public static void Open(LiveCatalog catalog,string screen,System.Action completed,string path=null)
        {
            var w=CreateInstance<ImportWizard>();w.titleContent=new GUIContent("Import into "+screen);w.minSize=new Vector2(730,600);
            w.catalog=catalog;w.screen=screen;w.completed=completed;w.ShowUtility();if(!string.IsNullOrEmpty(path))w.LoadFile(path);else w.Render();
        }
        void OnDisable(){closed=true;request?.Abort();}
        void Render()
        {
            if(catalog==null)return;rootVisualElement.Clear();rootVisualElement.style.paddingLeft=16;rootVisualElement.style.paddingRight=16;rootVisualElement.style.paddingTop=12;
            var title=new Label("Import into "+screen){style={fontSize=20,unityFontStyleAndWeight=FontStyle.Bold,marginBottom=8}};rootVisualElement.Add(title);
            rootVisualElement.Add(new HelpBox("Import XLS, XLSX, CSV, TSV, JSON or a Docs table (.docx/.html/.zip). The first row supplies column names. Excel formulas use their saved results. Dates and numbers use invariant formatting.",HelpBoxMessageType.Info));
            var fileRow=Row();rootVisualElement.Add(fileRow);fileRow.Add(new Button(ChooseFile){text="Choose local file…"});fileRow.Add(new Label(string.IsNullOrEmpty(sourcePath)?"No file selected":Path.GetFileName(sourcePath)));
            var url=new TextField("Google link"){value=googleUrl,isDelayed=true};rootVisualElement.Add(url);url.RegisterValueChangedCallback(e=>googleUrl=e.newValue);
            var fetch=new Button(async()=>await LoadGoogle()){text=loading?"Loading…":"Load Google Sheets / Docs"};fetch.SetEnabled(!loading);rootVisualElement.Add(fetch);
            rootVisualElement.Add(new HelpBox("Google Sheets: paste a sharing URL with the desired gid. Google Docs: use a table with headers (the first table is read). Links must allow access without signing in. For private files, download XLSX/CSV/JSON and choose the local file.",HelpBoxMessageType.None));
            if(!string.IsNullOrEmpty(error))rootVisualElement.Add(new HelpBox(error,HelpBoxMessageType.Error));
            if(package!=null) {
                rootVisualElement.Add(new Label("CardForge JSON package"));rootVisualElement.Add(new Label(DataTransfer.PackageSummary(package)));
                rootVisualElement.Add(new HelpBox("Stable IDs and nested definitions are preserved. Existing records are skipped. Referenced textures remain Unity asset paths; import those assets separately or use Block Builder bundles.",HelpBoxMessageType.Info));
                var match=new Toggle("Match schema fields by key and type") { value=matchSchemaKeys };match.RegisterValueChangedCallback(e=>matchSchemaKeys=e.newValue);rootVisualElement.Add(match);
                rootVisualElement.Add(new Button(()=>CommitPackage()){text="Import package"});return;
            }
            if(tables==null||tables.Count==0)return;
            var selector=new DropdownField("Worksheet",tables.Select((t,i)=>(i+1)+" · "+t.Name).ToList(),sheet);rootVisualElement.Add(selector);
            selector.RegisterValueChangedCallback(e=>{sheet=selector.choices.IndexOf(e.newValue);AutoMap();Render();});
            var policy=new DropdownField("Existing records",new List<string>{"Create new IDs","Update matching IDs","Skip matching IDs"},conflict);rootVisualElement.Add(policy);
            policy.RegisterValueChangedCallback(e=>{conflict=policy.choices.IndexOf(e.newValue);RefreshPlan();});
            var area=new ScrollView();area.style.flexGrow=1;rootVisualElement.Add(area);
            area.Add(new Label("Map source columns"));mappingHost=new VisualElement();area.Add(mappingHost);
            var fields=new List<string>{"— Ignore —"};fields.AddRange(DataTransfer.Fields(catalog,screen));
            for(int i=0;i<tables[sheet].Headers.Count;i++) {
                int column=i;var select=new DropdownField(string.IsNullOrEmpty(tables[sheet].Headers[i])?"Column "+(i+1):tables[sheet].Headers[i],fields,Math.Max(0,fields.IndexOf(mapping[i])));
                select.RegisterValueChangedCallback(e=>{mapping[column]=e.newValue;RefreshPlan();});mappingHost.Add(select);
            }
            previewHost=new VisualElement();area.Add(previewHost);
            previewHost.Add(new Label("Preview — first 10 rows"));
            foreach(var row in tables[sheet].Rows.Take(10)) {var label=new Label(string.Join("  |  ",row.Select(v=>v.Length>100?v.Substring(0,100)+"…":v)));label.style.whiteSpace=WhiteSpace.Normal;label.style.marginBottom=5;previewHost.Add(label);}
            report=new Label();report.style.whiteSpace=WhiteSpace.Normal;area.Add(report);
            importButton=new Button(Commit){text="Import records"};rootVisualElement.Add(importButton);RefreshPlan();
        }
        static VisualElement Row(){var row=new VisualElement();row.style.flexDirection=FlexDirection.Row;row.style.alignItems=Align.Center;row.style.marginTop=8;row.style.marginBottom=8;return row;}
        void ChooseFile(){var path=EditorUtility.OpenFilePanelWithFilters("Import data","",new[]{"Supported data","xls,xlsx,csv,tsv,json,docx,html,htm,zip","All files","*"});if(!string.IsNullOrEmpty(path))LoadFile(path);}
        void LoadFile(string path)
        {
            error="";package=null;tables=null;sourcePath=path;
            try {
                if(Path.GetExtension(path).Equals(".json",StringComparison.OrdinalIgnoreCase)) {
                    if(new FileInfo(path).Length>32*1024*1024)throw new FormatException("Files must be smaller than 32 MB.");
                    var text=File.ReadAllText(path);package=DataTransfer.ReadPackage(text)??DataTransfer.ReadNativePackage(screen,text);
                }
                if(package==null){tables=TabularReader.ReadFile(path);sheet=0;AutoMap();}
            }catch(Exception e){error=e.Message;}
            Render();
        }
        async Task LoadGoogle()
        {
            if(loading)return;error="";loading=true;package=null;tables=null;Render();
            try {
                string url=TabularReader.GoogleExportUrl(googleUrl,out bool doc);
                using(var download=UnityWebRequest.Get(url)) {
                    request=download;download.timeout=30;download.redirectLimit=5;var operation=download.SendWebRequest();
                    while(!operation.isDone){await Task.Delay(100);if(closed)return;}
                    if(download.result!=UnityWebRequest.Result.Success)throw new InvalidOperationException("Google export failed: "+download.error+". Check sharing access or import a local download.");
                    if(download.downloadHandler.data.Length>32*1024*1024)throw new FormatException("Google export exceeds 32 MB.");
                    string text=download.downloadHandler.text;
                    if(!doc && text.TrimStart().StartsWith("<",StringComparison.Ordinal))throw new FormatException("Google returned a sign-in page. Make the file accessible or download it locally.");
                    tables=new List<ImportTable>{doc?TabularReader.ReadGoogleDocumentBytes(download.downloadHandler.data):TabularReader.ReadDelimited(text,"Google Sheets")};sheet=0;sourcePath="Google export";AutoMap();
                }
            }catch(Exception e){if(!closed)error=e.Message;}
            finally{request=null;loading=false;if(!closed)Render();}
        }
        static string Normalize(string text)=>new string((text??"").Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
        void AutoMap()
        {
            mapping.Clear();var fields=DataTransfer.Fields(catalog,screen);var used=new HashSet<string>();
            foreach(var header in tables[sheet].Headers) {
                string normalized=Normalize(header);string field=fields.FirstOrDefault(f=>Normalize(f.StartsWith("field:")?f.Substring(6):f)==normalized);
                if(field==null&&screen=="cards") {var definition=catalog.Store.Schema.fields.FirstOrDefault(f=>Normalize(f.label)==normalized);if(definition!=null)field="field:"+definition.key;}
                if(field==null&&(normalized=="cardname"||normalized=="displayname"))field="name";
                if(field==null||!used.Add(field))field="— Ignore —";mapping.Add(field);
            }
        }
        void RefreshPlan()
        {
            plan=DataTransfer.Plan(catalog,screen,tables[sheet],mapping,conflict);
            report.text=$"{tables[sheet].Rows.Count} rows • {plan.Created} new • {plan.Updated} updates • {plan.Skipped} skipped • {plan.Errors.Count} errors\n"+string.Join("\n",plan.Errors.Take(30));
            importButton.SetEnabled(plan.Errors.Count==0&&plan.Documents.Count>0);
        }
        void Commit()
        {
            try{RefreshPlan();DataTransfer.Apply(catalog,plan);completed?.Invoke();Close();}
            catch(Exception e){error=e.Message;Render();}
        }
        void CommitPackage()
        {
            try{DataTransfer.ImportPackage(catalog,package,matchSchemaKeys);completed?.Invoke();Close();}
            catch(Exception e){error=e.Message;Render();}
        }
    }
}
