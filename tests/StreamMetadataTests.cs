using System;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace BareBonesDownloader
{
    internal static class StreamMetadataTests
    {
        private static int Main(string[] args)
        {
            Console.OutputEncoding = new UTF8Encoding(false);
            try
            {
                string tools = args[0];
                string work = Path.Combine(Path.GetTempPath(), "stream-metadata-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(work);
                string audio = Path.Combine(work, "test.opus");
                string info = Path.Combine(work, "test.info.json");
                using (ProcessJob job = new ProcessJob())
                {
                    MusicMetadata.RunTool(Path.Combine(tools, "ffmpeg.exe"), new string[]
                    {
                        "-hide_banner", "-loglevel", "error", "-f", "lavfi", "-i", "sine=frequency=440:duration=1",
                        "-c:a", "libopus", "-metadata:s:a:0", "date=20200519", "-metadata:s:a:0", "title=Old title",
                        "-metadata:s:a:0", "artist=Old artist", "-metadata:s:a:0", "album=Old album", audio
                    }, job, delegate { return false; });
                    string before = Hash(audio, tools, job);
                    File.WriteAllText(info, new JavaScriptSerializer().Serialize(new
                    { title = "New title", artist = "New artist", album = "New album", release_date = "20200520", duration = 1 }));
                    MusicMetadata.EnsureTags(audio, info, tools, job, delegate { return false; }, Console.WriteLine);
                    var tags = MusicMetadata.ReadTags(audio, tools, job, delegate { return false; });
                    if (tags["date"] != "20200520" || tags["title"] != "New title" || tags["artist"] != "New artist" || tags["album"] != "New album")
                        throw new Exception("Existing stream metadata was not overwritten correctly.");
                    if (Hash(audio, tools, job) != before) throw new Exception("Encoded audio changed.");
                    Console.WriteLine("PASS: existing Opus stream tags are updated, release date replaces upload date, and encoded audio is unchanged.");
                }
                return 0;
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        }

        private static string Hash(string audio, string tools, ProcessJob job)
        {
            return MusicMetadata.RunTool(Path.Combine(tools, "ffmpeg.exe"), new string[]
            { "-hide_banner", "-loglevel", "error", "-i", audio, "-map", "0:a:0", "-c:a", "copy", "-f", "hash", "-hash", "sha256", "-" },
                job, delegate { return false; }).Trim();
        }
    }
}
