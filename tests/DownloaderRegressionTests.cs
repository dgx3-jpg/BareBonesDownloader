using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

namespace BareBonesDownloader
{
    internal static class RegressionTests
    {
        private static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private static int _passed;

        [STAThread]
        private static int Main(string[] args)
        {
            try
            {
                if (args.Length == 3 && args[0] == "arguments")
                {
                    string command = (string)typeof(DownloaderForm).GetMethod("BuildDownloaderArguments",
                        BindingFlags.Static | BindingFlags.NonPublic).Invoke(null,
                        new object[] { "https://www.youtube.com/watch?v=BBBBBBBBBBB", args[1], args[2], Path.Combine(args[2], "deno.exe") });
                    Console.WriteLine(command);
                    return 0;
                }
                string basePath = AppDomain.CurrentDomain.BaseDirectory;
                string destination = Path.Combine(basePath, "音楽-music-test-" + Guid.NewGuid().ToString("N"));
                string videoDestination = Path.Combine(basePath, "動画-video-test-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(destination);
                Directory.CreateDirectory(videoDestination);
                CheckSettings(basePath, destination, videoDestination);
                CheckAppearanceSettings(basePath);
                CheckVideoOutput(basePath);
                CheckPublishing(destination);
                CheckQueue(destination, videoDestination);
                Console.WriteLine("PASS: " + _passed + " regression checks");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
                return 1;
            }
        }

        private static void CheckSettings(string basePath, string destination, string videoDestination)
        {
            string path = Path.Combine(basePath, "settings-regression", "settings.json");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, "{\"destination\":\"" + destination.Replace("\\", "\\\\") +
                "\",\"audioFormat\":\"mp3\",\"qualityMode\":\"bitrate\",\"bitrateKbps\":256,\"tagTitle\":false,\"replayGain\":false}");
            string problem;
            AppSettings legacy = SettingsStore.LoadFromPath(path, out problem);
            Assert(problem == null && legacy.Destination == destination && legacy.VideoDestination == null && legacy.Theme == "light",
                "legacy settings retain the audio folder and default new options");
            Assert(legacy.AudioFormat == "mp3" && legacy.QualityMode == "bitrate" && legacy.BitrateKbps == 256 &&
                !legacy.TagTitle && !legacy.ReplayGain, "legacy audio options remain intact");
            legacy.VideoDestination = videoDestination;
            legacy.Theme = "dark";
            SettingsStore.SaveToPath(legacy, path);
            AppSettings current = SettingsStore.LoadFromPath(path, out problem);
            Assert(problem == null && current.Destination == destination && current.VideoDestination == videoDestination &&
                current.Theme == "dark", "video folder and appearance save and load");
            File.WriteAllText(path, "{not valid json}");
            current = SettingsStore.LoadFromPath(path, out problem);
            Assert(problem != null && current.Theme == "light", "corrupt settings recover with the Light theme");
            Directory.Delete(Path.GetDirectoryName(path), true);
        }

        private static void CheckVideoOutput(string basePath)
        {
            string tools = Path.Combine(basePath, "tools");
            string folder = Path.Combine(basePath, "video-probe-test");
            Directory.CreateDirectory(folder);
            using (ProcessJob job = new ProcessJob())
            {
                string valid = Path.Combine(folder, "Valid [BBBBBBBBBBB].mp4");
                File.WriteAllText(valid, "synthetic video");
                VideoOutput.Validate(valid, Path.Combine(tools, "ffprobe.exe"), job, delegate { return false; });
                Assert(true, "video validation accepts H.264 video with AAC audio");
                foreach (string invalidName in new string[]
                    { "Video missing audio [MMMMMMMMMMM].mp4", "Video attached picture [NNNNNNNNNNN].mp4", "Video wrong codec [OOOOOOOOOOO].mp4" })
                {
                    string invalid = Path.Combine(folder, invalidName);
                    File.WriteAllText(invalid, "synthetic video");
                    bool rejected = false;
                    try { VideoOutput.Validate(invalid, Path.Combine(tools, "ffprobe.exe"), job, delegate { return false; }); }
                    catch (IOException) { rejected = true; }
                    Assert(rejected, "video validation rejects " + Path.GetFileNameWithoutExtension(invalid));
                }
            }
            Directory.Delete(folder, true);
        }

