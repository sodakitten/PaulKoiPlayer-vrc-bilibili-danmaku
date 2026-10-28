param([string]$RepositoryRoot = (Split-Path -Parent $PSScriptRoot))
$ErrorActionPreference = 'Stop'
Add-Type -Path (Join-Path $PSHOME 'Microsoft.CodeAnalysis.dll')
Add-Type -Path (Join-Path $PSHOME 'Microsoft.CodeAnalysis.CSharp.dll')
$path = Join-Path $RepositoryRoot 'Runtime/YamaBiliPagesPlaylist3.cs'
$text = [IO.File]::ReadAllText($path)
$tree = [Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree]::ParseText($text)
$errors = @($tree.GetDiagnostics() | Where-Object Severity -eq Error)
if ($errors.Count) { throw "Pages syntax errors: $errors" }
$nodes = $tree.GetRoot().DescendantNodes()
$methods = @{}
foreach ($node in $nodes) {
    if ($node -is [Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax]) {
        $methods[$node.Identifier.ValueText] = $node.ToString()
    }
}
if ($text -match 'new\s+VRCUrl\s*\(') { throw 'Runtime VRCUrl construction is forbidden' }
# Execute source methods, not a port of the parser or retry state machine.
# Unity/VRC I/O and unrelated UI/queue presentation are adapters in PagesHarness.cs.
$names = @(
    'BeginPagesRequest','BeginUnifiedSourceRequest','QueueInputUrl','GetControllerOwnerId',
    'IsPagesRequestCurrent','CancelInvalidPagesRequest','InvalidatePagesDownloads',
    'QueuePagesDownload','StartPendingPagesDownload','AcceptPagesDownload','SchedulePagesRetry',
    'RetryPagesDownload','FinishPagesDownloadFailure','CheckPagesTimeout','CancelUnifiedQueueRequest',
    'OnStringLoadSuccess','OnStringLoadError','ParsePagesJson','ParsePagesMetadata',
    'ExtractPagesMetadataHeader','GetPagesHeaderField','ParseIntString','UnescapeField','TokenToInt',
    'ResetParsedSource','SetParsedSourceTitle','SanitizeUiText','CompleteUnifiedRequest',
    'CompleteUnifiedRequestWithRawFallback','ShouldUseBiliMixedManifestPlayback',
    'ShouldUseNeteaseExclusiveManifestPlayback','CompleteBiliMixedManifestRequest','EnterBiliMixedManifestMode',
    'RequestSelectedNeteaseSongMetadata','CheckUnifiedMetadataTimeout','ApplyNeteaseSongMetadata',
    'LoadSelectedNeteaseDanmaku','LoadParsedCurrentDanmaku','GetVcridUrl','GetCurrentControllerUrl','GetUrlString',
    'IsPagesRequestUrl','IsDirectPagesApiUrl','IsVcridPlaybackUrl','GetPlayerParameterValue',
    'IsManifestSourceRequestUrl','IsUsableRequestUrl','IsEmptyPlayerPrefix',
    'GetSelectedVcrid','FindManifestItemIndexFromUrl','SelectManifestItemFromUrl','SelectManifestItemByVcrid',
    'ApplyCurrentOrSyncedSelection','WatchUnifiedQueue','IsKnownManifestVcridUrl',
    'FindBiliManifestIndexFromUrl','FindBiliManifestIndexByVcrid','NormalizeNextQueuedTrack','ScheduleNormalizeQueue',
    'FindQueuedTrackIndex','MarkQueuedTrackAsUnnamed','SetCurrentTrackTitle','GetTrackVrcUrl','IsUsableTrack'
)
$body = ($names | ForEach-Object {
    if (!$methods.ContainsKey($_)) { throw "Missing production method: $_" }
    $methods[$_]
}) -join "`n"
$harness = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'PagesHarness.cs'))
$chosen = @{}
$fields = @($nodes | Where-Object { $_ -is [Microsoft.CodeAnalysis.CSharp.Syntax.FieldDeclarationSyntax] })
do {
    $changed = $false
    foreach ($field in $fields) {
        foreach ($v in $field.Declaration.Variables) {
            $name = $v.Identifier.ValueText
            if (!$chosen.ContainsKey($name) -and ($body + $harness) -match ('\b' + [regex]::Escape($name) + '\b')) {
                $chosen[$name] = $true
                $body = $field.Modifiers.ToString() + ' ' + $field.Declaration.ToString() + ";`n" + $body
                $changed = $true
            }
        }
    }
} while ($changed)
$source = "#define PAGES_BACKEND_SYNC_TESTS`n" + $harness.Replace('/* PRODUCTION_METHODS */', $body) + "`n" +
    [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'PagesScenarios.cs'))
Add-Type -TypeDefinition $source -IgnoreWarnings -WarningAction SilentlyContinue
$count = [PagesBackendSync.Scenarios]::Run()
Write-Output "PASS Pages backend sync: $count actual-method assertions; $($names.Count) production methods. Not Unity/Udon/VR validation."
