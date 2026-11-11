$ErrorActionPreference='Stop'
$project='D:\CardForgeStudio\CardForge.Learn.Editor.csproj'
$raw=[IO.File]::ReadAllBytes($project)
try {
  $xml=[IO.File]::ReadAllText($project)
  $from='<Compile Include="Assets\CardForgeStudio\CardForgeLearn\Editor\CardForgePlayWorkflow.cs" />'
  if(!$xml.Contains($from)){throw 'Editor assembly item anchor missing'}
  if(!$xml.Contains('AstralRiftNativeContent.cs')){
    $xml=$xml.Replace($from,$from+[Environment]::NewLine+
      '    <Compile Include="Assets\CardForgeStudio\CardForgeLearn\Editor\AstralRiftNativeContent.cs" />')
    [IO.File]::WriteAllText($project,$xml)
  }
  & dotnet build $project -v:q -nologo
  if($LASTEXITCODE -ne 0){throw "Editor content compilation failed with exit code $LASTEXITCODE"}
} finally {
  [IO.File]::WriteAllBytes($project,$raw)
}
Write-Output 'Native publisher and Unity Editor C# compile successfully; generated csproj restored.'
