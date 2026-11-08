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
    private static string root, release, stateFile, version;
    private static JObject state;
    private static readonly AppId_t AppId = new AppId_t(App);
    private static UGCUpdateHandle_t update;
    private static bool hasUpdate;

    private static int Main(string[] args)
    {
        Console.OutputEncoding = new UTF8Encoding(false);
        if (args.Length < 2 || (args[0] != "check" && args[0] != "publish" && args[0] != "gallery" && args[0] != "verify"))
        { Console.Error.WriteLine("Usage: WorkshopPublisher check|publish|gallery|verify <project-root> [expected-SteamID64]"); return 2; }
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
            Require(args.Length < 3 || owner == ulong.Parse(args[2]), "Signed-in Steam account does not match the active desktop account.");
            Require(SteamApps.BIsSubscribedApp(AppId), "The active account does not have access to the target game.");
            Console.WriteLine("Steam session verified: " + SteamFriends.GetPersonaName() + " / " + owner + " / app " + App + ".");
            if (args[0] == "check") return 0;
            state["owner_steamid"] = owner.ToString(); state["appid"] = App;
            if (args[0] == "verify") { Verify(PublishedId(), owner); return 0; }
            Publish(owner, args[0] == "gallery");
            return state.Value<bool?>("needs_legal_agreement") == true ? 20 : 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error.ToString()); return 1; }
        finally { SteamAPI.Shutdown(); }
    }

    private static void Publish(ulong owner, bool galleryOnly)
    {
        JObject manifest = JObject.Parse(File.ReadAllText(Path.Combine(release, "release-manifest.json")));
        JObject page = JObject.Parse(File.ReadAllText(Path.Combine(release, "工坊页面.json")));
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
        Require(!galleryOnly || id != 0, "A gallery-only update requires an existing published item.");
        bool created = false;
        if (id == 0)
        {
            Console.WriteLine("Creating the new Workshop item...");
            CreateItemResult_t result = Wait<CreateItemResult_t>(SteamUGC.CreateItem(AppId, EWorkshopFileType.k_EWorkshopFileTypeCommunity), 180);
            Require(result.m_eResult == EResult.k_EResultOK, "CreateItem: " + result.m_eResult);
            id = result.m_nPublishedFileId.m_PublishedFileId;
            state["publishedfileid"] = id.ToString(); state["status"] = "created_pending_upload";
            state["needs_legal_agreement"] = result.m_bUserNeedsToAcceptWorkshopLegalAgreement;
            SaveState(); WriteVdf(id);
            created = true;
            Console.WriteLine("Created item " + id + "; ID saved before uploading to prevent duplicates.");
        }
        RemoteInfo old = created ? null : Query(id);
        if (old != null)
        {
            Require(old.Owner == owner && old.ConsumerApp == App, "Existing item belongs to another account or game.");
            string priorDescription = (string)page["base_description_bbcode"];
            if (!galleryOnly && priorDescription != null)
                Require(old.Title == (string)page["title"]
                    && (old.Description == priorDescription || old.Description == (string)page["description_bbcode"]),
                    "Workshop title or description changed since preparation; stopped before overwriting it.");
        }
        update = SteamUGC.StartItemUpdate(AppId, new PublishedFileId_t(id)); hasUpdate = true;
        Require(SteamUGC.SetItemTitle(update, (string)page["title"]), "SetItemTitle failed.");
        Require(SteamUGC.SetItemDescription(update, (string)page["description_bbcode"]), "SetItemDescription failed.");
        if (!galleryOnly) Require(SteamUGC.SetItemContent(update, content), "SetItemContent failed.");
        Require(SteamUGC.SetItemPreview(update, primary), "SetItemPreview failed.");
        Require(SteamUGC.SetItemVisibility(update, ERemoteStoragePublishedFileVisibility.k_ERemoteStoragePublishedFileVisibilityPublic),
            "SetItemVisibility failed.");
        // Steam's main preview is separate from its highlight carousel. Upload
        // all three approved images into the carousel in the requested order.
        if (old != null)
            for (int i = (int)old.AdditionalPreviews - 1; i >= 0; i--)
                Require(SteamUGC.RemoveItemPreview(update, (uint)i), "Could not replace old additional previews.");
        for (int i = 0; i < gallery.Count; i++)
            Require(SteamUGC.AddItemPreviewFile(update, Path.Combine(root, (string)gallery[i]["file"]), EItemPreviewType.k_EItemPreviewType_Image),
                "Additional preview upload was not accepted.");
        state["status"] = "uploading"; SaveState();
        Console.WriteLine("Uploading approved " + version + " content, public visibility, portrait + screenshot + expression sheet...");
        string notePath = Path.Combine(release, "workshop-update-note.txt");
        string changeNote = galleryOnly ? "完善发布图片：设定图、实机截图、表情图。"
            : created ? version + " 首次发布" : File.Exists(notePath) ? File.ReadAllText(notePath).Trim() : version + " 更新";
        SubmitItemUpdateResult_t submitted = Wait<SubmitItemUpdateResult_t>(SteamUGC.SubmitItemUpdate(update, changeNote), 1800);
        hasUpdate = false;
        Require(submitted.m_eResult == EResult.k_EResultOK, "SubmitItemUpdate: " + submitted.m_eResult);
        state["status"] = "uploaded";
        state["needs_legal_agreement"] = submitted.m_bUserNeedsToAcceptWorkshopLegalAgreement
            || state.Value<bool?>("needs_legal_agreement") == true;
        state["url"] = "https://steamcommunity.com/sharedfiles/filedetails/?id=" + id;
        state["uploaded_version"] = version;
        state["last_change_note"] = changeNote;
        state["uploaded_files_sha256"] = manifest["files"].DeepClone();
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
        JObject page = JObject.Parse(File.ReadAllText(Path.Combine(release, "工坊页面.json")));
        RemoteInfo remote = Query(id);
        Require(remote.Owner == owner && remote.ConsumerApp == App && !remote.Banned, "Uploaded item's ownership/app/state mismatch.");
        Require(remote.Title == (string)page["title"] && remote.Description == (string)page["description_bbcode"],
            "Uploaded title or description did not match the approved text.");
        Require(remote.Visibility == ERemoteStoragePublishedFileVisibility.k_ERemoteStoragePublishedFileVisibilityPublic,
            "The item is not set to public.");
        Require(remote.AdditionalPreviews == 3, "Uploaded carousel must contain all three approved images.");
        JArray gallery = (JArray)page["gallery"];
        for (int i=0; i<gallery.Count; i++)
            Require((string)remote.Previews[i]["filename"] == Path.GetFileName((string)gallery[i]["file"]),
                "Remote carousel image order differs from the approved order.");
        state["visibility"] = "public"; state["additional_previews"] = remote.AdditionalPreviews;
        state["carousel"] = remote.Previews;
        state["title"] = remote.Title; state["remote_metadata_verified"] = true;
        state["status"] = state.Value<bool?>("needs_legal_agreement") == true ? "uploaded_needs_legal_agreement" : "published";
        SaveState();
        Console.WriteLine("VERIFIED " + state["status"] + ": https://steamcommunity.com/sharedfiles/filedetails/?id=" + id);
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
        if (ulong.TryParse(state.Value<string>("publishedfileid"), out id) && id != 0) return id;
        foreach (string name in new[] { "workshop-item.vdf", "workshop-item.vdf.draft" })
        {
            string path = Path.Combine(release, name);
            if (!File.Exists(path)) continue;
            Match match = Regex.Match(File.ReadAllText(path), "\"publishedfileid\"\\s+\"(\\d+)\"");
            if (match.Success && ulong.TryParse(match.Groups[1].Value, out id) && id != 0) return id;
        }
        return 0;
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
