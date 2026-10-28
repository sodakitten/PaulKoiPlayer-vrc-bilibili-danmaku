param([string]$RepositoryRoot = (Split-Path $PSScriptRoot -Parent))
$ErrorActionPreference = 'Stop'
Add-Type -Path (Join-Path $PSHOME 'Microsoft.CodeAnalysis.dll')
Add-Type -Path (Join-Path $PSHOME 'Microsoft.CodeAnalysis.CSharp.dll')
$options = [Microsoft.CodeAnalysis.CSharp.CSharpParseOptions]::Default.WithPreprocessorSymbols([string[]]@('UNITY_EDITOR'))
$directories = @('Runtime', 'Editor')
$files = @($directories | ForEach-Object { Get-ChildItem (Join-Path $RepositoryRoot $_) -Recurse -Filter '*.cs' })
foreach ($file in $files) {
    $source = [IO.File]::ReadAllText($file.FullName)
    $tree = [Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree]::ParseText($source, $options)
    $errors = @($tree.GetDiagnostics() | Where-Object Severity -eq Error)
    if ($errors.Count) { throw "$($file.FullName): $errors" }
    if ($file.FullName -match '[\\/]Runtime[\\/]') {
        if ($source -match 'new\s+VRCUrl\s*\(') { throw "Runtime URL construction: $($file.FullName)" }
        if ($source -match '_lineCount\s*<\s*_maxDanmakuLines') { throw "Timeline truncation: $($file.FullName)" }
    }
}
$editor = [Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree]::ParseText(
    [IO.File]::ReadAllText((Join-Path $RepositoryRoot 'Editor/PaulKoiBackendProtocol3.cs')), $options).GetRoot()
