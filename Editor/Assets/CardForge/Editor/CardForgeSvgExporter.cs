using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using UnityEditor;

namespace CardForge.Editor
{
    public static class CardForgeSvgExporter
    {
        static string Number(float value)=>value.ToString("0.###",CultureInfo.InvariantCulture);
        public static void Export(LayoutDocument layout,CardDocument card,SchemaDocument schema)
        {
            if(layout==null)return;string path=EditorUtility.SaveFilePanel("Export card SVG","",card?.name ?? layout.name,"svg");if(string.IsNullOrEmpty(path))return;
            File.WriteAllText(path,Render(layout,card,schema),Encoding.UTF8);EditorUtility.RevealInFinder(path);
        }
        public static string Render(LayoutDocument layout,CardDocument card,SchemaDocument schema)
        {
            var output=new StringBuilder();
            var embeddedFonts=(layout.zones??new System.Collections.Generic.List<ZoneDefinition>())
                .Where(z=>z!=null&&!string.IsNullOrEmpty(z.fontAssetPath)&&z.fontAssetPath.StartsWith("Assets/",StringComparison.Ordinal)&&File.Exists(z.fontAssetPath))
                .Select(z=>z.fontAssetPath).Distinct().Select((path,index)=>new {path,name="CardForgeFont"+index})
                .ToDictionary(x=>x.path,x=>x.name);
            using(var xml=XmlWriter.Create(output,new XmlWriterSettings{OmitXmlDeclaration=true,Indent=true})) {
                xml.WriteStartElement("svg","http://www.w3.org/2000/svg");xml.WriteAttributeString("viewBox",$"0 0 {layout.width} {layout.height}");xml.WriteAttributeString("width",layout.width.ToString(CultureInfo.InvariantCulture));xml.WriteAttributeString("height",layout.height.ToString(CultureInfo.InvariantCulture));
                foreach(var font in embeddedFonts) {
                    string extension=Path.GetExtension(font.Key).ToLowerInvariant();
                    if(extension!=".ttf"&&extension!=".otf")continue;
                    string mime=extension==".ttf"?"font/ttf":"font/otf";
                    xml.WriteStartElement("style");
                    xml.WriteString("@font-face{font-family:'"+font.Value+"';src:url(data:"+mime+";base64,"+
                        Convert.ToBase64String(File.ReadAllBytes(font.Key))+") format('"+
                        (extension==".ttf"?"truetype":"opentype")+"');}");
                    xml.WriteEndElement();
                }
                xml.WriteStartElement("rect");Attr(xml,"width",layout.width);Attr(xml,"height",layout.height);Paint(xml,"fill",layout.backgroundColor);xml.WriteEndElement();
                Image(xml,layout.backgroundAssetPath,0,0,layout.width,layout.height);
                foreach(var source in layout.zones.OrderBy(z=>z.order)) {
                    var z=CardForgeRules.Resolve(source,card,schema,out var opacity);var ov=CardForgeData.GetOverride(card,z.id);if(!z.visible||(ov!=null&&ov.hidden))continue;
                    float x=z.x+z.offsetX+(ov?.deltaX??0),y=z.y+z.offsetY+(ov?.deltaY??0),w=z.width+(ov?.deltaWidth??0),h=z.height+(ov?.deltaHeight??0);
                    string text=z.staticText,asset=z.staticAssetPath;
                    var field=schema.fields.FirstOrDefault(f=>f.id==z.fieldId);
                    if(field!=null) {string value=CardForgeData.Value(card,field);if(z.kind==ZoneKind.Image||z.kind==ZoneKind.Icon)asset=value;else{if(field.kind==FieldKind.Number&&double.TryParse(value,NumberStyles.Float,CultureInfo.InvariantCulture,out var n))value=n.ToString("F"+Math.Max(0,Math.Min(8,field.decimalPlaces)),CultureInfo.InvariantCulture);text=(field.prefix??"")+value+(field.suffix??"");}}
                    if(!string.IsNullOrEmpty(ov?.textOverride))text=ov.textOverride;if(!string.IsNullOrEmpty(ov?.assetPathOverride))asset=ov.assetPathOverride;
                    xml.WriteStartElement("g");Attr(xml,"opacity",opacity);
                    xml.WriteStartElement("rect");Attr(xml,"x",x);Attr(xml,"y",y);Attr(xml,"width",w);Attr(xml,"height",h);Paint(xml,"fill",z.backgroundColor);xml.WriteEndElement();
                    if(z.kind==ZoneKind.Image||z.kind==ZoneKind.Icon)Image(xml,asset,x,y,w,h);
                    else {
                        float font=ov!=null&&ov.fontSize>0?ov.fontSize:z.fontSize;string align=z.alignment??"MiddleCenter";
                        bool left=align.EndsWith("Left"),right=align.EndsWith("Right");float tx=left?x:right?x+w:x+w/2;
                        var lines=Wrap(text??"",Math.Max(1,(int)(w/Math.Max(1,font*.55f))));float lineHeight=font*1.2f;
                        float ty=align.StartsWith("Upper")?y+font:align.StartsWith("Lower")?y+h-(lines.Length-1)*lineHeight:y+(h-lines.Length*lineHeight)/2+font;
                        xml.WriteStartElement("text");Attr(xml,"x",tx);Attr(xml,"y",ty);Attr(xml,"font-size",font);xml.WriteAttributeString("font-family",embeddedFonts.TryGetValue(z.fontAssetPath??"",out var face)?face:"Arial, sans-serif");xml.WriteAttributeString("font-weight",z.bold?"bold":"normal");xml.WriteAttributeString("text-anchor",left?"start":right?"end":"middle");Paint(xml,"fill",!string.IsNullOrEmpty(ov?.color)?ov.color:z.color);
                        foreach(var line in lines){xml.WriteStartElement("tspan");Attr(xml,"x",tx);Attr(xml,"y",ty);xml.WriteString(line);xml.WriteEndElement();ty+=lineHeight;}xml.WriteEndElement();
                    }
                    xml.WriteEndElement();
                }
                xml.WriteEndElement();
            }
            return output.ToString();
        }
        static string[] Wrap(string text,int columns)
        {
            var lines=new System.Collections.Generic.List<string>();
            foreach(var paragraph in text.Replace("\r","").Split('\n')) {
                string line="";foreach(var word in paragraph.Split(' ')){if(line.Length>0&&line.Length+word.Length+1>columns){lines.Add(line);line="";}line+=(line.Length==0?"":" ")+word;}lines.Add(line);
            }return lines.ToArray();
        }
        static void Attr(XmlWriter xml,string key,float value)=>xml.WriteAttributeString(key,Number(value));
        static void Paint(XmlWriter xml,string key,string color)
        {
            if(string.IsNullOrEmpty(color)){xml.WriteAttributeString(key,"none");return;}
            if(color.Length==9&&color[0]=='#') {xml.WriteAttributeString(key,color.Substring(0,7));if(byte.TryParse(color.Substring(7,2),NumberStyles.HexNumber,CultureInfo.InvariantCulture,out var alpha))Attr(xml,key+"-opacity",alpha/255f);}
            else xml.WriteAttributeString(key,color);
        }
        static void Image(XmlWriter xml,string asset,float x,float y,float width,float height)
        {
            if(string.IsNullOrEmpty(asset)||!asset.StartsWith("Assets/",StringComparison.Ordinal)||!File.Exists(asset))return;
            string ext=Path.GetExtension(asset).ToLowerInvariant();if(ext!=".png"&&ext!=".jpg"&&ext!=".jpeg"&&ext!=".svg")return;
            string mime=ext==".png"?"image/png":ext==".svg"?"image/svg+xml":"image/jpeg";
            xml.WriteStartElement("image");Attr(xml,"x",x);Attr(xml,"y",y);Attr(xml,"width",width);Attr(xml,"height",height);xml.WriteAttributeString("preserveAspectRatio","xMidYMid slice");xml.WriteAttributeString("href","data:"+mime+";base64,"+Convert.ToBase64String(File.ReadAllBytes(asset)));xml.WriteEndElement();
        }
    }
}
