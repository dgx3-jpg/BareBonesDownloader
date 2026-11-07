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
                Directory.CreateDirectory(destination);
                CheckPublishing(destination);
                CheckQueue(destination);
                Console.WriteLine("PASS: " + _passed + " regression checks");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
                return 1;
            }
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

        private static void CheckQueue(string destination)
        {
            using (DownloaderForm form = new DownloaderForm())
            {
                IntPtr handle = form.Handle;
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
        private static void Set(DownloaderForm form, string field, object value)
        {
            typeof(DownloaderForm).GetField(field, Private).SetValue(form, value);
        }
        private static ListView Rows(DownloaderForm form) { return (ListView)Get(form, "_queueView"); }
        private static string Status(DownloaderForm form, int index) { return Rows(form).Items[index].SubItems[1].Text; }
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
