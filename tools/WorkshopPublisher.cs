// Author-only Steam Workshop uploader. Never included in the mod package.
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Xml.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Steamworks;

internal static class WorkshopPublisher
{
    private const uint App = 4348910;
    private const ulong ExistingItem = 3835383562;
    private static string root, release, stateFile, version;
    private static string language;
    private static string PageFile { get { return Path.Combine(release,
        language == "schinese" ? "工坊页面.json" : "工坊页面_" + language + ".json"); } }
    private static JObject state;
    private static readonly AppId_t AppId = new AppId_t(App);
    private static UGCUpdateHandle_t update;
    private static bool hasUpdate;

    private static int Main(string[] args)
    {
        Console.OutputEncoding = new UTF8Encoding(false);
        if (args.Length < 2 || !new[] { "check", "inspect", "publish", "metadata", "gallery", "verify" }.Contains(args[0]))
        { Console.Error.WriteLine("Usage: WorkshopPublisher check|inspect|publish|metadata|gallery|verify <project-root> [expected-SteamID64] [schinese|english|japanese]"); return 2; }
        language = args.Length > 3 ? args[3] : "schinese";
        Require(new[] { "schinese", "english", "japanese" }.Contains(language), "Unsupported Workshop language.");
        root = Path.GetFullPath(args[1]);
        version = XDocument.Load(Path.Combine(root,"package","XiaomanVisitor","manifest.xml")).Root.Element("ModVersion").Value;
        Require(Regex.IsMatch(version,@"^\d+(?:\.\d+)*$"), "Invalid release version.");
        release = Path.Combine(root, "release", version);
        stateFile = Path.Combine(release, "workshop-publish-state.json");
        state = File.Exists(stateFile) ? JObject.Parse(File.ReadAllText(stateFile)) : new JObject();
        if (!SteamAPI.Init()) { Console.Error.WriteLine("SteamAPI.Init failed. No item was created."); return 10; }
        try
        {
            ulong owner = SteamUser.GetSteamID().m_SteamID;
            Require(SteamUser.BLoggedOn(), "Steam client is not signed in online.");
            Require(SteamUtils.GetAppID().m_AppId == App, "Unexpected Steam app context.");
            Require(args.Length < 3 || string.IsNullOrEmpty(args[2]) || owner == ulong.Parse(args[2]), "Signed-in Steam account does not match the active desktop account.");
            Require(SteamApps.BIsSubscribedApp(AppId), "The active account does not have access to the target game.");
            Console.WriteLine("Steam session verified: " + SteamFriends.GetPersonaName() + " / " + owner + " / app " + App + ".");
            if (args[0] == "check") return 0;
            state["owner_steamid"] = owner.ToString(); state["appid"] = App;
            if (args[0] == "inspect") { Inspect(PublishedId(), owner); return 0; }
            if (args[0] == "verify") { Verify(PublishedId(), owner); return 0; }
            Publish(owner, args[0] == "gallery", args[0] == "metadata");
            return state.Value<bool?>("needs_legal_agreement") == true ? 20 : 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error.ToString()); return 1; }
        finally { SteamAPI.Shutdown(); }
    }

    private static void Publish(ulong owner, bool galleryOnly, bool metadataOnly)
    {
        JObject manifest = JObject.Parse(File.ReadAllText(Path.Combine(release, "release-manifest.json")));
        JObject page = JObject.Parse(File.ReadAllText(PageFile));
        bool preserveGallery = page.Value<bool?>("preserve_existing_gallery") == true;
        Require(!galleryOnly || !preserveGallery, "Gallery replacement requires an explicit gallery draft.");
        string content = Path.GetFullPath((string)manifest["content_folder"]);
        Require(content == Path.Combine(root, "package", "XiaomanVisitor"), "Unexpected content root.");
        foreach (JProperty file in ((JObject)manifest["files"]).Properties())
            Require(Hash(Path.Combine(content, file.Name.Replace('/', Path.DirectorySeparatorChar))) == (string)file.Value,
                "Release content changed: " + file.Name);
        Require(Directory.GetFiles(content, "*", SearchOption.AllDirectories).Length == ((JObject)manifest["files"]).Count,
            "Unexpected file added to the publication folder.");
        JArray gallery = (JArray)page["gallery"];
        Require(gallery.Count == 3, "Exactly three approved images are required.");
        string primary = Path.Combine(root, (string)gallery[0]["file"]);
        Require(Hash(primary) == Hash(Path.Combine(content, "preview.png")), "Primary image differs from approved portrait.");
        foreach (JToken image in manifest["gallery"])
        {
            string path = Path.Combine(root, (string)image["file"]);
            Require(Hash(path) == (string)image["sha256"] && new FileInfo(path).Length < 1024*1024,
                "Approved preview changed or exceeds Steam's preview limit.");
        }
        ulong id = PublishedId();
        if (metadataOnly)
            Require(state.Value<string>("uploaded_version") == version
                && JToken.DeepEquals(state["uploaded_files_sha256"], manifest["files"]),
                "Publish the matching content before updating localized text.");
        RemoteInfo old = Query(id);
        Require(old.Owner == owner && old.ConsumerApp == App, "Existing item belongs to another account or game.");
        if (preserveGallery) VerifyGallery(page, old.Previews);
        string priorDescription = (string)page["base_description_bbcode"];
        if (!galleryOnly && priorDescription != null)
            Require((old.Title == (string)page["title"] || old.Title == (string)page["base_title"])
                && (old.Description == priorDescription || old.Description == (string)page["description_bbcode"]),
                "Workshop title or description changed since preparation; stopped before overwriting it.");
        update = SteamUGC.StartItemUpdate(AppId, new PublishedFileId_t(id)); hasUpdate = true;
        Require(SteamUGC.SetItemUpdateLanguage(update, language), "SetItemUpdateLanguage failed.");
        Require(SteamUGC.SetItemTitle(update, (string)page["title"]), "SetItemTitle failed.");
        Require(SteamUGC.SetItemDescription(update, (string)page["description_bbcode"]), "SetItemDescription failed.");
        if (!galleryOnly && !metadataOnly) Require(SteamUGC.SetItemContent(update, content), "SetItemContent failed.");
        if (!metadataOnly)
        {
            Require(SteamUGC.SetItemVisibility(update, ERemoteStoragePublishedFileVisibility.k_ERemoteStoragePublishedFileVisibilityPublic),
                "SetItemVisibility failed.");
        }
        if (!metadataOnly && !preserveGallery)
        {
            Require(SteamUGC.SetItemPreview(update, primary), "SetItemPreview failed.");
            // Main preview and the three-image carousel are separate native fields.
            for (int i = (int)old.AdditionalPreviews - 1; i >= 0; i--)
                Require(SteamUGC.RemoveItemPreview(update, (uint)i), "Could not replace old additional previews.");
            for (int i = 0; i < gallery.Count; i++)
                Require(SteamUGC.AddItemPreviewFile(update, Path.Combine(root, (string)gallery[i]["file"]), EItemPreviewType.k_EItemPreviewType_Image),
                    "Additional preview upload was not accepted.");
        }
        state["publishedfileid"] = id.ToString();
        state["status"] = "uploading"; SaveState();
        Console.WriteLine(metadataOnly ? "Updating " + language + " Workshop text..."
            : "Uploading approved " + version + " content, public visibility, "
                + (preserveGallery ? "preserving existing previews..." : "portrait + screenshot + expression sheet..."));
        string notePath = Path.Combine(release, "workshop-update-note.txt");
        string changeNote = metadataOnly ? null : galleryOnly ? "完善发布图片：设定图、实机截图、表情图。"
            : File.Exists(notePath) ? File.ReadAllText(notePath).Trim() : version + " 更新";
        SubmitItemUpdateResult_t submitted = Wait<SubmitItemUpdateResult_t>(SteamUGC.SubmitItemUpdate(update, changeNote), 1800);
        hasUpdate = false;
        Require(submitted.m_eResult == EResult.k_EResultOK, "SubmitItemUpdate: " + submitted.m_eResult);
        state["status"] = "uploaded";
        state["needs_legal_agreement"] = submitted.m_bUserNeedsToAcceptWorkshopLegalAgreement
            || state.Value<bool?>("needs_legal_agreement") == true;
        state["url"] = "https://steamcommunity.com/sharedfiles/filedetails/?id=" + id;
        if (!metadataOnly)
        {
            state["uploaded_version"] = version;
            state["last_change_note"] = changeNote;
            state["uploaded_files_sha256"] = manifest["files"].DeepClone();
        }
        SaveState(); WriteVdf(id);
        Verify(id, owner);
    }

    private sealed class RemoteInfo
    {
        public ulong Owner;
        public uint ConsumerApp, AdditionalPreviews;
        public string Title, Description;
        public ERemoteStoragePublishedFileVisibility Visibility;
        public bool Banned;
        public JArray Previews;
    }

    private static RemoteInfo Query(ulong id)
    {
        UGCQueryHandle_t query = SteamUGC.CreateQueryUGCDetailsRequest(new[] { new PublishedFileId_t(id) }, 1);
        Require(SteamUGC.SetReturnAdditionalPreviews(query, true), "Could not configure verification query.");
        Require(SteamUGC.SetReturnLongDescription(query, true), "Could not request the complete description.");
        try
        {
            Require(SteamUGC.SetLanguage(query, language), "Could not set the verification language.");
            SteamUGCQueryCompleted_t response = Wait<SteamUGCQueryCompleted_t>(SteamUGC.SendQueryUGCRequest(query), 180);
            Require(response.m_eResult == EResult.k_EResultOK && response.m_unNumResultsReturned == 1,
                "Workshop verification query: " + response.m_eResult);
            SteamUGCDetails_t detail;
            Require(SteamUGC.GetQueryUGCResult(query, 0, out detail) && detail.m_eResult == EResult.k_EResultOK,
                "Could not read uploaded item details.");
            uint count = SteamUGC.GetQueryUGCNumAdditionalPreviews(query, 0);
            JArray previews = new JArray();
            for (uint i=0; i<count; i++)
            {
                string url, fileName; EItemPreviewType type;
                Require(SteamUGC.GetQueryUGCAdditionalPreview(query, 0, i, out url, 4096, out fileName, 4096, out type),
                    "Could not verify the additional image order.");
                previews.Add(new JObject { ["index"]=i, ["filename"]=fileName, ["url"]=url, ["type"]=type.ToString() });
            }
            return new RemoteInfo { Owner=detail.m_ulSteamIDOwner, ConsumerApp=detail.m_nConsumerAppID.m_AppId,
                Title=detail.m_rgchTitle, Description=detail.m_rgchDescription, Visibility=detail.m_eVisibility,
                Banned=detail.m_bBanned, AdditionalPreviews=count, Previews=previews };
        }
        finally { SteamUGC.ReleaseQueryUGCRequest(query); }
    }

    private static void Verify(ulong id, ulong owner)
    {
        Require(id != 0, "No published item ID has been recorded.");
        JObject page = JObject.Parse(File.ReadAllText(PageFile));
        RemoteInfo remote = Query(id);
        Require(remote.Owner == owner && remote.ConsumerApp == App && !remote.Banned, "Uploaded item's ownership/app/state mismatch.");
        Require(remote.Title == (string)page["title"] && remote.Description == (string)page["description_bbcode"],
            "Uploaded title or description did not match the approved text.");
        Require(remote.Visibility == ERemoteStoragePublishedFileVisibility.k_ERemoteStoragePublishedFileVisibilityPublic,
            "The item is not set to public.");
        VerifyGallery(page, remote.Previews);
        state["visibility"] = "public"; state["additional_previews"] = remote.AdditionalPreviews;
        state["carousel"] = remote.Previews;
        if (language == "schinese") state["title"] = remote.Title;
        JObject localized = state["metadata_by_language"] as JObject ?? new JObject();
        localized[language] = new JObject { ["title"] = remote.Title, ["description"] = remote.Description,
            ["verified_utc"] = DateTimeOffset.UtcNow.ToString("o") };
        state["metadata_by_language"] = localized;
        state["remote_metadata_verified"] = true;
        state["status"] = state.Value<bool?>("needs_legal_agreement") == true ? "uploaded_needs_legal_agreement" : "published";
        SaveState();
        Console.WriteLine("VERIFIED " + state["status"] + ": https://steamcommunity.com/sharedfiles/filedetails/?id=" + id);
    }

    private static void VerifyGallery(JObject page, JArray actual)
    {
        bool preserved = page.Value<bool?>("preserve_existing_gallery") == true;
        JArray expected = (JArray)page[preserved ? "base_gallery" : "gallery"];
        Require(expected != null && actual.Count == expected.Count, "Remote carousel image count changed.");
        for (int i=0; i<expected.Count; i++)
        {
            string filename = preserved ? (string)expected[i]["filename"] : Path.GetFileName((string)expected[i]["file"]);
            Require((string)actual[i]["filename"] == filename, "Remote carousel image order differs from the inspected order.");
            if (preserved)
            {
                Require((string)actual[i]["type"] == (string)expected[i]["type"], "Remote carousel preview type changed.");
                string url = (string)expected[i]["url"];
                if (!string.IsNullOrEmpty(url)) Require((string)actual[i]["url"] == url, "Remote carousel preview changed.");
            }
        }
    }

    private static void Inspect(ulong id, ulong owner)
    {
        Require(id != 0, "No existing item ID has been recorded.");
        RemoteInfo remote = Query(id);
        Require(remote.Owner == owner && remote.ConsumerApp == App, "Item ownership/app mismatch.");
        string directory = Path.Combine(root, ".dev", "reports");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "workshop-remote-" + language + ".json");
        var observed = new JObject { ["publishedfileid"] = id.ToString(), ["language"] = language,
            ["title"] = remote.Title, ["description"] = remote.Description, ["owner"] = owner.ToString(),
            ["visibility"] = remote.Visibility.ToString(), ["banned"] = remote.Banned,
            ["previews"] = remote.Previews, ["observed_utc"] = DateTimeOffset.UtcNow.ToString("o") };
        File.WriteAllText(path, observed.ToString(Formatting.Indented), new UTF8Encoding(false));
        Console.WriteLine("Observed " + language + ": " + remote.Title + "; report: " + path);
    }

    private static T Wait<T>(SteamAPICall_t call, int seconds) where T : struct
    {
        Require(call.m_SteamAPICall != 0, "Steam did not return a valid async call.");
        bool done = false, failed = false; T result = default(T);
        using (CallResult<T> callback = CallResult<T>.Create((value, ioFailure) => { result=value; failed=ioFailure; done=true; }))
        {
            callback.Set(call);
            var watch = Stopwatch.StartNew(); int lastReport = -1;
            while (!done)
            {
                SteamAPI.RunCallbacks();
                if (watch.Elapsed.TotalSeconds > seconds) throw new TimeoutException("Steam operation timed out; retain the saved item ID for retry.");
                if (hasUpdate && (int)(watch.Elapsed.TotalSeconds/5) != lastReport)
                {
                    lastReport = (int)(watch.Elapsed.TotalSeconds/5);
                    ulong processed,total;
                    EItemUpdateStatus progress = SteamUGC.GetItemUpdateProgress(update, out processed, out total);
                    Console.WriteLine("Upload " + progress + ": " + processed + "/" + total + " bytes.");
                }
                if (!done) Thread.Sleep(50);
            }
        }
        Require(!failed, "Steam reported an I/O failure."); return result;
    }

    private static ulong PublishedId()
    {
        ulong id;
        if (ulong.TryParse(state.Value<string>("publishedfileid"), out id) && id != 0)
        { Require(id == ExistingItem, "Unexpected Workshop update target."); return id; }
        foreach (string name in new[] { "workshop-item.vdf", "workshop-item.vdf.draft" })
        {
            string path = Path.Combine(release, name);
            if (!File.Exists(path)) continue;
            Match match = Regex.Match(File.ReadAllText(path), "\"publishedfileid\"\\s+\"(\\d+)\"");
            if (match.Success && ulong.TryParse(match.Groups[1].Value, out id) && id != 0)
            { Require(id == ExistingItem, "Unexpected Workshop update target."); return id; }
        }
        return ExistingItem;
    }

    private static void WriteVdf(ulong id)
    {
        string draft = Path.Combine(release, "workshop-item.vdf.draft");
        string text = File.ReadAllText(draft);
        text = Regex.Replace(text, "(\"publishedfileid\"\\s+)\"\\d+\"", m => m.Groups[1].Value + "\"" + id + "\"");
        text = Regex.Replace(text, "(\"visibility\"\\s+)\"\\d+\"", m => m.Groups[1].Value + "\"0\"");
        File.WriteAllText(draft, text, new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(release, "workshop-item.vdf"), text, new UTF8Encoding(false));
    }

    private static void SaveState()
    {
        state["updated_utc"] = DateTimeOffset.UtcNow.ToString("o");
        File.WriteAllText(stateFile, state.ToString(Formatting.Indented), new UTF8Encoding(false));
    }

    private static string Hash(string path)
    { using (var sha = SHA256.Create()) using (var file = File.OpenRead(path)) return BitConverter.ToString(sha.ComputeHash(file)).Replace("-", "").ToLowerInvariant(); }
    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
}
