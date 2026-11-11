$p='D:\CardForgeStudio\Assets\CardForgeStudio\CardForgeDedicatedServer\Editor\DedicatedServerBuilder.cs'
$s=[IO.File]::ReadAllText($p)
$a=$s.IndexOf('            var report = BuildPipeline.BuildPlayer(options);')
$b=$s.IndexOf('            EditorUserBuildSettings.standaloneBuildSubtarget = StandaloneBuildSubtarget.Player;',$a)
if($a -lt 0 -or $b -lt 0){throw 'Build method anchors not found'}
$b+='            EditorUserBuildSettings.standaloneBuildSubtarget = StandaloneBuildSubtarget.Player;'.Length
$replacement=@'
            var previousSubtarget = EditorUserBuildSettings.standaloneBuildSubtarget;
            var previousOptimizations = PlayerSettings.dedicatedServerOptimizations;
            try
            {
                var report = BuildPipeline.BuildPlayer(options);
                if (report.summary.result != BuildResult.Succeeded)
                    throw new InvalidOperationException(
                        "Unity server build failed: " + report.summary.result +
                        ". Install Windows Dedicated Server build support if Unity reports a missing module.");
                Debug.Log("Native CardForge Unity Dedicated Server: " + Path.GetFullPath(Output));
            }
            finally
            {
                // Restore project build preferences even if BuildPlayer throws.
                PlayerSettings.dedicatedServerOptimizations = previousOptimizations;
                EditorUserBuildSettings.standaloneBuildSubtarget = previousSubtarget;
            }
'@
[IO.File]::WriteAllText($p,$s.Substring(0,$a)+$replacement+$s.Substring($b),[Text.UTF8Encoding]::new($false))
'Server builder now restores prior editor build settings after success and failure.'
