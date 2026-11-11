$ErrorActionPreference='Stop'
$p='D:\CardForgeStudio\Assets\CardForgeStudio\CardForgeLearn\Editor\CardForgePlayWorkflow.cs'
$s=[IO.File]::ReadAllText($p,[Text.Encoding]::UTF8)
$start=$s.IndexOf('        public static string GenerateSample()')
$end=$s.IndexOf('        public static void PlaySample()', $start)
if($start -lt 0 -or $end -lt 0){throw 'GenerateSample anchors not found'}
$generate=@'
        public static string GenerateSample()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play mode before preparing the sample.");
            SetStatus("Preparing Astral Rift 3.0: 240 cards, three unique starters and matched illustrations...");
            var rules = AstralRiftNativeContent.Prepare();
            TcgTemplateProjectSetup.CreateTemplateScene();
            TcgTemplateProjectSetup.ConfigureConnection(BaseUrl, AstralRiftNativeContent.PackId,
                AstralRiftNativeContent.PackId, AstralRiftNativeContent.Version);
            EditorPrefs.SetBool(Configured, true);
            SetStatus("Astral Rift 3.0 staged. Build the native host if needed, then press Play.");
            return rules;
        }

'@
$s=$s.Substring(0,$start)+$generate+$s.Substring($end)
$start=$s.IndexOf('                var package = Path.Combine(root, "GamePackages",')
$end=$s.IndexOf('                var scene = TcgTemplateProjectSetup.ScenePath;', $start)
if($start -lt 0 -or $end -lt 0){throw 'Stale package publish anchors not found'}
$s=$s.Substring(0,$start)+@'
                // The legacy 2.1 .cardforge archive must never overwrite
                // the authoritative Astral Rift 3 native rulepack or its art.
                AstralRiftNativeContent.Prepare();

'@+$s.Substring($end)
$old=@'
                TcgTemplateProjectSetup.ConfigureConnection(baseUrl, "astral-rift",
                    CardForgeDemoGenerator.PackageId, CardForgeDemoGenerator.Version);

                SetStatus("Publishing the editor's current rules and artwork to Unity...");
                await NativeContentPublisher.PublishToServerAsync(package, baseUrl);
'@
$new=@'
                TcgTemplateProjectSetup.ConfigureConnection(baseUrl, AstralRiftNativeContent.PackId,
                    AstralRiftNativeContent.PackId, AstralRiftNativeContent.Version);

                SetStatus("Checking Astral Rift 3 rules and publishing only changed illustrations...");
                await AstralRiftNativeContent.PublishAsync(baseUrl);
'@
if(!$s.Contains($old)){throw 'publish invocation anchors not found'}
$s=$s.Replace($old,$new)
[IO.File]::WriteAllText($p,$s,[Text.UTF8Encoding]::new($false))
Write-Output 'One-click Generate and Play now use native Astral Rift 3, not legacy 2.1 archives.'
