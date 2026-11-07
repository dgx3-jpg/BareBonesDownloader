using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace BareBonesDownloader
{
    internal static class UnicodeRecoveryTests
    {
        [STAThread]
        private static int Main(string[] args)
        {
            Console.OutputEncoding = new UTF8Encoding(false);
            try
            {
                if (args[0] == "observe")
                {
                    using (DownloaderForm form = new DownloaderForm())
                    {
                        IntPtr handle = form.Handle;
                        BindingFlags instance = BindingFlags.Instance | BindingFlags.NonPublic;
                        object state = Activator.CreateInstance(typeof(DownloaderForm).GetNestedType(
                            "DownloaderOutputState", BindingFlags.NonPublic), true);
                        foreach (string line in File.ReadAllLines(args[1], Encoding.UTF8))
                            typeof(DownloaderForm).GetMethod("ObserveDownloaderLine", instance).Invoke(form, new object[] { line, state });
                        string audio = (string)typeof(DownloaderForm).GetMethod("FindExistingAudioPath",
                            BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { state, args[2] });
                        typeof(DownloaderForm).GetMethod("BeginShutdown", instance).Invoke(form, null);
                        ((Thread)typeof(DownloaderForm).GetField("_worker", instance).GetValue(form)).Join(5000);
                        if (audio == null || !File.Exists(audio)) throw new Exception("Real yt-dlp path was not resolved.");
                        Console.WriteLine("PASS: real yt-dlp path resolves to " + audio);
                    }
                }
                else if (args[0] == "recover")
                {
                    string app = Path.GetFullPath(args[1]);
                    string id = args[2];
                    if (!System.Text.RegularExpressions.Regex.IsMatch(id, @"\A[A-Za-z0-9_-]{11}\z"))
                        throw new ArgumentException("Invalid video ID.");
                    string workRoot = Path.Combine(app, "work");
                    string work = Path.Combine(workRoot, id);
                    string tools = Path.Combine(app, "tools");
                    using (FileStream workLock = new FileStream(Path.Combine(workRoot, id + ".lock"),
                        FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
                    using (ProcessJob job = new ProcessJob())
                    {
                        string[] audioFiles = Directory.GetFiles(work, "*.opus");
                        if (audioFiles.Length != 1) throw new Exception("Expected exactly one completed Opus file.");
                        string audio = audioFiles[0];
                        string info = MusicMetadata.FindInfoFile(work, id);
                        string before = AudioHash(audio, tools, job);
                        MusicMetadata.EnsureTags(audio, info, tools, job, delegate { return false; }, Console.WriteLine);
                        MusicMetadata.RunTool(Path.Combine(tools, "rsgain", "rsgain.exe"),
                            new string[] { "custom", "-s", "i", audio }, job, delegate { return false; });
                        if (AudioHash(audio, tools, job) != before) throw new Exception("The encoded audio changed.");
                        Dictionary<string, string> tags = MusicMetadata.ReadTags(audio, tools, job, delegate { return false; });
                        foreach (KeyValuePair<string, string> expected in MusicMetadata.ExpectedTags(info))
                        {
                            string value;
                            if (!tags.TryGetValue(expected.Key, out value) || value != expected.Value)
                                throw new Exception("A recovered tag did not match: " + expected.Key);
                        }
                        if (!tags.ContainsKey("REPLAYGAIN_TRACK_GAIN")) throw new Exception("ReplayGain is missing.");
                        bool existing;
                        string final = DownloadFiles.PublishAudio(audio, args[3], delegate { return false; }, out existing);
                        if (existing) throw new Exception("A same-name song already exists; it was left unchanged.");
                        if (AudioHash(final, tools, job) != before) throw new Exception("The published audio changed.");
                        DownloadFiles.CleanupWorkingDirectory(workRoot, work);
                        Console.WriteLine("RECOVERED: " + final);
                    }
                }
                else throw new ArgumentException("Unknown mode.");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
                return 1;
            }
        }

        private static string AudioHash(string audio, string tools, ProcessJob job)
        {
            return MusicMetadata.RunTool(Path.Combine(tools, "ffmpeg.exe"), new string[]
            {
                "-hide_banner", "-loglevel", "error", "-i", audio, "-map", "0:a:0",
                "-c:a", "copy", "-f", "hash", "-hash", "sha256", "-"
            }, job, delegate { return false; }).Trim();
        }
    }
}
