using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace BareBonesDownloader
{
    internal delegate Dictionary<string, string> MetadataLookup(string title, string artist,
        double? duration, Func<bool> shouldStop, Action<string> log);

    internal sealed class CatalogueTrack
    {
        public string Title, Artist, Album, Date, Track, Disc, Genre;
        public double? Duration;
    }

    internal static class OnlineMetadata
    {
        private static readonly object RequestGate = new object();
        private static DateTime _nextRequest = DateTime.MinValue;
        private static readonly HttpClient Client = CreateClient();

        private static HttpClient CreateClient()
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            HttpClient client = new HttpClient();
            client.Timeout = TimeSpan.FromSeconds(5);
            client.MaxResponseContentBufferSize = 2 * 1024 * 1024;
            client.DefaultRequestHeaders.UserAgent.ParseAdd("BareBonesDownloader/1.0");
            return client;
        }

        public static Dictionary<string, string> Lookup(string title, string artist, double? duration,
            Func<bool> shouldStop, Action<string> log)
        {
            return Lookup(title, artist, duration, shouldStop, log, DownloadJson);
        }

        internal static Dictionary<string, string> Lookup(string title, string artist, double? duration,
            Func<bool> shouldStop, Action<string> log, Func<string, Func<bool>, string> fetch)
        {
            Dictionary<string, string> result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (String.IsNullOrWhiteSpace(title) || Normalize(title).Length == 0 || (String.IsNullOrWhiteSpace(artist) && !duration.HasValue))
            {
                log("Online tags need a song title and an artist or duration; keeping the available tags.");
                return result;
            }
            log("Searching the online music catalogue for missing tags: " + title);
            bool japanese = ContainsJapanese(title + " " + artist);
            string[] countries = japanese ? new string[] { "jp", "gb" } : new string[] { "gb", "jp" };
            foreach (string country in countries)
            {
                if (shouldStop()) throw new OperationCanceledException();
                try
                {
                    string query = (title + " " + artist).Trim();
                    string url = "https://itunes.apple.com/search?media=music&entity=song&limit=25&country=" +
                        country + "&term=" + Uri.EscapeDataString(query);
                    Dictionary<string, string> found = Select(Parse(fetch(url, shouldStop)), title, artist, duration);
                    string priorArtist, foundArtist;
                    if (result.TryGetValue("artist", out priorArtist) && found.TryGetValue("artist", out foundArtist) &&
                        Normalize(priorArtist) != Normalize(foundArtist))
                    {
                        log("Catalogue regions disagree about the performer; keeping the known tags.");
                        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    }
                    foreach (KeyValuePair<string, string> pair in found)
                        if (!result.ContainsKey(pair.Key)) result[pair.Key] = pair.Value;
                    if (found.ContainsKey("album"))
                    {
                        log("Found matching music catalogue metadata (" + country.ToUpperInvariant() + ").");
                        return result;
                    }
                }
                catch (OperationCanceledException)
                {
                    if (shouldStop()) throw;
                    log("Metadata warning: catalogue lookup timed out; trying the next region.");
                }
                catch (Exception ex)
                {
                    log("Metadata warning: the music catalogue lookup was unavailable: " + ex.Message);
                }
            }
            log("No unambiguous catalogue album match; unknown fields will stay empty.");
            return result;
        }

        private static bool ContainsJapanese(string value)
        {
            foreach (char c in value)
                if ((c >= 0x3040 && c <= 0x30ff) || (c >= 0x3400 && c <= 0x9fff)) return true;
            return false;
        }

        internal static string Normalize(string value)
        {
            StringBuilder text = new StringBuilder();
            foreach (char c in (value ?? String.Empty).Normalize(NormalizationForm.FormKC).ToLowerInvariant())
                if (Char.IsLetterOrDigit(c)) text.Append(c);
            return text.ToString();
        }

        internal static List<CatalogueTrack> Parse(string json)
        {
            JavaScriptSerializer serializer = new JavaScriptSerializer();
            serializer.MaxJsonLength = 2 * 1024 * 1024;
            Dictionary<string, object> root = serializer.Deserialize<Dictionary<string, object>>(json);
            List<CatalogueTrack> tracks = new List<CatalogueTrack>();
            object entries;
            if (root == null || !root.TryGetValue("results", out entries) || !(entries is IEnumerable)) return tracks;
            foreach (object entry in (IEnumerable)entries)
            {
                Dictionary<string, object> item = entry as Dictionary<string, object>;
                if (item == null || Get(item, "kind") != "song") continue;
                double milliseconds;
                CatalogueTrack track = new CatalogueTrack
                {
                    Title = Get(item, "trackName"), Artist = Get(item, "artistName"),
                    Album = Get(item, "collectionName"), Date = Get(item, "releaseDate"),
                    Track = Get(item, "trackNumber"), Disc = Get(item, "discNumber"), Genre = Get(item, "primaryGenreName")
                };
                if (Double.TryParse(Get(item, "trackTimeMillis"), NumberStyles.Any, CultureInfo.InvariantCulture, out milliseconds))
                    track.Duration = milliseconds / 1000;
                if (!String.IsNullOrEmpty(track.Date) && track.Date.Length >= 10)
                    track.Date = track.Date.Substring(0, 10).Replace("-", "");
                tracks.Add(track);
            }
            return tracks;
        }

        private static string Get(Dictionary<string, object> item, string key)
        {
            object value;
            return item.TryGetValue(key, out value) && value != null
                ? Convert.ToString(value, CultureInfo.InvariantCulture).Trim() : null;
        }

        internal static Dictionary<string, string> Select(IEnumerable<CatalogueTrack> candidates,
            string title, string artist, double? duration)
        {
            List<CatalogueTrack> matches = new List<CatalogueTrack>();
            foreach (CatalogueTrack candidate in candidates)
            {
                if (Normalize(candidate.Title) != Normalize(title) || String.IsNullOrWhiteSpace(candidate.Artist)) continue;
                if (!String.IsNullOrWhiteSpace(artist) && Normalize(candidate.Artist) != Normalize(artist)) continue;
                if (duration.HasValue)
                {
                    if (!candidate.Duration.HasValue || Math.Abs(candidate.Duration.Value - duration.Value) > Math.Max(2, duration.Value * 0.01)) continue;
                }
                else if (String.IsNullOrWhiteSpace(artist)) continue;
                matches.Add(candidate);
            }
            Dictionary<string, string> tags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (matches.Count == 0) return tags;
            string agreedArtist = Consensus(matches, delegate(CatalogueTrack t) { return t.Artist; });
            if (String.IsNullOrEmpty(agreedArtist)) return tags;
            tags["title"] = matches[0].Title;
            tags["artist"] = agreedArtist;
            string album = Consensus(matches, delegate(CatalogueTrack t) { return t.Album; });
            if (!String.IsNullOrEmpty(album))
            {
                tags["album"] = album;
                tags["album_artist"] = agreedArtist;
                Add(tags, "date", Consensus(matches, delegate(CatalogueTrack t) { return t.Date; }));
                Add(tags, "track", Consensus(matches, delegate(CatalogueTrack t) { return t.Track; }));
                Add(tags, "disc", Consensus(matches, delegate(CatalogueTrack t) { return t.Disc; }));
                Add(tags, "genre", Consensus(matches, delegate(CatalogueTrack t) { return t.Genre; }));
            }
            return tags;
        }

        private static string Consensus(List<CatalogueTrack> tracks, Func<CatalogueTrack, string> field)
        {
            string value = null;
            foreach (CatalogueTrack track in tracks)
            {
                string candidate = field(track);
                if (String.IsNullOrWhiteSpace(candidate)) return null;
                if (value == null) value = candidate;
                else if (Normalize(candidate) != Normalize(value)) return null;
            }
            return value;
        }

        private static void Add(Dictionary<string, string> tags, string name, string value)
        {
            if (!String.IsNullOrEmpty(value)) tags[name] = value;
        }

        private static string DownloadJson(string url, Func<bool> shouldStop)
        {
            lock (RequestGate)
            {
                while (DateTime.UtcNow < _nextRequest)
                {
                    if (shouldStop()) throw new OperationCanceledException();
                    Thread.Sleep(50);
                }
                _nextRequest = DateTime.UtcNow.AddMilliseconds(3100);
            }
            using (CancellationTokenSource cancellation = new CancellationTokenSource())
            {
                var pending = Client.GetAsync(url, cancellation.Token);
                while (!pending.IsCompleted)
                {
                    if (shouldStop())
                    {
                        cancellation.Cancel();
                        throw new OperationCanceledException();
                    }
                    Thread.Sleep(50);
                }
                using (HttpResponseMessage response = pending.GetAwaiter().GetResult())
                {
                    response.EnsureSuccessStatusCode();
                    return response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                }
            }
        }
    }
}
