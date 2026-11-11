using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace CardForge.Editor
{
    #if UNITY_6000_5_OR_NEWER
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    #endif
    public static class CardForgeArtwork
    {
        const string Root="Assets/CardForge/Editor/UI/Artwork/";
        public static bool Enabled { get=>EditorPrefs.GetBool("CardForge.Artwork",true); set=>EditorPrefs.SetBool("CardForge.Artwork",value); }
        static readonly Dictionary<string,string> Files=new Dictionary<string,string> {
            {"Layouts","LayoutDesigner"},{"layouts","LayoutDesigner"},{"Database","CardDatabase"},{"cards","CardDatabase"},
            {"Blocks","BlockBuilder"},{"blocks","BlockBuilder"},{"TurnFlow","TurnFlow"},{"turnflow","TurnFlow"},
            {"Actions","ActionDesigner"},{"actions","ActionDesigner"},{"Abilities","AbilityDesigner"},{"abilities","AbilityDesigner"},
            {"Icons","IconWorkshop"},{"icons","IconWorkshop"}
        };
        public static Texture2D Load(string file)=>AssetDatabase.LoadAssetAtPath<Texture2D>(Root+file);
        public static Texture2D Workspace(string key)=>Files.TryGetValue(key,out var file)?Load("CardForge_"+file+"_BG.png"):Load("CardForge_UIBackground.png");
        public static Image BrandMark() {var image=new Image{image=Load("CardForge_LogoMark.png"),scaleMode=ScaleMode.ScaleToFit,pickingMode=PickingMode.Ignore};image.AddToClassList("brand-mark");return image;}
        public static void AddWorkspaceBackdrop(VisualElement root,string key)
        {
            if(!Enabled)return;var texture=Workspace(key);if(texture==null)return;
            var image=new Image{image=texture,scaleMode=ScaleMode.ScaleAndCrop,pickingMode=PickingMode.Ignore};image.AddToClassList("workspace-artwork");root.Insert(0,image);
        }
        public static VisualElement Banner(string screen,string title)
        {
            var banner=new VisualElement();banner.AddToClassList("cf-art-banner");
            if(Enabled){var art=new Image{image=Workspace(screen),scaleMode=ScaleMode.ScaleAndCrop,pickingMode=PickingMode.Ignore};art.AddToClassList("cf-art-banner__image");banner.Add(art);}
            var tint=new VisualElement{pickingMode=PickingMode.Ignore};tint.AddToClassList("cf-art-banner__tint");banner.Add(tint);
            var kicker=new Label("STUDIO  /  LIBRARY"){pickingMode=PickingMode.Ignore};kicker.AddToClassList("cf-art-banner__kicker");banner.Add(kicker);
            var label=new Label(title){pickingMode=PickingMode.Ignore};label.AddToClassList("cf-art-banner__title");banner.Add(label);
            var subtitle=new Label(screen=="layouts"?"Shape every detail. Build reusable fronts, backs and variants.":screen=="cards"?"Bring your cards together. Edit, import and preview live data.":screen=="blocks"?"Build editions, manage reprints and export your game assets.":screen=="turnflow"?"Map phases, checks and resolution with connected visual flows.":screen=="icons"?"Craft reusable symbols with editable vector layers.":"Define reusable rules and connect them to your game."){pickingMode=PickingMode.Ignore};subtitle.AddToClassList("cf-art-banner__subtitle");banner.Add(subtitle);
            return banner;
        }
    }
    /// <summary>Applies editor-only UI texture settings to the supplied professional art pack.</summary>
    public sealed class CardForgeArtworkImporter : AssetPostprocessor
    {
        void OnPreprocessTexture()
        {
            if(!assetPath.StartsWith("Assets/CardForge/Editor/UI/Artwork/",System.StringComparison.Ordinal))return;
            var importer=(TextureImporter)assetImporter;importer.textureType=TextureImporterType.Default;importer.sRGBTexture=true;
            importer.alphaIsTransparency=true;importer.mipmapEnabled=false;importer.wrapMode=TextureWrapMode.Clamp;
            importer.maxTextureSize=assetPath.Contains("_BG")||assetPath.Contains("UIBackground")?2048:512;
            importer.textureCompression=TextureImporterCompression.Compressed;
        }
    }
}
