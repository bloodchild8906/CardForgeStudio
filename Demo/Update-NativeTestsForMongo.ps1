$p='D:\CardForgeStudio\Tests\CardForge.NativeServer.Tests\Program.cs'
$s=[IO.File]::ReadAllText($p)
$s=$s.Replace('using System.Text;','using System.Text;' + [Environment]::NewLine + 'using System.Collections.Generic;' + [Environment]::NewLine + 'using CardForge.Simulation;')
$s=$s.Replace('    var native = new NativeServerApi(shipping, temp, true);','    var persistence = new MemoryPersistence();' + [Environment]::NewLine + '    var native = new NativeServerApi(shipping, temp, true, persistence);')
$s=$s.Replace('Check(assets.Count() == 12, "art manifest contains 12 images");','Check(assets.Count() >= 240, "art manifest contains at least 240 images");')
$s=$s.Replace('    Check(Call("GET", "/api/decks").status == 401, "anonymous decks blocked");','    Check(Call("GET", "/api/decks").status == 401, "anonymous decks blocked");' + [Environment]::NewLine + '    Check(Parse(Call("GET", "/api/decks", token: token)).Count() == 4,' + [Environment]::NewLine + '        "all four starter decks created for demo account");')
$s=$s.Replace('    var restart = new NativeServerApi(shipping, temp, true);','    var restart = new NativeServerApi(shipping, temp, true, persistence);')
$s += @'

sealed class MemoryPersistence : INativePersistence
{
    private NativeState accounts = new NativeState();
    private List<SimMatch> matches = new List<SimMatch>();
    private static T Clone<T>(T data) =>
        Newtonsoft.Json.JsonConvert.DeserializeObject<T>(
            Newtonsoft.Json.JsonConvert.SerializeObject(data));
    public NativeState LoadAccounts() => Clone(accounts);
    public void SaveAccounts(NativeState state) => accounts = Clone(state);
    public List<SimMatch> LoadMatches() => Clone(matches);
    public void SaveMatches(IEnumerable<SimMatch> values) => matches = Clone(values.ToList());
    public bool CheckHealth() => true;
    public void Dispose() { }
}
'@
[IO.File]::WriteAllText($p,$s,[Text.UTF8Encoding]::new($false))
