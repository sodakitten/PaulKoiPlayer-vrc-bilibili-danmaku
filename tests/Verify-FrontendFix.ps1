param(
    [ValidateSet('normal','gold')][string]$Variant = 'normal',
    [Parameter(Mandatory)][string]$WorkspaceRoot
)
$ErrorActionPreference = 'Stop'
Add-Type -Path (Join-Path $PSHOME 'Microsoft.CodeAnalysis.dll')
Add-Type -Path (Join-Path $PSHOME 'Microsoft.CodeAnalysis.CSharp.dll')
$root = Join-Path $WorkspaceRoot "$Variant/YamaBiliDanmakuV3"
$baseline = Join-Path $WorkspaceRoot "baseline/$Variant/YamaBiliDanmakuV3"
$options = [Microsoft.CodeAnalysis.CSharp.CSharpParseOptions]::Default.WithPreprocessorSymbols([string[]]@('UNITY_EDITOR'))
function Read-Tree([string]$Path) {
    $tree = [Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree]::ParseText([IO.File]::ReadAllText($Path), $options)
    $errors = @($tree.GetDiagnostics() | Where-Object Severity -eq Error)
    if ($errors.Count) { throw "$Path syntax: $errors" }
    return $tree.GetRoot()
}
function Methods($Tree) {
    $map = @{}
    foreach ($m in $Tree.DescendantNodes()) {
        if ($m -is [Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax]) { $map[$m.Identifier.ValueText] = $m }
    }
    return $map
}
$module = Read-Tree "$root/Runtime/YamaBiliDanmakuModule3.cs"
$pages = Read-Tree "$root/Runtime/YamaBiliPagesPlaylist3.cs"
$editor = Read-Tree "$root/Editor/YamaBiliDanmakuRigBuilder3.cs"
$mm = Methods $module
$pm = Methods $pages
$em = Methods $editor
$checks = 0
function Assert([bool]$Pass, [string]$Message) {
    if (!$Pass) { throw $Message }
    $script:checks++
}
foreach ($file in Get-ChildItem $root -Recurse -File) {
    $relative = [IO.Path]::GetRelativePath($root, $file.FullName)
    if ($file.Extension -eq '.cs') { $null = Read-Tree $file.FullName }
    if ($relative -notin @('Runtime\YamaBiliDanmakuModule3.cs','Runtime\YamaBiliPagesPlaylist3.cs','Editor\YamaBiliDanmakuRigBuilder3.cs')) {
        Assert ((Get-FileHash $file.FullName).Hash -eq (Get-FileHash (Join-Path $baseline $relative)).Hash) "Unexpected change: $relative"
    }
}
# Exact method-token comparison: unlisted parser, timeline, ownership, queue and rendering methods must remain unchanged.
$allowed = @{
 'Runtime/YamaBiliDanmakuModule3.cs' = @('Update','LoadCurrentTrackDanmaku','LoadFallbackDanmaku','LoadDanmakuUrl','OnStringLoadSuccess','OnStringLoadError','ResetDanmaku','GetCurrentYamaPlayerUrl');
 'Runtime/YamaBiliPagesPlaylist3.cs' = @('WatchPlaybackUrl','WatchUnifiedQueue','BeginPagesRequest','BeginUnifiedSourceRequest','CheckPagesTimeout','CancelUnifiedQueueRequest','OnQueueUpdated','OnTrackUpdated','OnUrlChanged','OnOwnerChanged','OnVideoStop','OnStringLoadSuccess','OnStringLoadError','CompleteUnifiedRequest','CompleteUnifiedRequestWithRawFallback','ClearPages','OnDeserialization','ApplySyncedManifestState','RequestSelectedNeteaseSongMetadata','CheckUnifiedMetadataTimeout');
 'Editor/YamaBiliDanmakuRigBuilder3.cs' = @('CreateOrFindUrlPrefixHelper','CreateOrFindPagesPlaylist','WireUrlPrefixHelper','WireModuleUrlPrefixControls','WirePagesPlaylistReferences')
}
foreach ($path in $allowed.Keys) {
    $before = Methods (Read-Tree "$baseline/$path")
    $after = Methods (Read-Tree "$root/$path")
    foreach ($name in $before.Keys) {
        Assert ($after.ContainsKey($name)) "Method removed: $path $name"
        if ($name -notin $allowed[$path]) {
            Assert ($before[$name].ToString().Replace("`r",'') -ceq $after[$name].ToString().Replace("`r",'')) "Unexpected method change: $path $name"
        }
    }
}
$protected = '_urlPrefix|_enableUrlPrefixOnInput|_keepPrefixWhenEmpty|_refreshSeconds|_inputWatchSeconds|_urlPrefixFillEnabled|_pagesApiPrefix|_autoRefreshOnPlayback|_autoPlayNext|_useUnifiedQueue|_playbackWatchSeconds|_marqueePixelsPerSecond|_marqueeTickSeconds|_marqueePauseSeconds|_vcridUrlPrefix|_vcridMax'
foreach ($name in @('WireUrlPrefixHelper','WirePagesPlaylistReferences','WireModuleUrlPrefixControls')) {
    Assert ($em[$name].ToString() -notmatch ('"(' + $protected + ')"')) "Existing settings overwritten in $name"
}
foreach ($pair in @(@('CreateOrFindUrlPrefixHelper','helper','InitializeNewUrlPrefixHelper'),@('CreateOrFindPagesPlaylist','playlist','InitializeNewPagesPlaylist'))) {
    $call = @($em[$pair[0]].DescendantNodes() | Where-Object { $_ -is [Microsoft.CodeAnalysis.CSharp.Syntax.InvocationExpressionSyntax] -and $_.Expression.ToString() -eq $pair[2] })
    Assert ($call.Count -eq 1) "Initializer count: $($pair[2])"
    $parents = @($call[0].Ancestors() | Where-Object { $_ -is [Microsoft.CodeAnalysis.CSharp.Syntax.IfStatementSyntax] -and $_.Condition.ToString() -eq "$($pair[1]) == null" })
    Assert ($parents.Count -gt 0) "Defaults not restricted to new components"
}
foreach ($runtime in Get-ChildItem "$root/Runtime" -Filter '*.cs') {
    $s = [IO.File]::ReadAllText($runtime.FullName)
    Assert ($s -notmatch 'new\s+VRCUrl\s*\(') "Runtime URL construction: $runtime"
    Assert ($s -notmatch '\b(YamaPlayerListener|TrackUtils|MirrorFlip)\b|Controller\.Handler') "Forbidden API: $runtime"
}

