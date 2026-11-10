using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Xml.Linq;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using ExcelDataReader;

namespace CardForge.Editor.Importing
{
    public sealed class ImportTable
    {
        public string Name;
        public List<string> Headers = new List<string>();
        public List<List<string>> Rows = new List<List<string>>();
        public static ImportTable FromRows(string name,List<List<string>> rows)
        {
            while(rows.Count>0 && rows[0].All(string.IsNullOrWhiteSpace)) rows.RemoveAt(0);
            if(rows.Count==0) throw new FormatException("The source is empty.");
            var t=new ImportTable { Name=name,Headers=rows[0].Select(h=>(h??"").Trim().TrimStart('\uFEFF')).ToList() };
            if(t.Headers.Count>256) throw new FormatException("A maximum of 256 columns is supported.");
            foreach(var row in rows.Skip(1).Where(r=>r.Any(v=>!string.IsNullOrWhiteSpace(v)))) {
                if(row.Count>t.Headers.Count) throw new FormatException("A row contains more columns than the header. Check the delimiter.");
                while(row.Count<t.Headers.Count) row.Add("");t.Rows.Add(row);
            }
            if(t.Rows.Count>20000) throw new FormatException("Import at most 20,000 records at a time.");
            return t;
        }
    }
    public static class TabularReader
    {
        static void EnsureExcelEncodings()
        {
            // Unity 6 Mono supplies these encodings; newer .NET editors require an optional provider.
            try { Encoding.GetEncoding(1252); return; }
            catch(NotSupportedException) { }
            catch(ArgumentException) { }

            // Resolve dynamically so older Unity editors do not acquire a CodePages assembly dependency.
            var providerType=Type.GetType("System.Text.CodePagesEncodingProvider, System.Text.Encoding.CodePages",false);
            var instance=providerType?.GetProperty("Instance",System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.Static)?.GetValue(null,null);
            if(instance is EncodingProvider provider) Encoding.RegisterProvider(provider);
            try { Encoding.GetEncoding(1252); }
            catch(Exception error) when(error is NotSupportedException || error is ArgumentException)
            {
                throw new NotSupportedException("Excel import requires Windows-1252 encoding support. This editor runtime needs the System.Text.Encoding.CodePages assembly. Add it as an editor-only dependency and retry the import.",error);
            }
        }
        public static List<ImportTable> ReadFile(string path)
        {
            if(new FileInfo(path).Length>32*1024*1024) throw new FormatException("Files must be smaller than 32 MB.");
            string ext=Path.GetExtension(path).ToLowerInvariant();
            if(ext==".xls" || ext==".xlsx") {
                EnsureExcelEncodings();
                var tables=new List<ImportTable>();
                using(var stream=File.Open(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite))
                using(var reader=ExcelReaderFactory.CreateReader(stream)) {
                    do {
                        if(reader.FieldCount>256)throw new FormatException("A worksheet exceeds 256 columns.");
                        var rows=new List<List<string>>();
                        while(reader.Read()) {
                            if(rows.Count>20000)throw new FormatException("A worksheet exceeds 20,000 records.");
                            var row=new List<string>();
                            for(int i=0;i<reader.FieldCount;i++) row.Add(Convert.ToString(reader.GetValue(i),CultureInfo.InvariantCulture)??"");
                            rows.Add(row);
                        }
                        if(rows.Any(r=>r.Any(v=>!string.IsNullOrWhiteSpace(v))))tables.Add(ImportTable.FromRows(reader.Name,rows));
                    }while(reader.NextResult());
                }
                if(tables.Count==0)throw new FormatException("The workbook has no data.");return tables;
            }
            if(ext==".docx"||ext==".zip")return new List<ImportTable>{ReadGoogleDocumentBytes(File.ReadAllBytes(path))};
            var text=File.ReadAllText(path);
            if(ext==".html"||ext==".htm")return new List<ImportTable>{ReadGoogleDocumentHtml(text)};
            return new List<ImportTable>{ext==".json"?ReadJson(text):ReadDelimited(text,Path.GetFileName(path))};
        }
        public static ImportTable ReadDelimited(string text,string name="Table")
        {
            if(string.IsNullOrWhiteSpace(text)) throw new FormatException("The source is empty.");
            // Count delimiters on the first logical record, outside quoted cells.
            var counts=new Dictionary<char,int>{{',',0},{';',0},{'\t',0}};bool quoted=false;
            for(int i=0;i<text.Length;i++) {char c=text[i];if(c=='"'){if(quoted&&i+1<text.Length&&text[i+1]=='"'){i++;continue;}quoted=!quoted;}if(!quoted){if(c=='\r'||c=='\n')break;if(counts.ContainsKey(c))counts[c]++;}}
            char delimiter=counts.OrderByDescending(k=>k.Value).First().Key;
            return ImportTable.FromRows(name,ParseDelimited(text,delimiter));
        }
        public static List<List<string>> ParseDelimited(string text,char delimiter)
        {
            var rows=new List<List<string>>();var row=new List<string>();var cell=new StringBuilder();bool quoted=false,closed=false;
            for(int i=0;i<text.Length;i++) {
                char c=text[i];
                if(quoted) { if(c=='"') { if(i+1<text.Length&&text[i+1]=='"'){cell.Append('"');i++;}else{quoted=false;closed=true;} }else cell.Append(c);continue; }
                if(c=='"' && cell.Length==0&&!closed){quoted=true;continue;}
                if(c==delimiter){row.Add(cell.ToString());cell.Clear();closed=false;continue;}
                if(c=='\r'||c=='\n'){row.Add(cell.ToString());cell.Clear();rows.Add(row);row=new List<string>();closed=false;if(c=='\r'&&i+1<text.Length&&text[i+1]=='\n')i++;continue;}
                if(closed&&!char.IsWhiteSpace(c))throw new FormatException("Unexpected text after a quoted cell.");
                if(!closed)cell.Append(c);
            }
            if(quoted)throw new FormatException("A quoted cell is missing its closing quote.");
            if(cell.Length>0||row.Count>0||closed){row.Add(cell.ToString());rows.Add(row);}return rows;
        }
        public static ImportTable ReadJson(string text)
        {
            var value=JsonReader.Parse(text);
            var objects=value is List<object> list?list:value is Dictionary<string,object> obj?new List<object>{obj}:throw new FormatException("JSON must contain an object or an array of objects.");
            if(objects.Count==0)throw new FormatException("The JSON array is empty.");
            var headers=new List<string>();
            foreach(var item in objects) {
                if(!(item is Dictionary<string,object> record))throw new FormatException("Each JSON record must be an object.");
                foreach(var key in record.Keys)if(!headers.Contains(key))headers.Add(key);
            }
            var rows=new List<List<string>>{headers};
            foreach(Dictionary<string,object> record in objects)rows.Add(headers.Select(h=>record.TryGetValue(h,out var v)?Scalar(v):"").ToList());
            return ImportTable.FromRows("JSON",rows);
        }
        static string Scalar(object value)
        {
            if(value is Dictionary<string,object> || value is List<object>)throw new FormatException("Nested JSON documents must be imported as a CardForge JSON package. Flat JSON records support scalar fields.");
            return value is bool b?(b?"true":"false"):Convert.ToString(value,CultureInfo.InvariantCulture)??"";
        }
        public static ImportTable ReadGoogleDocumentBytes(byte[] bytes)
        {
            if(bytes.Length>=2&&bytes[0]=='P'&&bytes[1]=='K') {
                using(var stream=new MemoryStream(bytes))using(var zip=new ZipArchive(stream,ZipArchiveMode.Read)) {
                    var word=zip.GetEntry("word/document.xml");
                    var entry=word??zip.Entries.FirstOrDefault(e=>e.FullName.EndsWith(".html",StringComparison.OrdinalIgnoreCase)||e.FullName.EndsWith(".htm",StringComparison.OrdinalIgnoreCase));
                    if(entry==null||entry.Length>32*1024*1024)throw new FormatException("The document archive needs an HTML or DOCX document smaller than 32 MB.");
                    string text;using(var reader=new StreamReader(entry.Open()))text=reader.ReadToEnd();
                    if(word==null)return ReadGoogleDocumentHtml(text);
                    XNamespace w="http://schemas.openxmlformats.org/wordprocessingml/2006/main";
                    var table=XDocument.Parse(text).Descendants(w+"tbl").FirstOrDefault();
                    if(table==null)throw new FormatException("The document needs a table with headers.");
                    var rows=table.Elements(w+"tr").Select(tr=>tr.Elements(w+"tc").Select(tc=>string.Join("\n",tc.Elements(w+"p").Select(p=>string.Concat(p.Descendants(w+"t").Select(t=>t.Value))))).ToList()).ToList();
                    return ImportTable.FromRows("Document — first table",rows);
                }
            }
            return ReadGoogleDocumentHtml(Encoding.UTF8.GetString(bytes));
        }
        public static ImportTable ReadGoogleDocumentHtml(string html)
        {
            var tables=Regex.Matches(html,@"<table\b[^>]*>(.*?)</table>",RegexOptions.Singleline|RegexOptions.IgnoreCase);
            var rows=new List<List<string>>();
            if(tables.Count==0)throw new FormatException("The Google Doc needs a table with column headers in its first row. Alternatively, download a CSV/JSON file and import it.");
            foreach(Match tr in Regex.Matches(tables[0].Groups[1].Value,@"<tr\b[^>]*>(.*?)</tr>",RegexOptions.Singleline|RegexOptions.IgnoreCase)) {
                var cells=new List<string>();
                foreach(Match td in Regex.Matches(tr.Groups[1].Value,@"<t[dh]\b[^>]*>(.*?)</t[dh]>",RegexOptions.Singleline|RegexOptions.IgnoreCase)) {
                    string cell=Regex.Replace(td.Groups[1].Value,@"<br\s*/?>","\n",RegexOptions.IgnoreCase);
                    cell=Regex.Replace(cell,@"</p\s*>","\n",RegexOptions.IgnoreCase);
                    cells.Add(System.Net.WebUtility.HtmlDecode(Regex.Replace(cell,"<[^>]+>","")).Trim());
                }
                if(cells.Count>0)rows.Add(cells);
            }
            return ImportTable.FromRows("Google Docs — first table",rows);
        }
        public static string GoogleExportUrl(string input,out bool document)
        {
            if(!Uri.TryCreate(input,UriKind.Absolute,out var uri)||uri.Scheme!="https"||uri.Host!="docs.google.com")throw new FormatException("Use an HTTPS docs.google.com spreadsheet or document link.");
            var match=Regex.Match(uri.AbsolutePath,@"^/(spreadsheets|document)/d/([A-Za-z0-9_-]+)(?:/|$)");
            if(!match.Success)throw new FormatException("Use a standard Google Sheets or Google Docs sharing link.");
            document=match.Groups[1].Value=="document";string id=match.Groups[2].Value;
            if(document)return "https://docs.google.com/document/d/"+id+"/export?format=html";
            var gid=Regex.Match(uri.Query+uri.Fragment,@"(?:[?&#]|^)gid=(\d+)");
            return "https://docs.google.com/spreadsheets/d/"+id+"/export?format=csv&gid="+(gid.Success?gid.Groups[1].Value:"0");
        }
        public static string Csv(IEnumerable<IEnumerable<string>> rows)
        {
            return string.Join("\r\n",rows.Select(row=>string.Join(",",row.Select(value=>"\""+(value??"").Replace("\"","\"\"")+"\""))))+"\r\n";
        }
    }
    // Small strict JSON reader, kept independent of JsonUtility for arbitrary spreadsheet column names.
    public sealed class JsonReader
    {
        readonly string text;int at,depth;
        JsonReader(string text){this.text=text;}
        public static object Parse(string text){var r=new JsonReader(text.TrimStart('\uFEFF'));var result=r.Value();r.Space();if(r.at!=r.text.Length)throw new FormatException("Unexpected content after JSON.");return result;}
        void Space(){while(at<text.Length&&char.IsWhiteSpace(text[at]))at++;}
        char Peek(){Space();if(at>=text.Length)throw new FormatException("Unexpected end of JSON.");return text[at];}
        object Value()
        {
            if(++depth>64)throw new FormatException("JSON nesting exceeds 64 levels.");
            try {
                char c=Peek();if(c=='"')return String();
                if(c=='{'){at++;var obj=new Dictionary<string,object>();if(Peek()=='}'){at++;return obj;}while(true){if(Peek()!='"')throw new FormatException("Expected a JSON key.");string key=String();if(Peek()!=':')throw new FormatException("Expected ':'.");at++;if(obj.ContainsKey(key))throw new FormatException("Duplicate JSON key: "+key);obj.Add(key,Value());char end=Peek();at++;if(end=='}')break;if(end!=',')throw new FormatException("Expected ',' or '}'.");}return obj;}
                if(c=='['){at++;var list=new List<object>();if(Peek()==']'){at++;return list;}while(true){list.Add(Value());char end=Peek();at++;if(end==']')break;if(end!=',')throw new FormatException("Expected ',' or ']'.");}return list;}
                foreach(var token in new[]{"true","false","null"})if(text.Substring(at).StartsWith(token,StringComparison.Ordinal)){at+=token.Length;return token=="null"?null:(object)(token=="true");}
                int start=at;while(at<text.Length&&"-+0123456789.eE".IndexOf(text[at])>=0)at++;
                string raw=text.Substring(start,at-start);
                if(!Regex.IsMatch(raw,@"^-?(0|[1-9]\d*)(\.\d+)?([eE][+-]?\d+)?$"))throw new FormatException("Invalid JSON value.");
                return raw;
            }finally{depth--;}
        }
        string String()
        {
            at++;var b=new StringBuilder();while(at<text.Length){char c=text[at++];if(c=='"')return b.ToString();if(c<' ')throw new FormatException("Unescaped control character in JSON string.");if(c!='\\'){b.Append(c);continue;}if(at>=text.Length)break;c=text[at++];switch(c){case '"':case '\\':case '/':b.Append(c);break;case 'n':b.Append('\n');break;case 'r':b.Append('\r');break;case 't':b.Append('\t');break;case 'b':b.Append('\b');break;case 'f':b.Append('\f');break;case 'u':if(at+4>text.Length||!ushort.TryParse(text.Substring(at,4),NumberStyles.HexNumber,CultureInfo.InvariantCulture,out var code))throw new FormatException("Invalid Unicode escape.");b.Append((char)code);at+=4;break;default:throw new FormatException("Invalid JSON escape.");}}throw new FormatException("Unterminated JSON string.");
        }
    }
}