        private static void CheckAppearanceSettings(string basePath)
        {
            string path = Path.Combine(basePath, "appearance-regression", "settings.json");
            AppSettings settings = AppSettings.Defaults();
            settings.Destination = null;
            settings.VideoDestination = null;
            using (SettingsForm form = new SettingsForm(settings, path))
            {
                ComboBox theme = (ComboBox)Get(form, "_theme");
                theme.SelectedIndex = 1;
                Invoke(form, "SaveSettings");
                Assert(form.Result != null && form.Result.Theme == "dark" && form.Result.Destination == null &&
                    form.Result.VideoDestination == null, "appearance can be saved before choosing folders");
                Assert(File.Exists(path), "appearance settings use the isolated settings file");
            }
            Directory.Delete(Path.GetDirectoryName(path), true);
        }

        private static void CheckPublishing(string destination)
        {
            string work = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "publish-test");
            Directory.CreateDirectory(work);
            string source = Path.Combine(work, "Synthetic.opus");
            File.WriteAllText(source, "new audio");
            bool existed;
            string target = DownloadFiles.PublishAudio(source, destination, delegate { return false; }, out existed);
            Assert(!existed && File.ReadAllText(target) == "new audio", "finished audio publishes intact");
            using (FileStream locked = new FileStream(target, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                File.WriteAllText(source, "different audio");
                string duplicate = DownloadFiles.PublishAudio(source, destination, delegate { return false; }, out existed);
                Assert(existed && duplicate == target, "a locked existing song is skipped without opening it");
            }
            Assert(File.ReadAllText(target) == "new audio", "existing song remains unchanged");
            source = Path.Combine(work, "Cancelled.opus");
            File.WriteAllText(source, new string('x', 200000));
            bool cancelled = false;
            try { DownloadFiles.PublishAudio(source, destination, delegate { return true; }, out existed); }
            catch (OperationCanceledException) { cancelled = true; }
            Assert(cancelled && !File.Exists(Path.Combine(destination, "Cancelled.opus")), "cancelled publish creates no song");
            Assert(Directory.GetFiles(destination, "*.tmp").Length == 0, "cancelled publish removes its temporary file");
            string raceSource = Path.Combine(work, "Race.opus");
            File.WriteAllText(raceSource, "our copy");
            string raceTarget = Path.Combine(destination, "Race.opus");
            DownloadFiles.PublishAudio(raceSource, destination, delegate
            {
                if (!File.Exists(raceTarget)) File.WriteAllText(raceTarget, "other copy");
                return false;
            }, out existed);
            Assert(existed && File.ReadAllText(raceTarget) == "other copy", "concurrent same-title save is preserved");
            bool refused = false;
            try { DownloadFiles.CleanupWorkingDirectory(work, destination); }
            catch (InvalidOperationException) { refused = true; }
            Assert(refused && Directory.Exists(destination), "cleanup refuses paths outside its working folder");
        }