# Execute the REAL request-coordinator methods, extracted with Roslyn.
# Only SDK/Unity I/O, parsing and presentation are stubbed. This is NOT Udon/Unity compilation.
$common = @'
using System;
using System.Collections.Generic;
using System.Reflection;
public interface IUdonEventReceiver {}
public interface IVRCStringDownload { VRCUrl Url {get;} string Result {get;} int ErrorCode {get;} }
public class Download : IVRCStringDownload {
 public VRCUrl Url {get;set;} public string Result {get;set;} public int ErrorCode {get;set;}
 public Download(string url,string result="ok") {Url=new VRCUrl(url);Result=result;ErrorCode=500;}
}
public class VRCUrl { string value; public VRCUrl(string s){value=s;} public string Get(){return value;} public static VRCUrl Empty=new VRCUrl(""); public static bool IsNullOrEmpty(VRCUrl u){return u==null||string.IsNullOrEmpty(u.value);} }
public class VRCPlayerApi { public int playerId; }
public static class Utilities { public static bool IsValid(object o){return o!=null;} }
public static class Networking {
 public static bool IsOwner(object o){return ((Controller)o).OwnerId==1;}
 public static VRCPlayerApi GetOwner(object o){return new VRCPlayerApi{playerId=((Controller)o).OwnerId};}
}
public static class Time { public static float time; }
public static class Mathf {public static int RoundToInt(float x){return (int)Math.Round(x);} public static int Abs(int x){return Math.Abs(x);} }
public class Track {
 public VRCUrl Url; public bool IsValid(){return Url!=null;} public VRCUrl GetVRCUrl(){return Url;}
 public static Track New(int type,string title,VRCUrl url){return new Track{Url=url};}
}
public class Playlist { public int Length; public int Added; public void TakeOwnership(){} public void AddTrack(Track t){Length++;Added++;} }
public class Controller {
 public int OwnerId=1; public bool Stopped,IsLoading,Paused,IsLive; public float VideoTime; public int VideoPlayerType;
 public Track Track=new Track{Url=new VRCUrl("A")}; public object gameObject {get{return this;}}
 public VRCUrl SyncedUrl=new VRCUrl("old"); public Playlist Queue=new Playlist(); public int Played;
 public object GetProgramVariable(string name){return SyncedUrl;} public void TakeOwnership(){OwnerId=1;}
 public void PlayTrack(Track t){Track=t;Played++;}
}
public class StubBehaviour : IUdonEventReceiver {
 public List<string> Sent=new List<string>(); public List<float> Delays=new List<float>();
 public virtual void OnStringLoadSuccess(IVRCStringDownload result){} public virtual void OnStringLoadError(IVRCStringDownload result){}
 public void SendCustomEventDelayedSeconds(string name,float seconds){Delays.Add(seconds);}
}
public static class VRCStringDownloader { public static void LoadUrl(VRCUrl url,IUdonEventReceiver receiver){((StubBehaviour)receiver).Sent.Add(url.Get());} }
public static class Test {
 public static int Count;
 public static void Check(bool b,string name){if(!b)throw new Exception(name);Count++;}
 public static object Get(object o,string name){return o.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(o);}
 public static void Set(object o,string name,object v){o.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).SetValue(o,v);}
 public static object Call(object o,string name,params object[] a){return o.GetType().GetMethod(name,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic).Invoke(o,a);}
 public static Controller Setup(object o){var c=new Controller();Set(o,"_controller",c);Time.time=0;return c;}
 public static void Url(Controller c,string url){c.Track=new Track{Url=new VRCUrl(url)};}
}
'@
$moduleStubs = @'
 public int Parsed; public string Body="",Status="";
 private void SetStatus(string s){Status=s;} private string ExtractDanmakuBlock(string s){return s;}
 private void ParseDanmaku(string s){Parsed++;Body=s;_loaded=true;}
 private void HideAllTexts(){} private void ClearLaneTimers(){} private void UpdateStatusVisibility(){}
 private void ResumeActiveTimers(float x){} private void UpdateActiveTexts(){} private void SeekLineIndex(int x){} private void ShowLine(int x){}
