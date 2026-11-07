using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace BareBonesDownloader
{
    internal static class MusicMetadata
    {
        private static Dictionary<string, object> ReadJson(string text)
        {
            JavaScriptSerializer serializer = new JavaScriptSerializer();
            serializer.MaxJsonLength = 16 * 1024 * 1024;
            return serializer.Deserialize<Dictionary<string, object>>(text);
        }

        private static string Text(Dictionary<string, object> data, params string[] names)
        {
            foreach (string name in names)
            {
                object value;
                if (!data.TryGetValue(name, out value) || value == null)
                    continue;
                string result = value as string;
                if (result == null && value is IEnumerable && !(value is IDictionary))
                {
                    List<string> parts = new List<string>();
                    foreach (object part in (IEnumerable)value)
                        if (part != null) parts.Add(Convert.ToString(part, CultureInfo.InvariantCulture));
                    result = String.Join(", ", parts.ToArray());
                }
                if (result == null)
                    result = Convert.ToString(value, CultureInfo.InvariantCulture);
                result = result.Replace("\0", "").Trim();
                if (result.Length > 0 && result != "NA" && result != "N/A")
                    return result;
            }
            return null;
        }

        private static Dictionary<string, object> ReadInfo(string file)
        {
            if (String.IsNullOrEmpty(file)) return new Dictionary<string, object>();
            try { return ReadJson(File.ReadAllText(file, Encoding.UTF8)) ?? new Dictionary<string, object>(); }
            catch (IOException) { return new Dictionary<string, object>(); }
            catch (UnauthorizedAccessException) { return new Dictionary<string, object>(); }
            catch (ArgumentException) { return new Dictionary<string, object>(); }
            catch (InvalidOperationException) { return new Dictionary<string, object>(); }
        }

        public static string FindInfoFile(string directory, string videoId)
        {
            foreach (string file in Directory.GetFiles(directory, "*.info.json"))
                if (String.Equals(Text(ReadInfo(file), "id"), videoId, StringComparison.Ordinal)) return file;
            return null;
        }

        public static Dictionary<string, string> ExpectedTags(string infoFile)
        {
            Dictionary<string, object> data = ReadInfo(infoFile);
            // Topic uploads also carry the release information in their generated description.
            string description = Text(data, "description");
            if (!String.IsNullOrEmpty(description))
            {
                Match match = Regex.Match(description,
                    @"\AProvided to YouTube by [^\r\n]+\r?\n\s*\r?\n(?<credit>[^\r\n]+)\r?\n\s*\r?\n(?<album>[^\r\n]+)",
                    RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    string[] credit = match.Groups["credit"].Value.Split(new string[] { " · " }, StringSplitOptions.None);
                    if (credit.Length >= 2)
                    {
                        if (Text(data, "track", "title") == null) data["track"] = credit[0].Trim();
                        if (Text(data, "artist", "artists", "creator", "creators") == null)
                            data["artists"] = new List<string>(credit).GetRange(1, credit.Length - 1).ToArray();
                        string describedAlbum = match.Groups["album"].Value.Trim();
                        if (Text(data, "album", "series") == null && !describedAlbum.StartsWith("℗") &&
                            !describedAlbum.StartsWith("©") && !describedAlbum.StartsWith("Released on:", StringComparison.OrdinalIgnoreCase) &&
                            !describedAlbum.StartsWith("Auto-generated", StringComparison.OrdinalIgnoreCase))
                            data["album"] = describedAlbum;
                    }
                }
            }
            string artist = Text(data, "artists", "artist", "creators", "creator");
            if (!String.IsNullOrEmpty(artist))
            {
                // Extractor credits sometimes repeat the same performer several times.
                string[] parts = artist.Split(new string[] { ", " }, StringSplitOptions.None);
                List<string> distinct = new List<string>();
                HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (string part in parts) if (seen.Add(part)) distinct.Add(part);
                artist = String.Join(", ", distinct.ToArray());
            }
            if (String.IsNullOrEmpty(artist))
            {
                string uploader = Text(data, "uploader");
                if (uploader != null && uploader.EndsWith(" - Topic", StringComparison.OrdinalIgnoreCase))
                    artist = uploader.Substring(0, uploader.Length - 8).Trim();
            }
            Dictionary<string, string> tags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            Add(tags, "title", Text(data, "track", "title"));
            Add(tags, "artist", artist);
            Add(tags, "album", Text(data, "album", "series"));
            Add(tags, "album_artist", Text(data, "album_artist", "album_artists") ?? artist);
            Add(tags, "date", Text(data, "release_date", "release_year", "upload_date"));
            Add(tags, "track", Text(data, "track_number"));
            Add(tags, "disc", Text(data, "disc_number"));
            Add(tags, "composer", Text(data, "composer", "composers"));
            Add(tags, "genre", Text(data, "genre", "genres"));
            Add(tags, "purl", Text(data, "webpage_url", "original_url"));
            Add(tags, "comment", Text(data, "description"));
            return tags;
        }

        private static readonly string[] TagNames = new string[]
        { "title", "artist", "album", "album_artist", "date", "track", "disc", "composer", "genre", "purl", "comment" };

        private static Dictionary<string, string> AvailableTags(Dictionary<string, string> source,
            Dictionary<string, string> actual)
        {
            Dictionary<string, string> tags = new Dictionary<string, string>(source, StringComparer.OrdinalIgnoreCase);
            foreach (string name in TagNames)
            {
                string value;
                if (!tags.ContainsKey(name) && actual.TryGetValue(name, out value) && !String.IsNullOrWhiteSpace(value))
                    tags[name] = value;
            }
            return tags;
        }

        private static bool Complete(Dictionary<string, string> tags)
        {
            return tags.ContainsKey("title") && tags.ContainsKey("artist") && tags.ContainsKey("album");
        }

        internal static Dictionary<string, string> ResolveTags(Dictionary<string, string> source,
            Dictionary<string, string> actual, string titleHint, double? duration, MetadataLookup lookup,
            Func<bool> shouldStop, Action<string> log)
        {
            Dictionary<string, string> tags = AvailableTags(source, actual);
            if (!Complete(tags))
            {
                string title, artist;
                if (!tags.TryGetValue("title", out title)) title = titleHint;
                tags.TryGetValue("artist", out artist);
                try
                {
                    Dictionary<string, string> online = lookup(title, artist, duration, shouldStop, log);
                    foreach (string name in TagNames)
                    {
                        string value;
                        if (!tags.ContainsKey(name) && online.TryGetValue(name, out value) && !String.IsNullOrWhiteSpace(value))
                            tags[name] = value;
                    }
                }
                catch (OperationCanceledException)
                {
                    if (shouldStop()) throw;
                    log("Metadata warning: online lookup timed out; the available tags will be used.");
                }
                catch (Exception ex)
                {
                    log("Metadata warning: could not retrieve online tags; the available tags will be used. " + ex.Message);
                }
            }
            string performer;
            if (!tags.ContainsKey("album_artist") && tags.TryGetValue("artist", out performer)) tags["album_artist"] = performer;
            return tags;
        }

        private static void Add(Dictionary<string, string> tags, string name, string value)
        {
            if (!String.IsNullOrEmpty(value)) tags[name] = value;
        }

        public static void ValidateAudio(string audio, string toolsDirectory, ProcessJob job, Func<bool> shouldStop)
        {
            string json = RunTool(Path.Combine(toolsDirectory, "ffprobe.exe"), new string[]
            { "-v", "error", "-show_entries", "stream=codec_type", "-of", "json", audio }, job, shouldStop);
            Dictionary<string, object> probe = ReadJson(json);
            object streams;
            if (probe.TryGetValue("streams", out streams) && streams is IEnumerable)
                foreach (object raw in (IEnumerable)streams)
                {
                    Dictionary<string, object> stream = raw as Dictionary<string, object>;
                    if (stream != null && Text(stream, "codec_type") == "audio") return;
                }
            throw new IOException("The completed download does not contain a readable audio stream.");
        }

        public static Dictionary<string, string> ReadTags(string audio, string toolsDirectory,
            ProcessJob job, Func<bool> shouldStop)
        {
            string json = RunTool(Path.Combine(toolsDirectory, "ffprobe.exe"), new string[]
            {
                "-v", "error", "-show_entries", "format_tags:stream_tags", "-of", "json", audio
            }, job, shouldStop);
            Dictionary<string, object> probe = ReadJson(json);
            Dictionary<string, string> tags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            object format;
            if (probe.TryGetValue("format", out format)) ReadTagObject(format as Dictionary<string, object>, tags);
            object streams;
            if (probe.TryGetValue("streams", out streams) && streams is IEnumerable)
                foreach (object stream in (IEnumerable)streams)
                    ReadTagObject(stream as Dictionary<string, object>, tags);
            return tags;
        }

        private static void ReadTagObject(Dictionary<string, object> container, Dictionary<string, string> result)
        {
            object raw;
            if (container == null || !container.TryGetValue("tags", out raw)) return;
            Dictionary<string, object> tags = raw as Dictionary<string, object>;
            if (tags == null) return;
            foreach (KeyValuePair<string, object> pair in tags)
                if (pair.Value != null) result[pair.Key] = Convert.ToString(pair.Value, CultureInfo.InvariantCulture);
        }

        private static bool Matches(Dictionary<string, string> actual, Dictionary<string, string> expected)
        {
            foreach (KeyValuePair<string, string> tag in expected)
            {
                string value;
                if (!actual.TryGetValue(tag.Key, out value) || value != tag.Value) return false;
            }
            return true;
        }

        public static void EnsureTags(string audio, string infoFile, string toolsDirectory,
            AppSettings settings, ProcessJob job, Func<bool> shouldStop, Action<string> log)
        {
            Dictionary<string, string> source = ExpectedTags(infoFile);
            Dictionary<string, string> actual = ReadTags(audio, toolsDirectory, job, shouldStop);
            double? duration = null;
            double seconds;
            string sourceDuration = Text(ReadInfo(infoFile), "duration");
            if (Double.TryParse(sourceDuration, NumberStyles.Any, CultureInfo.InvariantCulture, out seconds)) duration = seconds;
            if (!Complete(AvailableTags(source, actual)) && !duration.HasValue)
            {
                try
                {
                    string durationJson = RunTool(Path.Combine(toolsDirectory, "ffprobe.exe"), new string[]
                    { "-v", "error", "-show_entries", "format=duration", "-of", "json", audio }, job, shouldStop);
                    object raw;
                    Dictionary<string, object> parsed = ReadJson(durationJson);
                    if (parsed.TryGetValue("format", out raw) && raw is Dictionary<string, object> &&
                        Double.TryParse(Text((Dictionary<string, object>)raw, "duration"), NumberStyles.Any,
                            CultureInfo.InvariantCulture, out seconds)) duration = seconds;
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { log("Metadata warning: could not read duration for the catalogue match: " + ex.Message); }
            }
            string hint = Regex.Replace(Path.GetFileNameWithoutExtension(audio), @" \[[A-Za-z0-9_-]{11}\]$", "");
            Dictionary<string, string> resolved = ResolveTags(source, actual, hint, duration,
                OnlineMetadata.Lookup, shouldStop, log);
            Dictionary<string, string> expected = SelectTags(resolved, settings);
            if (!IsClean(actual, expected))
            {
                string temporary = Path.Combine(Path.GetDirectoryName(audio),
                    "tags-" + Guid.NewGuid().ToString("N") + Path.GetExtension(audio));
                try
                {
                    List<string> args = new List<string>(new string[]
                    {
                        "-hide_banner", "-loglevel", "error", "-nostdin", "-y", "-i", audio,
                        "-map", "0:a:0", "-vn", "-map_metadata", "-1", "-map_metadata:s:a:0", "-1", "-c:a", "copy"
                    });
                    foreach (KeyValuePair<string, string> tag in expected)
                    {
                        args.Add("-metadata");
                        args.Add(tag.Key + "=" + tag.Value);
                        args.Add("-metadata:s:a:0");
                        args.Add(tag.Key + "=" + tag.Value);
                    }
                    args.Add(temporary);
                    RunTool(Path.Combine(toolsDirectory, "ffmpeg.exe"), args.ToArray(), job, shouldStop);
                    if (shouldStop()) throw new OperationCanceledException();
                    File.Replace(temporary, audio, null);
                }
                finally
                {
                    if (File.Exists(temporary)) File.Delete(temporary);
                }
                actual = ReadTags(audio, toolsDirectory, job, shouldStop);
                if (!Matches(actual, expected) || HasUnselectedTags(actual, expected))
                {
                    List<string> different = new List<string>();
                    foreach (KeyValuePair<string, string> tag in expected)
                    {
                        string value;
                        if (!actual.TryGetValue(tag.Key, out value) || value != tag.Value) different.Add(tag.Key);
                    }
                    throw new InvalidOperationException("Could not verify these metadata fields: " + String.Join(", ", different.ToArray()) + ".");
                }
            }
            List<string> missing = new List<string>();
            foreach (string name in new string[] { "title", "artist", "album" })
                if (!expected.ContainsKey(name)) missing.Add(name);
            if (missing.Count > 0)
                log("Tags verified where available. Still unknown: " + String.Join(", ", missing.ToArray()) + ". The audio will be saved.");
            else
                log("Tags verified: " + expected["title"] + " / " + expected["artist"] + " / " + expected["album"]);
        }

        public static void EnsureTags(string audio, string infoFile, string toolsDirectory,
            ProcessJob job, Func<bool> shouldStop, Action<string> log)
        {
            EnsureTags(audio, infoFile, toolsDirectory, AppSettings.Defaults(), job, shouldStop, log);
        }

        private static Dictionary<string, string> SelectTags(Dictionary<string, string> available, AppSettings settings)
        {
            Dictionary<string, string> result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, string> tag in available)
            {
                bool selected = tag.Key == "title" ? settings.TagTitle :
                    tag.Key == "artist" ? settings.TagArtist :
                    tag.Key == "album" ? settings.TagAlbum :
                    tag.Key == "album_artist" ? settings.TagAlbumArtist :
                    tag.Key == "date" ? settings.TagDate :
                    tag.Key == "track" ? settings.TagTrack :
                    tag.Key == "disc" ? settings.TagDisc :
                    tag.Key == "genre" ? settings.TagGenre :
                    tag.Key == "composer" ? settings.TagComposer :
                    tag.Key == "purl" ? settings.TagSourceUrl :
                    tag.Key == "comment" ? settings.TagDescription : false;
                if (selected && AudioFormats.SupportsTag(settings.AudioFormat, tag.Key) && !String.IsNullOrWhiteSpace(tag.Value))
                    result[tag.Key] = tag.Value;
            }
            return result;
        }

        private static bool IsClean(Dictionary<string, string> actual, Dictionary<string, string> expected)
        {
            return Matches(actual, expected) && !HasUnselectedTags(actual, expected);
        }

        private static bool HasUnselectedTags(Dictionary<string, string> actual, Dictionary<string, string> expected)
        {
            string[] controlled = new string[] { "title", "artist", "album", "album_artist", "date", "year", "track", "tracknumber", "disc", "discnumber", "genre", "composer", "purl", "comment", "description", "REPLAYGAIN_TRACK_GAIN", "REPLAYGAIN_TRACK_PEAK", "REPLAYGAIN_ALBUM_GAIN", "REPLAYGAIN_ALBUM_PEAK", "R128_TRACK_GAIN", "R128_ALBUM_GAIN", "iTunNORM" };
            foreach (string name in controlled)
                if (actual.ContainsKey(name) && !expected.ContainsKey(name)) return true;
            return false;
        }

        public static string RunTool(string tool, string[] args, ProcessJob job, Func<bool> shouldStop)
        {
            if (shouldStop()) throw new OperationCanceledException();
            StringBuilder command = new StringBuilder();
            foreach (string arg in args)
            {
                if (command.Length > 0) command.Append(' ');
                command.Append(QuoteArgument(arg));
            }
            using (Process process = new Process())
            {
                process.StartInfo = new ProcessStartInfo
                {
                    FileName = tool, Arguments = command.ToString(),
                    WorkingDirectory = Path.GetDirectoryName(tool),
                    UseShellExecute = false, CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    RedirectStandardOutput = true, RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
                };
                bool started = false;
                try
                {
                    if (!process.Start()) throw new IOException("Windows could not start " + Path.GetFileName(tool));
                    started = true;
                    job.AssignProcess(process);
                    Task<string> stdout = process.StandardOutput.ReadToEndAsync();
                    Task<string> stderr = process.StandardError.ReadToEndAsync();
                    while (!process.WaitForExit(100))
                    {
                        if (shouldStop())
                        {
                            job.Terminate();
                            process.WaitForExit();
                            throw new OperationCanceledException();
                        }
                    }
                    process.WaitForExit();
                    if (shouldStop()) throw new OperationCanceledException();
                    if (!job.WaitUntilEmpty(5000))
                    {
                        job.Terminate();
                        throw new IOException("The metadata tool process did not stop cleanly.");
                    }
                    if (process.ExitCode != 0)
                        throw new IOException(Path.GetFileName(tool) + " could not process the tags: " + stderr.Result.Trim());
                    return stdout.Result;
                }
                finally
                {
                    if (started)
                    {
                        try { if (!process.HasExited) { process.Kill(); process.WaitForExit(); } }
                        catch { }
                    }
                }
            }
        }

        private static string QuoteArgument(string value)
        {
            StringBuilder result = new StringBuilder("\"");
            int slashes = 0;
            foreach (char c in value)
            {
                if (c == '\\') { slashes++; continue; }
                result.Append('\\', c == '"' ? slashes * 2 + 1 : slashes);
                result.Append(c);
                slashes = 0;
            }
            result.Append('\\', slashes * 2);
            result.Append('"');
            return result.ToString();
        }
    }
}