        private static void CheckQueue(string destination, string videoDestination)
        {
            using (DownloaderForm form = new DownloaderForm())
            {
                IntPtr handle = form.Handle;
                AppSettings settings = AppSettings.Defaults();
                settings.Destination = destination;
                settings.VideoDestination = videoDestination;
                Set(form, "_settings", settings);
                Set(form, "_destinationPath", destination);
                try
                {
                    string unicodeDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "日本語-é-한국어");
                    Directory.CreateDirectory(unicodeDirectory);
                    string unicodeFile = Path.Combine(unicodeDirectory, "ナ2モノ #1.opus");
                    File.WriteAllText(unicodeFile, "audio");
                    Type outputType = typeof(DownloaderForm).GetNestedType("DownloaderOutputState", BindingFlags.NonPublic);
                    object output = Activator.CreateInstance(outputType, true);
                    string json = new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(unicodeFile);
                    string asciiJson = String.Empty;
                    foreach (char c in json) asciiJson += c > 127 ? "\\u" + ((int)c).ToString("x4") : c.ToString();
                    MethodInfo observe = typeof(DownloaderForm).GetMethod("ObserveDownloaderLine", Private);
                    observe.Invoke(form, new object[] { "BBD_FILE:" + asciiJson, output });
                    string decoded = (string)typeof(DownloaderForm).GetMethod("FindExistingAudioPath",
                        BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { output, unicodeDirectory });
                    Assert(decoded == unicodeFile, "JSON Unicode path is decoded to the actual Japanese filename");
                    observe.Invoke(form, new object[] { "BBD_FILE:\"bad JSON", output });
                    Assert(!form.IsDisposed, "malformed path records do not close the app");
                    Add(form, "https://youtu.be/AAAAAAAAAAA\r\nhttps://youtu.be/BBBBBBBBBBB");
                    WaitFor(form, delegate { return Rows(form).Items.Count == 2 && Status(form, 0) == "Failed" && Status(form, 1) == "Finished"; });
                    Assert(Status(form, 0) == "Failed", "WinError 32 is displayed as a failed item");
                    Assert(Status(form, 1) == "Finished", "the next queued download completes after the error");
                    string log = ((TextBox)Get(form, "_logBox")).Text;
                    Assert(log.Contains("[WinError 32]") && log.Contains("->"), "the original arrow diagnostic remains visible");
                    Assert(!form.IsDisposed && ((Thread)Get(form, "_worker")).IsAlive && !(bool)Get(form, "_shutdownRequested"), "window and queue stay alive after errors and queue completion");
                    string target = Path.Combine(destination, "Regression (test).opus");
                    Assert(File.Exists(target) && !File.Exists(Path.Combine(destination, "Regression (test).webm")), "only final audio reaches Music");
                    Assert(!Directory.Exists(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "work", "BBBBBBBBBBB")), "successful download removes its intermediates");
                    using (FileStream locked = new FileStream(target, FileMode.Open, FileAccess.Read, FileShare.None))
                    {
                        Add(form, "https://youtu.be/BBBBBBBBBBB");
                        WaitFor(form, delegate { return Rows(form).Items.Count == 3 && Status(form, 2) == "Finished"; });
                        Assert(Status(form, 2) == "Finished", "repeat download succeeds while the existing song is locked");
                    }
                    Assert(File.ReadAllText(target) == "finished synthetic audio", "repeat download preserves existing audio");
                    Add(form, "https://youtu.be/CCCCCCCCCCC");
                    string partial = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "work", "CCCCCCCCCCC", "Regression (test).webm");
                    WaitFor(form, delegate { return File.Exists(partial); });
                    Invoke(form, "OnCancelCurrent");
                    WaitFor(form, delegate { return Status(form, 3) == "Cancelled"; });
                    Assert(File.Exists(partial), "cancelled download keeps resumable data in work");
                    Assert(Directory.GetFiles(destination, "*.webm").Length == 0 && Directory.GetFiles(destination, "*.tmp").Length == 0,
                        "cancellation leaves no intermediate audio in Music");
                    Add(form, "https://youtu.be/DDDDDDDDDDD\r\nhttps://youtu.be/BBBBBBBBBBB");
                    WaitFor(form, delegate { return Rows(form).Items.Count == 6 && Status(form, 4) == "Finished" && Status(form, 5) == "Finished"; });
                    Assert(Status(form, 4) == "Finished", "missing metadata does not fail the audio download");
                    Assert(File.Exists(Path.Combine(destination, "Missing metadata.opus")), "audio with unavailable tags is still saved to Music");
                    Assert(Status(form, 5) == "Finished", "the queue continues after tags are unavailable");
                    Add(form, "https://youtu.be/EEEEEEEEEEE\r\nhttps://youtu.be/GGGGGGGGGGG\r\nhttps://youtu.be/FFFFFFFFFFF\r\nhttps://youtu.be/BBBBBBBBBBB");
                    WaitFor(form, delegate { return Rows(form).Items.Count == 10 && Status(form, 6) == "Finished" && Status(form, 7) == "Finished" && Status(form, 8) == "Failed" && Status(form, 9) == "Finished"; });
                    Assert(File.Exists(Path.Combine(destination, "Tag warning.opus")), "tag-writing failure does not block valid audio from Music");
                    Assert(File.Exists(Path.Combine(destination, "Verification mismatch.opus")), "metadata verification mismatch does not block valid audio from Music");
                    string updatedLog = ((TextBox)Get(form, "_logBox")).Text;
                    Assert(updatedLog.Contains("Metadata warning:") && updatedLog.Contains("date"), "metadata warnings identify the field without failing the download");
                    Assert(!File.Exists(Path.Combine(destination, "Broken audio.opus")), "a file without a readable audio stream is rejected");
                    Assert(Status(form, 9) == "Finished", "the queue continues through metadata warnings and invalid audio");

                    AddKind(form, "https://youtu.be/BBBBBBBBBBB", DownloadKind.Video);
                    WaitFor(form, delegate { return Rows(form).Items.Count == 11 && Status(form, 10) == "Finished"; });
                    Assert(Rows(form).Items[10].SubItems[1].Text == "Video + audio", "queue shows the video mode beside its status");
                    Assert(File.Exists(Path.Combine(videoDestination, "Video Regression [BBBBBBBBBBB].mp4")),
                        "video publishes a validated MP4 to its own destination");
                    Assert(!File.Exists(Path.Combine(videoDestination, "Video Regression [BBBBBBBBBBB].info.json")),
                        "video downloads skip audio metadata files");

