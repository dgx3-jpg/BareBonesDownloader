using System;
using System.Net;
using System.Text.RegularExpressions;

namespace BareBonesDownloader
{
    internal sealed class YoutubeVideo
    {
        public string Id { get; private set; }
        public string CanonicalUrl { get; private set; }

        public YoutubeVideo(string id)
        {
            Id = id;
            CanonicalUrl = "https://www.youtube.com/watch?v=" + id;
        }
    }

    internal static class YoutubeUrlParser
    {
        private static readonly Regex VideoIdPattern =
            new Regex("^[A-Za-z0-9_-]{11}$", RegexOptions.CultureInvariant);

        public static bool TryParse(string text, out YoutubeVideo video, out string error)
        {
            video = null;
            error = null;

            if (String.IsNullOrWhiteSpace(text))
            {
                error = "Empty input.";
                return false;
            }

            Uri uri;
            if (!Uri.TryCreate(text.Trim(), UriKind.Absolute, out uri))
            {
                error = "This is not a complete HTTP or HTTPS link.";
                return false;
            }

            if (!String.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
                !String.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                error = "Only HTTP and HTTPS links are accepted.";
                return false;
            }

            if (!String.IsNullOrEmpty(uri.UserInfo))
            {
                error = "Links containing user information are not accepted.";
                return false;
            }

            int expectedPort = String.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
                ? 443 : 80;
            if (uri.Port != expectedPort)
            {
                error = "YouTube links using a non-standard port are not accepted.";
                return false;
            }

            string host = uri.DnsSafeHost.ToLowerInvariant();
            bool isYouTubeHost = host == "youtube.com" ||
                host.EndsWith(".youtube.com", StringComparison.Ordinal);
            bool isShortHost = host == "youtu.be";

            if (!isYouTubeHost && !isShortHost)
            {
                error = "The link must be from youtube.com or youtu.be.";
                return false;
            }

            string id = null;
            string[] pathParts = uri.AbsolutePath.Split(
                new char[] { '/' }, StringSplitOptions.RemoveEmptyEntries);

            if (isShortHost)
            {
                if (pathParts.Length == 1)
                    id = pathParts[0];
                else
                    error = "This youtu.be link does not identify one video.";
            }
            else if (pathParts.Length == 1 &&
                String.Equals(pathParts[0], "watch", StringComparison.OrdinalIgnoreCase))
            {
                id = GetQueryValue(uri.Query, "v");
                if (String.IsNullOrEmpty(id))
                    error = "This watch link is missing its video ID.";
            }
            else if (pathParts.Length == 2 &&
                String.Equals(pathParts[0], "shorts", StringComparison.OrdinalIgnoreCase))
            {
                id = pathParts[1];
            }
            else if (pathParts.Length > 0 &&
                String.Equals(pathParts[0], "live", StringComparison.OrdinalIgnoreCase))
            {
                error = "Live stream links are not supported.";
            }
            else if (pathParts.Length > 0 &&
                (String.Equals(pathParts[0], "playlist", StringComparison.OrdinalIgnoreCase) ||
                 String.Equals(pathParts[0], "channel", StringComparison.OrdinalIgnoreCase) ||
                 String.Equals(pathParts[0], "user", StringComparison.OrdinalIgnoreCase) ||
                 String.Equals(pathParts[0], "c", StringComparison.OrdinalIgnoreCase) ||
                 pathParts[0].StartsWith("@", StringComparison.Ordinal)))
            {
                error = "Channel and playlist links are not supported; paste one video link.";
            }
            else
            {
                error = "This link is not a supported YouTube video URL.";
            }

            if (!String.IsNullOrEmpty(id))
            {
                id = WebUtility.UrlDecode(id);
                if (!VideoIdPattern.IsMatch(id))
                {
                    error = "The link does not contain a valid YouTube video ID.";
                    return false;
                }

                video = new YoutubeVideo(id);
                return true;
            }

            if (String.IsNullOrEmpty(error))
                error = "This link does not identify one YouTube video.";
            return false;
        }

        private static string GetQueryValue(string query, string wantedKey)
        {
            if (String.IsNullOrEmpty(query))
                return null;

            string[] parts = query.TrimStart('?').Split('&');
            int i;
            for (i = 0; i < parts.Length; i++)
            {
                int equals = parts[i].IndexOf('=');
                string key = equals < 0 ? parts[i] : parts[i].Substring(0, equals);
                if (String.Equals(WebUtility.UrlDecode(key), wantedKey, StringComparison.OrdinalIgnoreCase))
                    return equals < 0 ? String.Empty : parts[i].Substring(equals + 1);
            }
            return null;
        }
    }
}
