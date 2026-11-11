$ErrorActionPreference = 'Stop'
$root = 'D:\CardForgeStudio'
$assets = Join-Path $root 'Assets'
$parent = Join-Path $assets 'CardForgeStudio'
$names = @('CardForge','CardForgeClient','CardForgeData','CardForgeDedicatedServer','CardForgeLearn','Plugins')
if (-not (Test-Path $parent)) { New-Item -ItemType Directory -Path $parent | Out-Null }
$log = Join-Path $root 'artifacts\asset-consolidation.log'
('Consolidating at ' + (Get-Date -Format o)) | Set-Content $log
foreach ($name in $names) {
    $old = Join-Path $assets $name
    $new = Join-Path $parent $name
    if (Test-Path $old) {
        if (Test-Path $new) { throw "Both old and new folders exist for $name" }
        Move-Item -LiteralPath $old -Destination $new -ErrorAction Stop
        "$old -> $new" | Add-Content $log
    }
    $oldMeta = "$old.meta"
    $newMeta = "$new.meta"
    if (Test-Path $oldMeta) {
        if (Test-Path $newMeta) { throw "Metadata already exists at $newMeta" }
        Move-Item -LiteralPath $oldMeta -Destination $newMeta -ErrorAction Stop
        "$oldMeta -> $newMeta" | Add-Content $log
    }
}
$extensions = @('.cs','.json','.md','.ps1','.uxml','.uss','.asmdef','.asmref','.asset','.unity','.xml','.tss','.slnx','.csproj','.yml','.yaml','.cmd','.sh','.props','.targets','.txt','.inputactions','.shader','.hlsl','.html','.js','.ts')
$skip = '\\(Library|Temp|obj|bin|\.git|Logs|artifacts|Builds|\.vs|PackageCache|art-transfer)\\'
$files = Get-ChildItem $root -Recurse -File -ErrorAction SilentlyContinue | Where-Object { $extensions -contains $_.Extension.ToLowerInvariant() -and $_.FullName -notmatch $skip }
$changed = 0
foreach ($file in $files) {
    $original = [System.IO.File]::ReadAllText($file.FullName,[System.Text.Encoding]::UTF8)
    $updated = $original
    foreach ($name in $names) {
        $updated = $updated.Replace("Assets/$name/","Assets/CardForgeStudio/$name/")
        $updated = $updated.Replace(("Assets\" + $name + "\"),("Assets\CardForgeStudio\" + $name + "\"))
    }
    if ($updated -cne $original) {
        [System.IO.File]::WriteAllText($file.FullName,$updated,(New-Object System.Text.UTF8Encoding($false)))
        $changed++
        $file.FullName | Add-Content $log
    }
}
"Rewritten text files: $changed" | Add-Content $log
Write-Output "MOVED 6 asset directories and .meta files; UPDATED $changed text files."
foreach ($name in $names) {
  Write-Output "$name old=$(Test-Path (Join-Path $assets $name)) new=$(Test-Path (Join-Path $parent $name)) meta=$(Test-Path (Join-Path $parent ($name+'.meta')))"
}
