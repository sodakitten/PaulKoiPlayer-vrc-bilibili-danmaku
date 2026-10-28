param([string]$RepositoryRoot = (Split-Path -Parent $PSScriptRoot))
$ErrorActionPreference = 'Stop'
Add-Type -Path (Join-Path $PSHOME 'Microsoft.CodeAnalysis.dll')
Add-Type -Path (Join-Path $PSHOME 'Microsoft.CodeAnalysis.CSharp.dll')
$path = Join-Path $RepositoryRoot 'Runtime/YamaBiliDanmakuModule3.cs'
$source = [IO.File]::ReadAllText($path)
$tree = [Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree]::ParseText($source)
$errors = @($tree.GetDiagnostics() | Where-Object Severity -eq Error)
if ($errors.Count) { throw "Module syntax errors: $errors" }
$methods = @{}
foreach ($node in $tree.GetRoot().DescendantNodes()) {
    if ($node -is [Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax]) {
        $methods[$node.Identifier.ValueText] = $node
    }
}
if ($source -match 'new\s+VRCUrl\s*\(|_maxDanmakuLines|\.Split\(') {
    throw 'Runtime URL construction, total row cap, or whole-response splitting found.'
}

# Compile the real coordinator, incremental parser and binary seek methods.
# Only Unity/SDK I/O and renderer presentation are replaced by small adapters.
# This is executable .NET coverage, not a Unity or UdonSharp compilation.
$names = @(
    'Update','LoadCurrentTrackDanmaku','LoadFallbackDanmaku','QueueFallbackDanmaku',
    'LoadDanmakuUrl','ClearDanmaku','SetExternalAudioMode','OnStringLoadSuccess','OnStringLoadError',
    'GetUrlString','BeginDanmakuRequest','BeginDanmakuDownload','IsMetadataV2Url','IsDanmakuRequestCurrent',
    'StartPendingDanmakuDownload','AcceptDanmakuDownload','TryLoadFallbackAfterCurrentUrl',
    'ScheduleDanmakuRetry','RetryDanmakuDownload','ResetDanmaku','GetCurrentYamaPlayerUrl',
    'ParseDanmaku','CancelDanmakuParse','TickDanmakuParser','FindDanmakuBlock','MatchesDanmakuText',
    'ReadDanmakuInteger','ReadDanmakuLine','ReadDanmakuMetadata','ReadDanmakuContentChar',
    'FinishDanmakuPass','FollowDanmakuMetadata','SeekLineIndex','LowerBoundDanmakuTime','ParseInt',
    'FindPagesPlaylistInParents','GetFixedLyricLane','GetFixedLyricDuration'
)
$body = ($names | ForEach-Object {
    if (!$methods.ContainsKey($_)) { throw "Missing actual method: $_" }
    $methods[$_].ToString()
}) -join "`n"
$stubs = @'
 public string Status="";
 public int Hidden,Shown;
 private void SetStatus(string s){Status=s;}
 private void HideAllTexts(){Hidden++;}
 private void ClearLaneTimers(){}
 private void UpdateStatusVisibility(){}
 private void ResumeActiveTimers(float seconds){}
 private void UpdateActiveTexts(){}
 private void ShowLine(int index){Shown++;}