                    AddKind(form, "https://youtu.be/BBBBBBBBBBB", DownloadKind.Audio);
                    AddKind(form, "https://youtu.be/BBBBBBBBBBB", DownloadKind.Video);
                    WaitFor(form, delegate { return Rows(form).Items.Count == 13 && Status(form, 11) == "Finished" && Status(form, 12) == "Finished"; });
                    Assert(Rows(form).Items[11].SubItems[1].Text == "Audio only" &&
                        Rows(form).Items[12].SubItems[1].Text == "Video + audio",
                        "the same YouTube ID can be queued for both download types");

                    AddKind(form, "https://youtu.be/MMMMMMMMMMM\r\nhttps://youtu.be/NNNNNNNNNNN\r\nhttps://youtu.be/OOOOOOOOOOO",
                        DownloadKind.Video);
                    WaitFor(form, delegate { return Rows(form).Items.Count == 16 && Status(form, 13) == "Failed" &&
                        Status(form, 14) == "Failed" && Status(form, 15) == "Failed"; });
                    Assert(!File.Exists(Path.Combine(videoDestination, "Video missing audio [MMMMMMMMMMM].mp4")),
                        "video without an audio stream is never published");
                    Assert(!File.Exists(Path.Combine(videoDestination, "Video attached picture [NNNNNNNNNNN].mp4")),
                        "cover artwork does not count as a video stream");
                    Assert(!File.Exists(Path.Combine(videoDestination, "Video wrong codec [OOOOOOOOOOO].mp4")),
                        "video with unsupported codecs is never published");

                    AddKind(form, "https://youtu.be/CCCCCCCCCCC", DownloadKind.Video);
                    string videoPartial = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "work", "video-4343434343434343434343",
                        "Video cancelled [CCCCCCCCCCC].part");
                    WaitFor(form, delegate { return Directory.Exists(Path.GetDirectoryName(videoPartial)); });
                    Invoke(form, "OnCancelCurrent");
                    WaitFor(form, delegate { return Status(form, 16) == "Cancelled"; });
                    Assert(!File.Exists(Path.Combine(videoDestination, "Video cancelled [CCCCCCCCCCC].mp4")),
                        "cancelling a video leaves no published output");
                    DownloadFiles.CleanupWorkingDirectory(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "work"),
                        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "work", "CCCCCCCCCCC"));
                }
                finally
                {
                    typeof(DownloaderForm).GetMethod("BeginShutdown", Private).Invoke(form, null);
                    ((Thread)Get(form, "_worker")).Join(10000);
                    Application.DoEvents();
                }
            }
        }

        private static void Add(DownloaderForm form, string links)
        {
            AddKind(form, links, DownloadKind.Audio);
        }
        private static void AddKind(DownloaderForm form, string links, DownloadKind kind)
        {
            ((RadioButton)Get(form, "_audioRadio")).Checked = kind == DownloadKind.Audio;
            ((RadioButton)Get(form, "_videoRadio")).Checked = kind == DownloadKind.Video;
            ((TextBox)Get(form, "_linksBox")).Text = links;
            Invoke(form, "OnAddToQueue");
        }
        private static void Invoke(DownloaderForm form, string method)
        {
            typeof(DownloaderForm).GetMethod(method, Private).Invoke(form, new object[] { null, EventArgs.Empty });
        }
        private static object Get(DownloaderForm form, string field)
        {
            return typeof(DownloaderForm).GetField(field, Private).GetValue(form);
        }
        private static object Get(SettingsForm form, string field)
        {
            return typeof(SettingsForm).GetField(field, Private).GetValue(form);
        }
        private static void Invoke(SettingsForm form, string method)
        {
            typeof(SettingsForm).GetMethod(method, Private).Invoke(form, new object[] { null, EventArgs.Empty });
        }
        private static void Set(DownloaderForm form, string field, object value)
        {
            typeof(DownloaderForm).GetField(field, Private).SetValue(form, value);
        }
        private static ListView Rows(DownloaderForm form) { return (ListView)Get(form, "_queueView"); }
        private static string Status(DownloaderForm form, int index) { return Rows(form).Items[index].SubItems[2].Text; }
        private static void WaitFor(DownloaderForm form, Func<bool> condition)
        {
            Stopwatch timer = Stopwatch.StartNew();
            while (!condition())
            {
                Application.DoEvents();
                if (timer.ElapsedMilliseconds > 20000)
                    throw new Exception("Timed out. Log: " + ((TextBox)Get(form, "_logBox")).Text);
                Thread.Sleep(20);
            }
            Application.DoEvents();
        }
        private static void Assert(bool condition, string message)
        {
            if (!condition) throw new Exception("FAIL: " + message);
            _passed++;
            Console.WriteLine("PASS: " + message);
        }
    }
}