'@
$pageStubs = @'
 public int Parsed,MetadataWrites,Actions,TitleWrites; public string Status=""; public bool QueueItemExists=true;
 private void SetStatus(string s){Status=s;} private void UpdateLabels(){} private void ScheduleNormalizeQueue(){}
 private void DiscardPreservedStandaloneManifest(){_preserveStandaloneManifestRequest=false;}
 private void PreserveStandaloneManifestForQueueRequest(){_preserveStandaloneManifestRequest=true;}
 private void ExitStandaloneManifestModeForUnifiedQueueRequest(){_standaloneManifestMode=false;}
 private void RestorePreservedStandaloneManifest(){_preserveStandaloneManifestRequest=false;}
 private int FindQueuedTrackIndex(int i,string u){return QueueItemExists?i:-1;}
 private void ParsePagesJson(string s){Parsed++;_totalPages=s=="empty"?0:1;_selectedIndex=0;_needsNeteaseSongMetadata=s=="song";_vcrids=new[]{1};_vcridMax=10;_vcridUrls=new[]{VRCUrl.Empty,new VRCUrl("meta")};}
 private void ApplyNeteaseSongMetadata(string s,int i){MetadataWrites++;}
 private bool IsVcridPlaybackUrl(string u){return u.Contains("vcrid=");}
 private bool ApplyCurrentOrSyncedSelection(){return false;}
 private void ActivateSyncedBiliManifestFromParsed(){Actions++;}
 private void PublishManifestState(VRCUrl u,int i,bool active){}
 private int GetSelectedVcrid(){return 1;} private void LoadSelectedNeteaseDanmaku(){}
 private bool ShouldUseNeteaseExclusiveManifestPlayback(){return false;} private bool ShouldUseBiliMixedManifestPlayback(){return false;}
 private void EnterStandaloneManifestMode(int m,VRCUrl u){Actions++;}
 private void CompleteBiliMixedManifestRequest(int m,VRCUrl u,int i,string s){Actions++;}
 private void ReplaceQueuedTrackWithParsedTracks(VRCUrl u,int i,string s){Actions++;}
 private string GetParsedQueueTitle(int i){return "Title";} private bool HasRetainedBiliManifestItems(){return false;}
 private int GetRetainedHistoryDisplayCount(){return 0;} private bool HasCurrentUnifiedTrack(){return true;}
 private void MergeUnifiedQueueHeader(VRCUrl u,string t,bool b){} private void SetCurrentTrackTitle(string s){TitleWrites++;}
 private string BuildParsedTrackTitle(int i){return "Title";} private void PrepareCurrentTrackRetention(VRCUrl u){}
 private void AddParsedTracksToQueue(int i,VRCUrl u,bool start,bool expand){Actions++;} private void LoadParsedCurrentDanmaku(int i){}
 private void ClampUnifiedPageOffset(){} private void MarkQueuedTrackAsUnnamed(int i,string u,string s){Actions++;}
 private void PublishUnifiedQueueHeader(string s,VRCUrl u,bool a,bool b){} private int GetUnifiedDisplayCount(){return 0;}
