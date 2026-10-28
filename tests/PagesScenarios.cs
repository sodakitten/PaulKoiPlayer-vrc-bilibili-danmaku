#if PAGES_BACKEND_SYNC_TESTS
namespace PagesBackendSync
{
    public static class Scenarios
    {
        private static int count;
        private const string Source = "https://example.test/player/?__pkdm=2&url=video%3Fp%3D10";
        private const string Catalog = "https://example.test/player/?__pkdm=2&vcrid=";
        private const string Song = "{\"type\":\"netease-song\",\"provider\":\"netease\",\"pages\":[{\"page\":1,\"vcrid\":1}]}";
        private const string RetryBody = "#YBDM/1\n#danmaku_status=error\n#danmaku_complete=0\n#retryable=1\n#retry_after_seconds=30\n";
        private static object Get(object o, string n) { return o.GetType().GetField(n, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(o); }
        private static T Get<T>(object o, string n) { return (T)Get(o, n); }
        private static void Set(object o, string n, object v) { o.GetType().GetField(n, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(o, v); }
        private static object Call(object o, string n, params object[] a) { return o.GetType().GetMethod(n, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance).Invoke(o, a); }
        private static void Check(bool pass, string name) { if (!pass) throw new Exception(name); count++; }
        private static PagesHarness Fresh(out Controller c)
        {
            Time.time = 0;
            var p = new PagesHarness(); c = new Controller(); c.Track.Url = new VRCUrl(Source);
            Set(p, "_controller", c);
            var urls = new VRCUrl[100]; for (int i = 1; i < urls.Length; i++) urls[i] = new VRCUrl(Catalog + i);
            Set(p, "_vcridUrls", urls); Set(p, "_danmakuModule", new YamaBiliDanmakuModule3());
            return p;
        }
        private static void Begin(PagesHarness p, string url = Source, int mode = 1) { Call(p, "BeginUnifiedSourceRequest", new VRCUrl(url), mode, false); }
        private static void TickRetry(PagesHarness p) { Time.time = Get<float>(p, "_pagesRetryDueAt"); p.RetryPagesDownload(); }
        private static void Ok(PagesHarness p, string body, string url = Source) { p.OnStringLoadSuccess(new Download(url, body)); }
        private static void Error(PagesHarness p, int code = 500, string url = Source) { p.OnStringLoadError(new Download(url, "", code) { ThrowOnResult = true }); }
        private static string Manifest(string status = "metadata", string retryable = "1", bool page = true)
        {
            var b = new StringBuilder("#YBDM/1\n#manifest_type=bilibili-pages\n#vcrid=10\n#title=Video title\n#part=Part 10\n");
            if (page) b.Append("#page=10\n");
            b.Append("#danmaku_status=" + status + "\n#danmaku_complete=" + (status == "complete" ? "1\n#count=0" : "0") + "\n#retryable=" + retryable + "\n#retry_after_seconds=180\n#columns=progressMs\tmode\tcolor\tfontSize\tpool\tcontent\n");
            for (int i = 1; i <= 12; i++) b.Append("#page_item=" + i + "\t123\t99\tPart " + i + "\t" + i + "\n");
            return b.ToString();
        }
        private static void ParsingAndCompletion()
        {
            foreach (var status in new[] { "metadata", "error", "complete" }) foreach (var retry in new[] { "0", "1" })
            {
                Controller c; var p = Fresh(out c); Begin(p, Source, 2); Ok(p, Manifest(status, retry));
                Check(Get<int>(p, "_biliManifestTotalPages") == 12, status + ": full 12P list");
                Check(Get<int>(p, "_biliManifestSelectedIndex") == 9, status + ": selected P10, not P1");
                Check(Get<string[]>(p, "_biliManifestParts")[9] == "Part 10" && c.Track.Title == "Part 10", "selected title retained");
                Check(Get<string>(p, "_biliManifestTitle") == "Video title", "manifest title retained");
                Check(p.PlayedManifest == -1 && c.Played == 0 && p.AddedParsed == 0 && c.Queue.Added == 0, "expand does not switch or duplicate queue");
                Ok(p, Manifest(status, retry)); p.RetryPagesDownload(); Call(p, "WatchUnifiedQueue");
                Check(p.ManifestEntries == 1 && p.Sent.Count == 1 && !Get<bool>(p, "_pagesRetryPending"), "valid list completes once regardless of comment error");
            }
            Controller controller; var parser = Fresh(out controller);
            Check((bool)Call(parser, "ParsePagesMetadata", Manifest("metadata", "1", false)), "vcrid-only selection parses");
            Check(Get<int>(parser, "_selectedIndex") == 9 && Get<int>(parser, "_pageOffset") == 6, "no #page uses selected vcrid 10");
            string custom = "#YBDM/1\n#provider=custom\n#manifest_type=bilibili-video\n#vcrid=8643\n#title=Custom\\tTitle\n#count=1\n0\t1\t16777215\t25\t0\tcomment\n";
            Check((bool)Call(parser, "ParsePagesMetadata", custom), "legacy custom no part/page_item accepted");
            Check(Get<int[]>(parser, "_vcrids")[0] == 8643, "custom ID retains first digit");
            Check(Get<string[]>(parser, "_parts")[0] == "Custom\tTitle", "custom title not replaced by P1");
            foreach (var invalid in new[] { "#YBDM/1\n", RetryBody, "#YBDM/1\n#vcrid=8643\n#error=missing\n", "#YBDM/1\n#error=missing\n#title=Error\n" })
            {
                Check(!(bool)Call(parser, "ParsePagesMetadata", invalid), "error-only body is not a fabricated item");
                Check(Get<int[]>(parser, "_vcrids")[0] == 8643, "failed parse preserves usable item");
            }
            var p2 = Fresh(out controller); controller.Stopped = true; Begin(p2); Ok(p2, Manifest());
            Check(p2.PlayedManifest == 9 && p2.ManifestEntries == 1, "enqueue stopped uses existing selected-P10 playback logic");
            p2 = Fresh(out controller); controller.Queue.Items.Add(Track.New(0, "", new VRCUrl(Source)));
            p2.NormalizeNextQueuedTrack(); Ok(p2, Manifest());
            Check(controller.Queue.GetTrack(0).HasTitle() && p2.PlayedManifest == -1, "normalize titles placeholder without playback");
            p2 = Fresh(out controller); Set(p2, "_useUnifiedQueue", false); Call(p2, "BeginPagesRequest", new VRCUrl(Catalog + 10)); Ok(p2, Manifest("error", "0"), Catalog + 10);
            Check(Get<int>(p2, "_totalPages") == 12 && Get<int>(p2, "_selectedIndex") == 9, "direct vcrid error with valid list parses");
        }
        private static void HeaderAndCatalog()
        {
            Controller c; var p = Fresh(out c); string head = Manifest();
            var body = new StringBuilder(head); for (int i = 0; i < 100000; i++) body.Append("1000\t1\t16777215\t25\t0\tcomment\n");
            body.Append("#title=body injection\n#retry_after_seconds=1\n"); string payload = body.ToString();
            long before = GC.GetAllocatedBytesForCurrentThread();
            string extracted = (string)Call(p, "ExtractPagesMetadataHeader", payload);
            Call(p, "ParsePagesMetadata", payload);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Check(extracted == head && allocated < 250000, "100k TSV rows are not split or copied by header parsing");
            Check(Get<string>(p, "_parsedSourceTitle") == "Video title", "body cannot overwrite header title");
            Check((bool)Call(p, "ParsePagesMetadata", head.Replace("\n", "\r\n")), "CRLF header accepted");
            Check((bool)Call(p, "ParsePagesMetadata", head.Replace("\n", "\r")), "CR header accepted");
            string list = "#YBDM/1\n#manifest_type=bilibili-list\n#page=10\n#list_item=1\t1\tBV\t1\t1\tOne\t1\n#list_item=10\t1\tBV\t1\t1\tTen\t10\n";
            Check((bool)Call(p, "ParsePagesMetadata", list) && Get<int>(p, "_selectedIndex") == 1 && Get<string[]>(p, "_parts")[1] == "Ten", "legacy list_item layout retained");
            var stored = Get<VRCUrl[]>(p, "_vcridUrls");
            Check(object.ReferenceEquals(p.GetVcridUrl(10), stored[10]), "accessor returns stored prebuilt object");
            foreach (int id in new[] { -1, 0, 100, 1000001, int.MaxValue }) Check(VRCUrl.IsNullOrEmpty(p.GetVcridUrl(id)), "catalog boundary " + id);
            Set(p, "_vcridMax", 5); Check(VRCUrl.IsNullOrEmpty(p.GetVcridUrl(10)), "serialized lower max honored");
            Set(p, "_vcridMax", int.MaxValue); var large = new VRCUrl[1000002]; large[1000000] = new VRCUrl("limit"); large[1000001] = new VRCUrl("out"); Set(p, "_vcridUrls", large);
            Check(p.GetVcridUrl(1000000) == large[1000000] && VRCUrl.IsNullOrEmpty(p.GetVcridUrl(1000001)), "hard catalog ceiling despite invalid serialized max");
            Set(p, "_vcridUrls", null); Check(VRCUrl.IsNullOrEmpty(p.GetVcridUrl(1)), "null catalog");
            foreach (string url in new[] { Source, "https://example.test/player/?url=video&__pkdm=2", Catalog + 10, "https://example.test/player/?vcrid=10&__pkdm=2" })
                Check((bool)Call(p, "IsPagesRequestUrl", url), "legacy and opt-in parameter ordering recognized");
            Check((bool)Call(p, "IsManifestSourceRequestUrl", Source) && (bool)Call(p, "IsVcridPlaybackUrl", Catalog + 10), "V2 source and catalog detection");
            Check(!(bool)Call(p, "IsUsableRequestUrl", new VRCUrl("https://example.test/player/?__pkdm=2&url=")), "empty V2 prefix is not a request");
        }
        private static void RetryAndTimeout()
        {
            Controller c; var p = Fresh(out c); Begin(p); Error(p);
            Check(Get<float>(p, "_pagesRetryDueAt") == 180 && c.Queue.Added == 0, "network error uses 180s fallback without reading Result");
            Time.time = 179; p.RetryPagesDownload(); p.CheckPagesTimeout(); Check(p.Sent.Count == 1 && c.Queue.Added == 0, "early retry/old timeout cannot enqueue or dispatch");
            for (int i = 1; i <= 3; i++) { TickRetry(p); Error(p); }
            Check(p.Sent.Count == 4 && c.Queue.Added == 1 && !Get<bool>(p, "_requestActive"), "initial plus at most three retries then one fallback");
            p.RetryPagesDownload(); Error(p); p.CheckPagesTimeout(); Check(c.Queue.Added == 1 && p.Sent.Count == 4, "terminal callbacks cannot duplicate fallback");
            p = Fresh(out c); Begin(p); Time.time = 100; Ok(p, RetryBody);
            Check(Get<float>(p, "_pagesRetryDueAt") == 130, "server remaining seconds are measured at receipt, not request start");
            Time.time = 129; p.RetryPagesDownload(); Check(p.Sent.Count == 1, "body cooldown not elapsed");
            TickRetry(p); Ok(p, Manifest()); p.RetryPagesDownload(); Check(p.Sent.Count == 2 && p.QueuedManifest == 1, "success cancels pending retry");
            p = Fresh(out c); Begin(p);
            foreach (float delay in new[] { 5f, 10f, 20f })
            {
                Ok(p, RetryBody.Replace("=30", "=1")); Check(Get<float>(p, "_pagesRetryDueAt") == Time.time + delay, "minimum local backoff " + delay); TickRetry(p);
            }
            Error(p); Check(p.Sent.Count == 4 && c.Queue.Added == 1, "local backoff does not reset retry cap");
            foreach (int code in new[] { 403, 404 })
            {
                p = Fresh(out c); Begin(p); Error(p, code); Check(p.Sent.Count == 1 && c.Queue.Added == 1 && !Get<bool>(p, "_pagesRetryPending"), "permanent HTTP " + code);
            }
            p = Fresh(out c); Begin(p); Ok(p, RetryBody.Replace("#retryable=1", "#retryable=0")); Check(c.Queue.Added == 1 && !Get<bool>(p, "_requestActive"), "permanent body error stops");
            p = Fresh(out c); Begin(p); Time.time = 15; p.CheckPagesTimeout();
            Check(Get<bool>(p, "_downloadInFlight") && Get<bool>(p, "_pagesRetryPending") && c.Queue.Added == 0, "timeout reserves retry and keeps SDK slot");
            Time.time = 200; p.RetryPagesDownload(); Check(p.Sent.Count == 1, "retry waits for SDK callback drain even after due");
            Ok(p, Manifest()); p.RetryPagesDownload(); Check(p.QueuedManifest == 1 && p.Sent.Count == 1, "usable late response completes once and cancels retry");
            p = Fresh(out c); Begin(p); Time.time = 15; p.CheckPagesTimeout(); Time.time = 200; Error(p);
            Check(Get<int>(p, "_pagesRetryCount") == 1, "timeout and late failure consume only one retry");
            p.RetryPagesDownload(); Check(p.Sent.Count == 2, "drained timeout dispatches due retry");
            for (int i = 0; i < 2; i++) { Error(p); TickRetry(p); }
            Time.time += 15; p.CheckPagesTimeout(); Check(c.Queue.Added == 1 && Get<bool>(p, "_downloadInFlight"), "exhausted timeout falls back once but retains SDK slot");
            Begin(p); p.CheckPagesTimeout(); Check(p.Sent.Count == 4 && c.Queue.Added == 1, "next request cannot timeout before dispatch");
            Error(p); Check(p.Sent.Count == 5 && Get<bool>(p, "_requestActive"), "old callback drains before new same-URL request");
            Ok(p, Manifest()); Check(p.QueuedManifest == 1 && c.Queue.Added == 1, "new epoch accepts only new response");
            p = Fresh(out c); Begin(p); Error(p, 404); Begin(p);
            Check(p.Sent.Count == 1 && p.Events.Contains("StartPendingPagesDownload"), "SDK 5s spacing schedules a wake without Update");
            Time.time = 4; p.StartPendingPagesDownload(); Check(p.Sent.Count == 1, "SDK spacing rejects early wake");
            Time.time = 5; p.StartPendingPagesDownload(); Check(p.Sent.Count == 2, "SDK delayed wake dispatches");
        }
        private static void GuardsAndAutoPaths()
        {
            Controller c; var p = Fresh(out c);
            c.Track.Url = new VRCUrl(Catalog + 10); Call(p, "WatchUnifiedQueue");
            Check(p.Sent.Count == 1 && p.Sent[0] == Catalog + 10, "native direct VCRID starts manifest without prior source URL");
            Ok(p, Manifest("error", "0"), Catalog + 10);
            Check(Get<int>(p, "_biliManifestTotalPages") == 12 && Get<int>(p, "_biliManifestSelectedIndex") == 9 && c.Played == 0,
                "direct VCRID comment failure still expands 12P without reloading video");
            c.Track.Url = new VRCUrl(Catalog + 11); Call(p, "WatchUnifiedQueue");
            Check(p.Sent.Count == 1, "known next P does not refetch active manifest");
            c.Track.Url = new VRCUrl(Catalog + 50); Time.time = 5; Call(p, "WatchUnifiedQueue");
            Check(p.Sent.Count == 2 && p.Sent[1] == Catalog + 50, "foreign VCRID refreshes stale previous BV manifest");
            var other = new StringBuilder("#YBDM/1\n#manifest_type=bilibili-pages\n#title=Another video\n#page=10\n#vcrid=50\n#danmaku_status=error\n#danmaku_complete=0\n#retryable=0\n");
            for (int i = 1; i <= 12; i++) other.Append("#page_item=" + i + "\t999\t99\tAnother " + i + "\t" + (40 + i) + "\n");
            Ok(p, other.ToString(), Catalog + 50);
            Check(Get<int[]>(p, "_biliManifestVcrids")[9] == 50 && c.Track.Title == "Another 10" && p.ManifestEntries == 2,
                "foreign VCRID applies its own title and selected ID");
            Call(p, "WatchUnifiedQueue"); Check(p.Sent.Count == 2, "foreign VCRID completes only once");
            p = Fresh(out c); Set(p, "_useUnifiedQueue", false); Call(p, "ParsePagesMetadata", Manifest());
            c.Track.Url = new VRCUrl(Catalog + 50); Call(p, "BeginPagesRequest", c.Track.Url); Ok(p, other.ToString(), Catalog + 50);
            Check(Get<int[]>(p, "_vcrids")[9] == 50, "standalone foreign VCRID does not keep unrelated old list");
            p = Fresh(out c); Begin(p, Source, 2); Error(p);
            c.OwnerId = 2; TickRetry(p); Check(!Get<bool>(p, "_requestActive") && p.Sent.Count == 1, "ownership loss cancels retry");
            c.OwnerId = 1; p.RetryPagesDownload(); Check(p.Sent.Count == 1, "ownership ABA does not revive retry");
            p = Fresh(out c); Begin(p, Source, 2); c.Track.Url = new VRCUrl("B"); Call(p, "CancelInvalidPagesRequest"); c.Track.Url = new VRCUrl(Source); Begin(p, Source, 2);
            Check(p.Sent.Count == 1, "URL ABA retains old singleflight"); Time.time = 5; Ok(p, Manifest());
            Check(p.Sent.Count == 2 && p.ManifestEntries == 0, "stale callback drained without parsing"); Ok(p, Manifest());
            Check(p.ManifestEntries == 1, "new epoch accepted after ABA drain");
            p = Fresh(out c); Begin(p); Error(p); Call(p, "CancelUnifiedQueueRequest"); TickRetry(p); Check(p.Sent.Count == 1 && c.Queue.Added == 0, "explicit clear cancels retry without fallback");
            p = Fresh(out c); c.Queue.Items.Add(Track.New(0, "", new VRCUrl(Source))); p.NormalizeNextQueuedTrack(); Error(p); c.Queue.Items.Clear(); TickRetry(p);
            Check(p.Sent.Count == 1 && !Get<bool>(p, "_requestActive"), "removed queue item cancels retry");
            p = Fresh(out c); Begin(p); c.OwnerId = 2; Ok(p, Manifest()); Check(p.QueuedManifest == 1, "explicit enqueue retains original cross-owner semantics");
            p = Fresh(out c); Call(p, "WatchUnifiedQueue"); Error(p); for (int i = 0; i < 3; i++) { TickRetry(p); Error(p); }
            for (int i = 0; i < 5; i++) Call(p, "WatchUnifiedQueue");
            Check(p.Sent.Count == 4 && Get<string>(p, "_expandedPlaybackSourceUrl") == Source, "watcher cannot bypass exhausted retry budget");
            p = Fresh(out c); c.Queue.Items.Add(Track.New(0, "", new VRCUrl(Source))); p.NormalizeNextQueuedTrack(); Error(p); for (int i = 0; i < 3; i++) { TickRetry(p); Error(p); }
            for (int i = 0; i < 5; i++) p.NormalizeNextQueuedTrack();
            Check(p.Sent.Count == 4 && c.Queue.GetTrack(0).HasTitle(), "normalizer cannot bypass exhausted retry budget");
            p = Fresh(out c); Set(p, "_useUnifiedQueue", false); Call(p, "ParsePagesMetadata", Manifest()); Call(p, "BeginPagesRequest", new VRCUrl(Source));
            Ok(p, RetryBody); TickRetry(p); Error(p, 404);
            Check(Get<int>(p, "_totalPages") == 12 && Get<int>(p, "_selectedIndex") == 9 && Get<string[]>(p, "_parts")[9] == "Part 10", "unavailable comments/list refresh never destroy usable manifest");
            p = Fresh(out c); Set(p, "_syncedManifestActive", true); Set(p, "_syncedManifestLoadPending", true); Set(p, "_syncedManifestUrl", new VRCUrl(Source)); Call(p, "BeginPagesRequest", new VRCUrl(Source));
            c.Track.Url = new VRCUrl(Catalog + 10); Ok(p, Manifest()); Check(Get<int>(p, "_totalPages") == 12, "selection-only synced URL change preserves request ownership");
            p = Fresh(out c); Set(p, "_syncedManifestActive", true); Set(p, "_syncedManifestLoadPending", true); Set(p, "_syncedManifestUrl", new VRCUrl(Source)); Call(p, "BeginPagesRequest", new VRCUrl(Source)); Set(p, "_syncedManifestUrl", new VRCUrl("B"));
            Ok(p, Manifest()); Check(Get<int>(p, "_totalPages") == 0, "replaced synced manifest callback rejected");
        }
        private static void Netease()
        {
            Controller c; var p = Fresh(out c);
            string json = "{\"type\":\"netease-playlist\",\"provider\":\"netease\",\"title\":\"Playlist\",\"selectedPage\":2,\"pages\":[{\"page\":1,\"vcrid\":1,\"name\":\"One\"},{\"page\":2,\"vcrid\":2,\"name\":\"Two\",\"artist\":\"Artist\"}]}";
            Check((bool)Call(p, "ParsePagesJson", json), "NetEase JSON accepted");
            Check(Get<int>(p, "_selectedIndex") == 1 && Get<bool>(p, "_parsedShouldCycle") && Get<string[]>(p, "_parts")[1] == "Two - Artist", "NetEase selection/cycle/title preserved");
            Call(p, "LoadSelectedNeteaseDanmaku"); var module = Get<YamaBiliDanmakuModule3>(p, "_danmakuModule");
            Check(module.AudioMode && module.Lyrics.Count == 1 && module.Lyrics[0] == p.GetVcridUrl(2), "NetEase lyrics use stored catalog URL");
            Call(p, "LoadSelectedNeteaseDanmaku"); Check(module.Lyrics.Count == 1, "lyrics duplicate guard preserved");
            p = Fresh(out c); Begin(p); Ok(p, Song); Check(p.AddedParsed == 0 && p.Sent.Count == 1, "song metadata waits for SDK spacing");
            Time.time = 5; p.StartPendingPagesDownload(); Check(p.Sent.Count == 2 && p.Sent[1] == Catalog + 1, "song metadata dispatches from prebuilt catalog");
            Ok(p, "#YBDM/1\n#title=Song title\n#artist=Artist\n#danmaku_status=error\n#danmaku_complete=0\n", Catalog + 1);
            Check(p.AddedParsed == 1 && Get<string[]>(p, "_parts")[0] == "Song title - Artist", "metadata title accepted despite unavailable lyrics");
            p = Fresh(out c); Begin(p); Ok(p, Song); Time.time = 5; p.StartPendingPagesDownload(); Error(p, 500, Catalog + 1);
            for (int i = 0; i < 3; i++) { TickRetry(p); Error(p, 500, Catalog + 1); }
            Check(p.Sent.Count == 5 && p.AddedParsed == 1 && Get<int>(p, "_totalPages") == 1 && c.Queue.Added == 0, "metadata cap completes existing song, not raw duplicate");
            p = Fresh(out c); Begin(p); Ok(p, Song); Time.time = 5; p.StartPendingPagesDownload(); Time.time = 20; p.CheckUnifiedMetadataTimeout();
            Check(p.AddedParsed == 0 && Get<bool>(p, "_downloadInFlight") && Get<bool>(p, "_pagesRetryPending"), "metadata timeout preserves list and in-flight ownership");
            Time.time = 200; p.RetryPagesDownload(); Check(p.Sent.Count == 2, "metadata retry waits for drain"); Error(p, 404, Catalog + 1);
            Check(p.AddedParsed == 1 && Get<int>(p, "_totalPages") == 1, "permanent metadata failure keeps usable list");
            p = Fresh(out c); Begin(p, Source, 2); Ok(p, Song); Time.time = 5; p.StartPendingPagesDownload(); c.Track.Url = new VRCUrl("B"); Ok(p, "#YBDM/1\n#title=Old song\n", Catalog + 1);
            Check(p.AddedParsed == 0 && c.Track.Title == "", "stale metadata cannot title different playback");
        }
        public static int Run()
        {
            count = 0; ParsingAndCompletion(); HeaderAndCatalog(); RetryAndTimeout(); GuardsAndAutoPaths(); Netease(); return count;
        }
    }
}
#endif