$method = @($editor.DescendantNodes() | Where-Object {
    $_ -is [Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax] -and $_.Identifier.ValueText -eq 'WithVersion'
})[0].ToString()
Add-Type -TypeDefinition ("using System; using System.Collections.Generic; public static class PrefixHarness { " + $method.Replace('internal static', 'public static') + ' }')
function Expect([string]$Actual, [string]$Expected) {
    if ($Actual -cne $Expected) { throw "Expected $Expected, got $Actual" }
}
Expect ([PrefixHarness]::WithVersion('https://example.org/player/?url=', 'url', $true)) 'https://example.org/player/?__pkdm=2&url='
Expect ([PrefixHarness]::WithVersion('http://localhost:7858/player/?__pkdm=1&lang=zh%2DCN&vcrid=', 'vcrid', $true)) 'http://localhost:7858/player/?lang=zh-CN&__pkdm=2&vcrid='
Expect ([PrefixHarness]::WithVersion('https://example.org/player/?__pkdm=2&url=', 'url', $false)) 'https://example.org/player/?url='
Expect ([PrefixHarness]::WithVersion('https://example.org/player/?__pkdm=2&url=', 'url', $true)) 'https://example.org/player/?__pkdm=2&url='
foreach ($invalid in @('file:///player/?url=', 'https://example.org/api/pages?url=', 'https://example.org/player/?url=x', 'https://example.org/player/?url=&p=1', 'https://example.org/player/?__dm=1&url=', 'https://example.org/player/?vcrid=1&url=', 'https://example.org/player/?url=#x', 'https://user:password@example.org/player/?url=')) {
    $rejected = $false
    try { [void][PrefixHarness]::WithVersion($invalid, 'url', $true) } catch { $rejected = $true }
    if (!$rejected) { throw "Invalid playback prefix accepted: $invalid" }
}
Write-Output "PASS: $($files.Count) C# files parse; runtime URL/truncation guards; 12 real editor-prefix cases."
function Read-SyntaxRoot([string]$RelativePath) {
    [Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree]::ParseText(
        [IO.File]::ReadAllText((Join-Path $RepositoryRoot $RelativePath)), $options).GetRoot()
}
function Find-Variable($Root, [string]$Name) {
    @($Root.DescendantNodes() | Where-Object {
        $_ -is [Microsoft.CodeAnalysis.CSharp.Syntax.VariableDeclaratorSyntax] -and $_.Identifier.ValueText -eq $Name
    })[0]
}
$builder = Read-SyntaxRoot 'Editor/YamaBiliDanmakuRigBuilder3.cs'
$pages = Read-SyntaxRoot 'Runtime/YamaBiliPagesPlaylist3.cs'
$inputDefault = (Find-Variable $builder 'DefaultUrlPrefix').Initializer.Value.Token.ValueText
$catalogDefault = (Find-Variable $builder 'DefaultVcridUrlPrefix').Initializer.Value.Token.ValueText
Expect $inputDefault 'https://danmaku.paulkoishi.com/player/?__pkdm=2&url='
Expect $catalogDefault 'https://danmaku.paulkoishi.com/player/?__pkdm=2&vcrid='
Expect ((Find-Variable $builder 'DefaultPagesApiPrefix').Initializer.Value.ToString()) 'DefaultUrlPrefix'
Expect ((Find-Variable $pages '_vcridUrlPrefix').Initializer.Value.Token.ValueText) $catalogDefault
foreach ($pair in @(@('InitializeNewUrlPrefixHelper', 'helper == null'), @('InitializeNewPagesPlaylist', 'playlist == null'))) {
    $calls = @($builder.DescendantNodes() | Where-Object {
        $_ -is [Microsoft.CodeAnalysis.CSharp.Syntax.InvocationExpressionSyntax] -and $_.Expression.ToString() -eq $pair[0]
    })
    if ($calls.Count -ne 1) { throw "Unexpected default initializer calls: $($pair[0])" }
    $guards = @($calls[0].Ancestors() | Where-Object {
        $_ -is [Microsoft.CodeAnalysis.CSharp.Syntax.IfStatementSyntax] -and $_.Condition.ToString() -eq $pair[1]
    })
    if (!$guards.Count) { throw "Existing scene configuration may be overwritten: $($pair[0])" }
}
foreach ($name in @('WireUrlPrefixHelper', 'WirePagesPlaylistReferences', 'WireModuleUrlPrefixControls')) {
    $wire = @($builder.DescendantNodes() | Where-Object {
        $_ -is [Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax] -and $_.Identifier.ValueText -eq $name
    })[0]
    if ($wire.ToString() -match '"(_urlPrefix|_pagesApiPrefix|_vcridUrlPrefix)"') {
        throw "Reference repair rewrites protocol preferences: $name"
    }
}
$build = Read-SyntaxRoot 'Editor/YamaBiliVcridBuildProcess3.cs'
$catalogMethod = @($build.DescendantNodes() | Where-Object {
    $_ -is [Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax] -and $_.Identifier.ValueText -eq 'BuildCatalog'
})[0].ToString().Replace('private static', 'public static')
Add-Type -TypeDefinition ('public class VRCUrl { public static VRCUrl Empty = new VRCUrl(""); private string value; public VRCUrl(string s) { value=s; } public string Get() { return value; } } public static class CatalogHarness { ' + $catalogMethod + ' }')
$catalog = [CatalogHarness]::BuildCatalog($catalogDefault, 3)
if ($catalog.Length -ne 4 -or ![object]::ReferenceEquals($catalog[0], [VRCUrl]::Empty)) { throw 'Invalid VCRID zero slot or catalog length' }
foreach ($id in 1..3) { Expect ($catalog[$id].Get()) ($catalogDefault + $id) }
Write-Output 'PASS: paired V2 defaults, real catalog generation and existing-scene configuration guards.'
foreach ($script in @('Verify-DanmakuBackendSync.ps1', 'Verify-PagesBackendSync.ps1')) {
    & (Join-Path $PSHOME 'pwsh.exe') -NoProfile -File (Join-Path $PSScriptRoot $script) -RepositoryRoot $RepositoryRoot
    if ($LASTEXITCODE -ne 0) { throw "$script failed ($LASTEXITCODE)" }
}
Write-Output 'This verifies source and actual methods with SDK stubs, NOT a Unity/Udon build or VRChat client test.'