'@
function Build-Harness($Tree,$MethodNames,[string]$Stubs,[string]$Class) {
    $map=Methods $Tree
    $body=($MethodNames | ForEach-Object {
        if (!$map.ContainsKey($_)) {throw "Missing method $_"}
        $map[$_].ToString()
    }) -join "`n"
    # Include only original fields referenced by the real methods / I/O adapters, stripping Unity attributes.
    $fields=@($Tree.DescendantNodes() | Where-Object {$_ -is [Microsoft.CodeAnalysis.CSharp.Syntax.FieldDeclarationSyntax]})
    $chosen=@{}
    do {
        $changed=$false
        foreach ($field in $fields) {
            foreach ($v in $field.Declaration.Variables) {
                $name=$v.Identifier.ValueText
                if (!$chosen.ContainsKey($name) -and ($body+"`n"+$Stubs) -match ("\b"+[regex]::Escape($name)+"\b")) {
                    $declaration=$field.Modifiers.ToString()+' '+$field.Declaration.ToString()+';'
                    $chosen[$name]=$true; $body=$declaration+"`n"+$body; $changed=$true
                }
            }
        }
    } while ($changed)
    return "public class $Class : StubBehaviour {`n$body`n$Stubs`n}"
}
$moduleMethods = @('Update','LoadCurrentTrackDanmaku','LoadFallbackDanmaku','LoadDanmakuUrl','ClearDanmaku','SetExternalAudioMode','OnStringLoadSuccess','OnStringLoadError','GetUrlString','BeginDanmakuDownload','IsDanmakuRequestCurrent','StartPendingDanmakuDownload','AcceptDanmakuDownload','TryLoadFallbackAfterCurrentUrl','ResetDanmaku','GetCurrentYamaPlayerUrl')
$pageMethods = @('BeginPagesRequest','BeginUnifiedSourceRequest','GetControllerOwnerId','IsPagesRequestCurrent','CancelInvalidPagesRequest','InvalidatePagesDownloads','QueuePagesDownload','StartPendingPagesDownload','AcceptPagesDownload','CheckPagesTimeout','CancelUnifiedQueueRequest','OnStringLoadSuccess','OnStringLoadError','CompleteUnifiedRequest','CompleteUnifiedRequestWithRawFallback','RequestSelectedNeteaseSongMetadata','CheckUnifiedMetadataTimeout','ResetParsedSource','GetCurrentControllerUrl','GetUrlString')
$tests = @'
public static class Scenarios {
 static void Check(bool b,string n){Test.Check(b,n);} static object G(object o,string n){return Test.Get(o,n);}
 static void S(object o,string n,object v){Test.Set(o,n,v);} static object C(object o,string n,params object[] a){return Test.Call(o,n,a);}
 static void Begin(PageHarness p,string u,int mode=1){C(p,"BeginUnifiedSourceRequest",new VRCUrl(u),mode,false);}
 public static int Run() {
   var m=new ModuleHarness();var c=Test.Setup(m);m.LoadCurrentTrackDanmaku();
   Check(m.Sent[0]=="A","module uses current Track before lagging synced _url");
   m.OnStringLoadSuccess(new Download("other","bad"));Check(m.Parsed==0,"unrequested URL ignored");
   m.OnStringLoadSuccess(new Download("A","good"));Check(m.Parsed==1&&m.Body=="good","valid danmaku accepted");
   m.OnStringLoadSuccess(new Download("A","duplicate"));Check(m.Parsed==1,"completed callback ignored");
   m=new ModuleHarness();c=Test.Setup(m);S(m,"_fallbackDanmakuUrl",new VRCUrl("fallback"));m.LoadCurrentTrackDanmaku();
   Test.Url(c,"B");m.LoadDanmakuUrl(new VRCUrl("B"));Test.Url(c,"A");m.LoadDanmakuUrl(new VRCUrl("A"));
   Check(m.Sent.Count==1,"same URL ABA serialized");m.OnStringLoadError(new Download("A"));
   Check(m.Parsed==0&&m.Sent.Count==2&&m.Sent[1]=="A","stale error cannot trigger fallback, latest A dispatched");
   m.OnStringLoadSuccess(new Download("A","new A"));Check(m.Body=="new A","latest A callback accepted");
   m=new ModuleHarness();c=Test.Setup(m);m.LoadCurrentTrackDanmaku();Test.Url(c,"B");
   m.OnStringLoadSuccess(new Download("A","bad"));Check(m.Parsed==0,"current Track checked before Update tick");
   m=new ModuleHarness();c=Test.Setup(m);m.LoadCurrentTrackDanmaku();m.ClearDanmaku();m.OnStringLoadSuccess(new Download("A"));Check(m.Parsed==0,"clear invalidates response");
   m=new ModuleHarness();c=Test.Setup(m);m.LoadCurrentTrackDanmaku();m.SetExternalAudioMode(true);m.LoadDanmakuUrl(new VRCUrl("lyric"));
   m.OnStringLoadSuccess(new Download("A","bad"));Check(m.Parsed==0&&m.Sent[1]=="lyric","provider switch drains old video");
   m.OnStringLoadSuccess(new Download("lyric","lyrics"));Check(m.Body=="lyrics","lyrics accepted with same current playback");
   m=new ModuleHarness();c=Test.Setup(m);c.Stopped=true;c.IsLoading=true;m.LoadDanmakuUrl(new VRCUrl("lyric"));C(m,"Update");
   m.OnStringLoadSuccess(new Download("lyric","loading"));Check(m.Parsed==1,"loading state may be Stopped until video start");
   m=new ModuleHarness();c=Test.Setup(m);m.LoadCurrentTrackDanmaku();c.Stopped=true;m.OnStringLoadSuccess(new Download("A"));Check(m.Parsed==0,"stopped playback rejects download");
   m=new ModuleHarness();c=Test.Setup(m);S(m,"_loadFromCurrentYamaPlayerUrl",false);C(m,"Update");int epoch=(int)G(m,"_requestEpoch");C(m,"Update");
   Check(m.Sent.Count==0&&(int)G(m,"_requestEpoch")==epoch,"disabled current loading and empty fallback do not reset every frame");
   m=new ModuleHarness();c=Test.Setup(m);S(m,"_fallbackDanmakuUrl",new VRCUrl("fallback"));m.LoadCurrentTrackDanmaku();m.OnStringLoadError(new Download("A"));
   Check(m.Sent.Count==2&&m.Sent[1]=="fallback","valid failure retains normal fallback");m.OnStringLoadSuccess(new Download("fallback","fallback body"));Check(m.Body=="fallback body","valid fallback accepted");

   var p=new PageHarness();c=Test.Setup(p);int enqueue=(int)typeof(PageHarness).GetField("RequestModeEnqueue",BindingFlags.NonPublic|BindingFlags.Static).GetRawConstantValue();
   int expand=(int)typeof(PageHarness).GetField("RequestModeExpandCurrent",BindingFlags.NonPublic|BindingFlags.Static).GetRawConstantValue();
   int normalize=(int)typeof(PageHarness).GetField("RequestModeNormalizeQueued",BindingFlags.NonPublic|BindingFlags.Static).GetRawConstantValue();
   Begin(p,"A",expand);p.OnStringLoadSuccess(new Download("A"));Check(p.Parsed==1&&p.TitleWrites==1,"current expansion completes");
   p.OnStringLoadSuccess(new Download("A"));Check(p.Parsed==1,"page completion consumed once");
   p=new PageHarness();c=Test.Setup(p);Begin(p,"A",expand);Test.Url(c,"B");p.OnStringLoadSuccess(new Download("A"));Check(p.Parsed==0&&p.TitleWrites==0,"changed playback rejected before parsing");
   p=new PageHarness();c=Test.Setup(p);Begin(p,"A",expand);c.OwnerId=2;p.OnStringLoadSuccess(new Download("A"));Check(p.Parsed==0&&p.Actions==0,"lost ownership rejected before apply");
   p=new PageHarness();c=Test.Setup(p);Begin(p,"input",enqueue);Test.Url(c,"B");c.OwnerId=2;p.OnStringLoadSuccess(new Download("input"));Check(p.Parsed==1&&p.Actions==1,"explicit enqueue survives playback and owner changes");
   p=new PageHarness();c=Test.Setup(p);Begin(p,"A",expand);Test.Url(c,"B");C(p,"CancelInvalidPagesRequest");Test.Url(c,"A");Begin(p,"A",expand);
   Check(p.Sent.Count==1,"pages ABA waits for old request");p.OnStringLoadSuccess(new Download("A"));Check(p.Parsed==0&&p.Sent.Count==2,"old A drained without parsing");
   p.OnStringLoadSuccess(new Download("A"));Check(p.Parsed==1&&p.TitleWrites==1,"new A accepted");
   p=new PageHarness();c=Test.Setup(p);Begin(p,"A",enqueue);Time.time=15;p.CheckPagesTimeout();Check(c.Queue.Added==1,"timeout raw fallback once");
   p.OnStringLoadSuccess(new Download("A"));p.CheckPagesTimeout();Check(c.Queue.Added==1&&p.Parsed==0,"late success cannot duplicate timeout insertion");
   p=new PageHarness();c=Test.Setup(p);Begin(p,"A",enqueue);Time.time=15;p.CheckPagesTimeout();Begin(p,"A",enqueue);Time.time=40;p.CheckPagesTimeout();
   Check(c.Queue.Added==1,"waiting SDK request does not time out before dispatch");p.OnStringLoadError(new Download("A"));Check(p.Sent.Count==2,"old timeout error drains");
   Time.time=41;p.CheckPagesTimeout();Check(c.Queue.Added==1,"old delayed timer cannot expire new request");p.OnStringLoadSuccess(new Download("A"));Check(p.Actions==1,"next request succeeds after previous timeout");
   p=new PageHarness();c=Test.Setup(p);Begin(p,"A",enqueue);C(p,"CancelUnifiedQueueRequest");p.OnStringLoadError(new Download("A"));Check(c.Queue.Added==0,"cleared request error cannot enqueue fallback");
   p=new PageHarness();c=Test.Setup(p);S(p,"_normalizeQueueIndex",0);S(p,"_normalizeQueueTrackUrl","Q");Begin(p,"Q",normalize);p.QueueItemExists=false;p.OnStringLoadSuccess(new Download("Q"));Check(p.Parsed==0,"removed queue item cannot receive old title");
   p=new PageHarness();c=Test.Setup(p);S(p,"_syncedManifestActive",true);S(p,"_syncedManifestLoadPending",true);S(p,"_syncedManifestUrl",new VRCUrl("manifest"));C(p,"BeginPagesRequest",new VRCUrl("manifest"));
   Test.Url(c,"vcrid=150");p.OnStringLoadSuccess(new Download("manifest"));Check(p.Parsed==1,"synced manifest survives selection-only playback change");
   p=new PageHarness();c=Test.Setup(p);S(p,"_syncedManifestActive",true);S(p,"_syncedManifestLoadPending",true);S(p,"_syncedManifestUrl",new VRCUrl("manifest"));C(p,"BeginPagesRequest",new VRCUrl("manifest"));
   S(p,"_syncedManifestUrl",new VRCUrl("new manifest"));p.OnStringLoadSuccess(new Download("manifest"));Check(p.Parsed==0,"replaced synced manifest rejected");
   p=new PageHarness();c=Test.Setup(p);Begin(p,"song",enqueue);p.OnStringLoadSuccess(new Download("song","song"));Check(p.Actions==0&&p.Sent.Count==2&&p.Sent[1]=="meta","metadata awaited before enqueue");
   p.OnStringLoadSuccess(new Download("meta"));Check(p.MetadataWrites==1&&p.Actions==1,"valid metadata completes once");p.OnStringLoadSuccess(new Download("meta"));Check(p.MetadataWrites==1&&p.Actions==1,"metadata duplicate ignored");
   p=new PageHarness();c=Test.Setup(p);Begin(p,"A",expand);p.OnStringLoadSuccess(new Download("A","song"));Test.Url(c,"B");p.OnStringLoadSuccess(new Download("meta"));Check(p.MetadataWrites==0&&p.TitleWrites==0,"metadata cannot title different playback");
   p=new PageHarness();c=Test.Setup(p);Begin(p,"song",enqueue);p.OnStringLoadSuccess(new Download("song","song"));Time.time=15;p.CheckUnifiedMetadataTimeout();Check(p.Actions==1,"metadata timeout completes without title");
   p.OnStringLoadSuccess(new Download("meta"));Check(p.MetadataWrites==0&&p.Actions==1,"late metadata after timeout rejected");
   p=new PageHarness();c=Test.Setup(p);Begin(p,"song",enqueue);Time.time=10;p.OnStringLoadSuccess(new Download("song","song"));Time.time=15;p.CheckUnifiedMetadataTimeout();Check(p.Actions==0,"metadata has its own dispatch deadline");
   Time.time=25;p.CheckUnifiedMetadataTimeout();Check(p.Actions==1,"metadata own deadline expires");
   p=new PageHarness();c=Test.Setup(p);Begin(p,"A",expand);c.OwnerId=2;C(p,"CancelInvalidPagesRequest");c.OwnerId=1;p.OnStringLoadSuccess(new Download("A"));Check(p.Parsed==0,"ownership ABA remains invalidated");
   return Test.Count;
 }
}
'@
$source=$common+"`n"+(Build-Harness $module $moduleMethods $moduleStubs 'ModuleHarness')+"`n"+(Build-Harness $pages $pageMethods $pageStubs 'PageHarness')+"`n"+$tests
Add-Type -TypeDefinition $source -IgnoreWarnings -WarningAction SilentlyContinue
$scenarioCount=[Scenarios]::Run()
Write-Output "PASS $Variant : $checks source/invariant checks; $scenarioCount actual-method behavioral assertions. NOT Unity/Udon/VR validation."