'@
$fields = @($tree.GetRoot().DescendantNodes() | Where-Object {
    $_ -is [Microsoft.CodeAnalysis.CSharp.Syntax.FieldDeclarationSyntax]
})
$chosen = @{}
do {
    $changed = $false
    foreach ($field in $fields) {
        foreach ($variable in $field.Declaration.Variables) {
            $name = $variable.Identifier.ValueText
            if (!$chosen.ContainsKey($name) -and ($body + $stubs) -match ('\b' + [regex]::Escape($name) + '\b')) {
                $chosen[$name] = $true
                $body = $field.Modifiers.ToString() + ' ' + $field.Declaration.ToString() + ";`n" + $body
                $changed = $true
            }
        }
    }
} while ($changed)
$common = @'
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
public interface IUdonEventReceiver {}
public interface IVRCStringDownload { VRCUrl Url{get;} string Result{get;} int ErrorCode{get;} }
public class Download : IVRCStringDownload {
 public VRCUrl Url{get;set;} public int ErrorCode{get;set;} public string Body; public bool ResultUnavailable;
 public string Result {get{if(ResultUnavailable)throw new InvalidOperationException("No SDK error Result");return Body;}}
 public Download(string url,string body=null,int code=500){Url=new VRCUrl(url);Body=body;ErrorCode=code;}
}
public class VRCUrl {
 readonly string value; public VRCUrl(string s){value=s;} public string Get(){return value;}
 public static readonly VRCUrl Empty=new VRCUrl("");
 public static bool IsNullOrEmpty(VRCUrl u){return u==null||string.IsNullOrEmpty(u.value);}
}
public static class Utilities {public static bool IsValid(object o){return o!=null;}}
public static class Time {public static float time;public static int frameCount;}
public static class Mathf {
 public static int Min(int a,int b){return Math.Min(a,b);} public static int Max(int a,int b){return Math.Max(a,b);}
 public static float Max(float a,float b){return Math.Max(a,b);} public static int Abs(int a){return Math.Abs(a);}
 public static int RoundToInt(float a){return (int)Math.Round(a);} public static float Pow(float a,float b){return (float)Math.Pow(a,b);}
 public static int Clamp(int v,int lo,int hi){return Math.Max(lo,Math.Min(hi,v));}
}
public class Track {public VRCUrl Url;public bool IsValid(){return Url!=null;}public VRCUrl GetVRCUrl(){return Url;}}
public class Controller {
 public bool Stopped,IsLoading,Paused,IsLive;public float VideoTime,Duration=200000;
 public Track Track=new Track{Url=new VRCUrl("A")};public VRCUrl SyncedUrl=new VRCUrl("old");
 public object GetProgramVariable(string name){return SyncedUrl;}
}
public class YamaBiliPagesPlaylist3 {
 public Dictionary<int,VRCUrl> Catalog=new Dictionary<int,VRCUrl>();public int LastId,Lookups;
 public VRCUrl GetVcridUrl(int id){LastId=id;Lookups++;return Catalog.ContainsKey(id)?Catalog[id]:VRCUrl.Empty;}
}
public class Transform {
 public Transform parent;public YamaBiliPagesPlaylist3 Pages;
 public T GetComponentInChildren<T>(bool inactive) where T:class {return Pages as T;}
}
public class StubBehaviour : IUdonEventReceiver {
 public Transform transform=new Transform();public List<string> Sent=new List<string>();
 public List<float> DispatchTimes=new List<float>();public List<float> Delays=new List<float>();
 public virtual void OnStringLoadSuccess(IVRCStringDownload d){}public virtual void OnStringLoadError(IVRCStringDownload d){}
 public void SendCustomEventDelayedSeconds(string name,float seconds){Delays.Add(seconds);}
}
public static class VRCStringDownloader {
 public static void LoadUrl(VRCUrl u,IUdonEventReceiver r){var m=(StubBehaviour)r;m.Sent.Add(u.Get());m.DispatchTimes.Add(Time.time);}
}
'@
$scenarios = @'
public static class DanmakuScenarios {
 static int checks;static int maxFrames;
 static readonly BindingFlags flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
 static void Check(bool pass,string name){if(!pass)throw new Exception(name);checks++;}
 static object Get(ModuleHarness m,string n){return typeof(ModuleHarness).GetField(n,flags).GetValue(m);}
 static int I(ModuleHarness m,string n){return (int)Get(m,n);}static bool B(ModuleHarness m,string n){return (bool)Get(m,n);}
 static float F(ModuleHarness m,string n){return (float)Get(m,n);}
 static void Set(ModuleHarness m,string n,object v){typeof(ModuleHarness).GetField(n,flags).SetValue(m,v);}
 static object Call(ModuleHarness m,string n,params object[] args){return typeof(ModuleHarness).GetMethod(n,flags).Invoke(m,args);}
 static Controller Controller(ModuleHarness m){return (Controller)Get(m,"_controller");}
 static ModuleHarness New(string url="A") {
   Time.time=0;Time.frameCount=0;var m=new ModuleHarness();
   var c=new Controller();c.Track.Url=new VRCUrl(url);Set(m,"_controller",c);m.LoadCurrentTrackDanmaku();return m;
 }
 static void Tick(ModuleHarness m){Time.frameCount++;Call(m,"TickDanmakuParser");}
 static void At(ModuleHarness m,float time){Time.time=time;Time.frameCount++;Call(m,"Update");}
 static int Drain(ModuleHarness m) {
   int frames=0;
   while(I(m,"_parsePhase")!=0) {
     int phase=I(m,"_parsePhase"),pos=I(m,"_parsePosition"),rows=I(m,"_parseRows"),count=I(m,"_lineCount");
     Tick(m);frames++;if(frames>200000)throw new Exception("Parser did not terminate");
     if(phase>=2&&I(m,"_parsePhase")==phase) {
       Check(I(m,"_parsePosition")-pos<=8192,"bounded scan character budget");
       Check(I(m,"_parseRows")-rows<=128,"bounded row count budget");
       Check(I(m,"_lineCount")-count<=128,"bounded decoded row budget");
     }
   }
   maxFrames=Math.Max(maxFrames,frames);return frames;
 }
 static string Rows(int count,string header=null) {
   var b=new StringBuilder(header??("#YBDM/1\n#danmaku_status=complete\n#danmaku_complete=1\n#count="+count+"\n"));
   for(int i=0;i<count;i++)b.Append(i*1000).Append("\t1\t16777215\t25\t0\trow ").Append(i).Append('\n');
   return b.ToString();
 }
 static void Reply(ModuleHarness m,string body){m.OnStringLoadSuccess(new Download(m.Sent[m.Sent.Count-1],body));}
 static void Parsed(ModuleHarness m,string body){Reply(m,body);Drain(m);}
 static string Complete(string rows,int count){return "#YBDM/1\n#danmaku_status=complete\n#danmaku_complete=1\n#count="+count+"\n"+rows;}
 const string Pending="#YBDM/1\n#danmaku_status=pending\n#danmaku_complete=0\n#retry_after_seconds=0\n";
 const string Metadata="#YBDM/1\n#danmaku_status=metadata\n#danmaku_complete=0\n#provider=bili\n#vcrid=42\n#count=0\n";
 static YamaBiliPagesPlaylist3 Catalog(ModuleHarness m,string url) {
   var p=new YamaBiliPagesPlaylist3();p.Catalog[42]=new VRCUrl(url);Set(m,"_pagesPlaylist",p);return p;
 }
 static void NotLoaded(ModuleHarness m,string name){Check(!B(m,"_loaded")&&I(m,"_lineCount")==0,name);}
 static void Spacing(ModuleHarness m){for(int i=1;i<m.DispatchTimes.Count;i++)Check(m.DispatchTimes[i]-m.DispatchTimes[i-1]>=5,"5 second component dispatch spacing");}
 public static string Run() {
   foreach(int count in new[]{7200,100000}) {
     var m=New();Controller(m).VideoTime=count-100;
     Reply(m,Rows(count));Check(I(m,"_parsePhase")==1&&I(m,"_lineCount")==0,"callback does not scan/split body");
     int frames=Drain(m);Check(frames>count/128,"timeline spans bounded frames");
     Check(B(m,"_loaded")&&I(m,"_lineCount")==count,"full "+count+" row timeline");
     Check(((int[])Get(m,"_timeMs")).Length==count,"arrays sized by real rows");
     Check(((string[])Get(m,"_content"))[count-1]=="row "+(count-1),"last row preserved");
     Check(I(m,"_nextLine")==count-100,"late join binary seek uses latest player clock");
     Call(m,"SeekLineIndex",300);Check(I(m,"_nextLine")==0,"seek back to first row");
     Call(m,"SeekLineIndex",count*1000+1000);Check(I(m,"_nextLine")==count,"seek after end");
   }
   {
     var m=New();string escaped=@"slash\\n tab\t newline\n cr\r unknown\q end\\";
     Parsed(m,Complete("1000\t4\t1\t25\t0\t"+escaped+"\r\n1000\t4\t2\t25\t0\tsecond\r2000\t1\t3\t25\t0\tlast",3));
     var text=(string[])Get(m,"_content");Check(text[0]=="slash\\n tab\t newline\n cr\r unknown\\q end\\","escape sequences decoded once without corrupting literal slashes");
     Check(text[1]=="second"&&I(m,"_lineCount")==3,"CRLF/CR/EOF and equal timestamps retained");
     Call(m,"SeekLineIndex",1300);Check(I(m,"_nextLine")==0,"lower bound includes every equal timestamp");
     Check((int)Call(m,"GetFixedLyricLane",0,12)==2&&(int)Call(m,"GetFixedLyricLane",1,12)==1,"fixed lyric lanes retained");
     Check((float)Call(m,"GetFixedLyricDuration",0)==1f,"fixed lyric duration retained");
   }
   {
     var m=New();Parsed(m,Complete("0\t7\t1\t25\t0\tadvanced\n0\t1\t1\t25\t2\tpool\n0\t1\t1\t25\t0\tvisible",3));
     Check(B(m,"_loaded")&&I(m,"_lineCount")==1,"count validates all rows while renderer filters stay intact");
   }
   {
     var m=New();Parsed(m,Rows(2,"#YBDM/1\n"));Check(B(m,"_loaded")&&I(m,"_lineCount")==2,"legacy without count remains supported");
     m=New();Parsed(m,Complete("",0));Check(B(m,"_loaded")&&I(m,"_lineCount")==0&&!B(m,"_retryPending"),"complete zero is valid");
     m=New();Parsed(m,"#YBDM/1\n#count=0\n");Check(B(m,"_loaded"),"legacy zero is valid");
   }
   foreach(string headers in new[]{
     "#danmaku_status=complete\n#danmaku_complete=0\n#count=1\n",
     "#danmaku_status=complete\n#count=1\n",
     "#danmaku_status=complete\n#danmaku_complete=1\n",
     "#danmaku_complete=0\n#count=1\n",
     "#danmaku_status=metadata\n#danmaku_complete=1\n#vcrid=42\n",
     "#danmaku_status=metadata\n#vcrid=42\n",
     "#danmaku_status=partial\n#danmaku_complete=1\n#count=1\n",
     "#danmaku_status=pending\n#count=1\n",
     "#danmaku_status=unknown\n#count=1\n",
     "#danmaku_status=complete\n#danmaku_complete=1\n#count=2\n",
     "#danmaku_status=complete\n#danmaku_complete=1\n#count=-1\n"
   }) {
     var m=New();Catalog(m,"catalog");Parsed(m,"#YBDM/1\n"+headers+"0\t1\t1\t25\t0\tnever render\n");
     NotLoaded(m,"incomplete contract rejected: "+headers);Check(m.Sent.Count==1,"invalid metadata cannot follow");
   }
   {
     var m=New();Parsed(m,Rows(7200)+"#danmaku_status=error\n#error=late error\n#retryable=0\n");
     NotLoaded(m,"trailing error prevents any timeline publish");Check(!B(m,"_retryPending"),"retryable zero is permanent");
     m=New();Parsed(m,"#YBDM/1\n#error=legacy failure\n#retryable=0\n");NotLoaded(m,"legacy error not empty success");
     m=New();Parsed(m,Complete("2000\t1\t1\t25\t0\ta\n1000\t1\t1\t25\t0\tb",2));NotLoaded(m,"unsorted rows rejected before publish");
   }
   {
     var m=New();var p=Catalog(m,"catalog");int epoch=I(m,"_requestEpoch");Parsed(m,Metadata);
     Check(m.Sent.Count==1&&p.LastId==42&&!B(m,"_loaded"),"metadata uses selected catalog id, waits for SDK spacing");
     At(m,4.9f);Check(m.Sent.Count==1,"metadata follow not sent early");At(m,5);
     Check(m.Sent.Count==2&&m.Sent[1]=="catalog"&&I(m,"_requestEpoch")==epoch&&I(m,"_retryCount")==0,"one catalog follow keeps request and retry budget");
     Parsed(m,Metadata);At(m,10);Check(m.Sent.Count==2,"metadata follow cannot loop");Spacing(m);
   }
   {
     var m=New("A?__pkdm=2&url=x");Catalog(m,"catalog");Parsed(m,Metadata);At(m,20);
     Check(m.Sent.Count==1&&m.Status.Contains("protocol mismatch"),"V2 catalog mismatch fails without downgrade");
     m=New("A?url=x&__pkdm=2");Catalog(m,"catalog?__pkdm=2&vcrid=42");Parsed(m,Metadata);At(m,5);
     Check(m.Sent.Count==2&&m.Sent[1]=="catalog?__pkdm=2&vcrid=42","V2 follow uses exact prebuilt catalog URL");
     Parsed(m,Complete("",0));Check(B(m,"_loaded"),"V2 follow completes");
     m=New("A?__pkdm=2");Parsed(m,"<html>video redirect</html>");NotLoaded(m,"V2 marker is not itself a text envelope");
     m=New("A?__pkdm=2");Catalog(m,"catalog?__pkdm=20&vcrid=42");Parsed(m,Metadata);At(m,20);
     Check(m.Sent.Count==1,"V2 query value must be exactly two");
     m=New("A?__pkdm=2");Set(m,"_fallbackDanmakuUrl",new VRCUrl("legacy fallback"));Parsed(m,"no block");At(m,20);
     Check(m.Sent.Count==1&&m.Status.Contains("protocol mismatch"),"configured fallback cannot silently downgrade V2");
     m=New("A?__pkdm=2");Parsed(m,Rows(1,"#YBDM/1\n"));Check(B(m,"_loaded"),"disabled server V2 feature remains legacy compatible");
   }
   {
     var m=New();Parsed(m,Metadata);Check(m.Status.Contains("missing from catalog")&&m.Sent.Count==1,"missing catalog is terminal");
     m=New();var p=new YamaBiliPagesPlaylist3();p.Catalog[42]=new VRCUrl("catalog");m.transform.parent=new Transform{Pages=p};
     Parsed(m,Metadata);At(m,5);Check(m.Sent.Count==2,"parent/child catalog lookup fallback");
     m=New();Catalog(m,"catalog");Parsed(m,Metadata.Replace("provider=bili","provider=netease"));At(m,5);Check(m.Sent.Count==1,"non-Bili metadata never followed");
   }
   {
     var m=New();Parsed(m,Pending);Check(I(m,"_retryCount")==1&&F(m,"_retryDueAt")==5,"local backoff wins zero hint");
     At(m,4);Check(m.Sent.Count==1,"no early retry");At(m,5);Check(m.Sent.Count==2,"retry at deadline");
     Parsed(m,Pending);Check(I(m,"_retryCount")==2&&F(m,"_retryDueAt")==15,"second local backoff");
     At(m,15);Parsed(m,Pending);Check(I(m,"_retryCount")==3&&F(m,"_retryDueAt")==35,"third local backoff");
     At(m,35);Parsed(m,Pending);At(m,1000);Check(m.Sent.Count==4&&I(m,"_retryCount")==3&&!B(m,"_retryPending"),"at most three retries");
     m.LoadCurrentTrackDanmaku();Check(I(m,"_retryCount")==0,"manual reload resets retry budget");Spacing(m);
     m=New();Parsed(m,Pending.Replace("#retry_after_seconds=0\n",""));Check(F(m,"_retryDueAt")==180,"missing hint waits 180 seconds");
     m=New();Parsed(m,Pending.Replace("seconds=0","seconds=300"));Check(F(m,"_retryDueAt")==300,"server cooldown wins local backoff");
   }
   {
     var m=New();Catalog(m,"catalog");Parsed(m,Pending);At(m,5);int epoch=I(m,"_requestEpoch");Parsed(m,Metadata);At(m,10);
     Check(I(m,"_retryCount")==1&&I(m,"_requestEpoch")==epoch,"follow after retry does not reset or consume budget");
     Parsed(m,Pending);Check(I(m,"_retryCount")==2,"catalog errors share original retry budget");Spacing(m);
   }
   foreach(int code in new[]{403,404,500}) {
     var m=New();m.OnStringLoadError(new Download("A",null,code){ResultUnavailable=true});
     Check(B(m,"_retryPending")== (code==500),"HTTP code permanence with unavailable error.Result");
     Check(I(m,"_retryCount")== (code==500?1:0),"HTTP failure retry count");
   }
   {
     var m=New();Parsed(m,"#YBDM/1\n#danmaku_status=error\n#http_status=403\n#retryable=1\n");
     Check(!B(m,"_retryPending"),"permanent body HTTP status wins retryable hint");
     m=New();Set(m,"_fallbackDanmakuUrl",new VRCUrl("fallback"));Parsed(m,Pending);At(m,5);
     m.OnStringLoadError(new Download("A"));At(m,10);
     Check(m.Sent[m.Sent.Count-1]=="fallback"&&I(m,"_retryCount")==1,"fallback preserves request retry budget");
     Parsed(m,Pending);Check(I(m,"_retryCount")==2,"fallback retry stays bounded");Spacing(m);
     m=New();Set(m,"_fallbackDanmakuUrl",new VRCUrl("fallback"));Parsed(m,"ordinary video response");At(m,5);
     Check(m.Sent.Count==2&&m.Sent[1]=="fallback","missing block retains configured fallback");Parsed(m,Complete("",0));Check(B(m,"_loaded"),"fallback response accepted");
   }
   {
     var m=New();Reply(m,Rows(100000));Tick(m);Tick(m);m.ClearDanmaku();Drain(m);
     NotLoaded(m,"clear cancels scanning");Check((string)Get(m,"_parseText")=="","clear releases source buffer");
     m=New();Reply(m,Rows(7200));while(I(m,"_parsePhase")!=3)Tick(m);Tick(m);
     Check(I(m,"_lineCount")>0&&!B(m,"_loaded"),"partly decoded timeline remains unpublished");
     Controller(m).Track.Url=new VRCUrl("B");Tick(m);NotLoaded(m,"playback change cancels parsing before Update");
     m=New();Reply(m,Rows(7200));while(I(m,"_parsePhase")!=3)Tick(m);Tick(m);m.SetExternalAudioMode(true);
     NotLoaded(m,"provider switch cancels parsing");m.LoadDanmakuUrl(new VRCUrl("lyrics"));At(m,5);
     Parsed(m,"#YBDM/1\n0\t4\t1\t25\t0\tlyric\n");Check(B(m,"_loaded")&&I(m,"_lineCount")==1,"explicit audio lyrics still load");
   }
   {
     var m=New();m.OnStringLoadSuccess(new Download("wrong",Rows(1)));Check(B(m,"_downloadInFlight"),"unrelated callback does not consume active request");
     At(m,30);Check(m.Sent.Count==1&&B(m,"_downloadInFlight"),"global queue delay has no invented timeout");
     Parsed(m,Rows(1));Check(B(m,"_loaded"),"late valid callback accepted");
     m.OnStringLoadSuccess(new Download("A",Rows(2)));Check(I(m,"_lineCount")==1,"completed duplicate ignored");
     m=New();Controller(m).Track.Url=new VRCUrl("B");m.LoadDanmakuUrl(new VRCUrl("B"));Controller(m).Track.Url=new VRCUrl("A");m.LoadDanmakuUrl(new VRCUrl("A"));
     Check(m.Sent.Count==1,"A-B-A does not overlap SDK requests");Time.time=15;m.OnStringLoadSuccess(new Download("A",Rows(2)));
     Check(m.Sent.Count==2&&I(m,"_parsePhase")==0,"old A drained, latest A dispatched without parsing old body");
     Parsed(m,Rows(1));Check(I(m,"_lineCount")==1,"latest A accepted");Spacing(m);
   }
   {
     var m=New();Parsed(m,Pending.Replace("seconds=0","seconds=20"));
     Time.time=1;Controller(m).Track.Url=new VRCUrl("B");m.LoadDanmakuUrl(new VRCUrl("B"));
     Time.time=2;Controller(m).Track.Url=new VRCUrl("A");m.LoadDanmakuUrl(new VRCUrl("A"));At(m,5);
     Parsed(m,Pending.Replace("seconds=0","seconds=180"));Check(F(m,"_retryDueAt")==185,"new A owns new retry deadline");
     Time.time=20;m.RetryDanmakuDownload();Check(m.Sent.Count==2&&I(m,"_retryCount")==1,"old delayed wake cannot bypass new A deadline");
     Time.time=185;m.RetryDanmakuDownload();m.RetryDanmakuDownload();Check(m.Sent.Count==3,"due wake dispatches once");Spacing(m);
     m=New();Parsed(m,Pending);Controller(m).Track.Url=new VRCUrl("B");Time.time=100;m.RetryDanmakuDownload();
     Check(m.Sent.Count==1&&!B(m,"_retryPending"),"retry checks live playback before dispatch");
     m=New();Parsed(m,Pending);m.SetExternalAudioMode(true);Time.time=100;m.RetryDanmakuDownload();Check(m.Sent.Count==1,"provider switch cancels delayed retry");
   }
   {
     var m=New();Controller(m).Paused=true;Reply(m,Rows(7200));At(m,1);Drain(m);Check(B(m,"_loaded"),"parsing proceeds while paused");
     m=New();Controller(m).Stopped=true;Controller(m).IsLoading=true;Parsed(m,Rows(1));Check(B(m,"_loaded"),"stopped/loading transition still accepts active request");
     m=New();Controller(m).Stopped=true;Reply(m,Rows(1));NotLoaded(m,"actual stop rejects callback");
     m=New();m.ClearDanmaku();Set(m,"_loadFromCurrentYamaPlayerUrl",false);At(m,1);int epoch=I(m,"_requestEpoch");At(m,2);Check(I(m,"_requestEpoch")==epoch,"empty fallback does not reset every frame");
   }
   {
     var m=New();string longText=new string('x',200000)+@"\tend";Reply(m,Complete("0\t1\t1\t25\t0\t"+longText,1));
     Tick(m);Tick(m);Check(!B(m,"_loaded"),"one huge line remains incremental");Drain(m);
     Check(((string[])Get(m,"_content"))[0]==new string('x',200000)+"\tend","no content size truncation");
     m=New();Parsed(m,new string('x',200000)+"<script type=\"application/yama-bili-danmaku\">"+Complete("0\t1\t1\t25\t0\t&lt;tag&gt; &amp; &quot;q&quot;",1)+"</script><div>ignored</div>");
     Check(((string[])Get(m,"_content"))[0]=="<tag> & \"q\"","incremental HTML wrapper discovery and entity decoding");
     m=New();Parsed(m,"<script type=\"application/yama-bili-danmaku\">"+Complete("",0));NotLoaded(m,"truncated HTML block rejected");
     m=New();Parsed(m,null);NotLoaded(m,"null success body tolerated");
   }
   return "PASS Danmaku: "+checks+" actual-method assertions; largest parse "+maxFrames+" frames. NOT Unity/UdonSharp/VRChat validation.";
 }
}
'@
$harness = $common + "`npublic class ModuleHarness : StubBehaviour {`n" + $body + "`n" + $stubs + "`n}`n" + $scenarios
Add-Type -TypeDefinition $harness -IgnoreWarnings -WarningAction SilentlyContinue
[DanmakuScenarios]::Run()
