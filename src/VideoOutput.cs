using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Web.Script.Serialization;

namespace BareBonesDownloader
{
    internal static class VideoOutput
    {
        public static void Validate(string path, string ffprobe, ProcessJob job, Func<bool> shouldStop)
        {
            if (!String.Equals(Path.GetExtension(path), ".mp4", StringComparison.OrdinalIgnoreCase))
                throw new IOException("The completed video is not an MP4 file.");

            string json = MusicMetadata.RunTool(ffprobe, new string[]
            {
                "-v", "error", "-show_entries",
                "stream=codec_type,codec_name:stream_disposition=attached_pic", "-of", "json", path
            }, job, shouldStop);
            Dictionary<string, object> probe = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(json);
            object rawStreams;
            if (probe == null || !probe.TryGetValue("streams", out rawStreams) || !(rawStreams is IEnumerable))
                throw new IOException("The downloaded MP4 has no readable audio or video streams.");

            bool video = false;
            bool audio = false;
            foreach (object raw in (IEnumerable)rawStreams)
            {
                Dictionary<string, object> stream = raw as Dictionary<string, object>;
                if (stream == null) continue;
                string type = Text(stream, "codec_type");
                string codec = Text(stream, "codec_name");
                if (type == "video" && codec == "h264" && !IsAttachedPicture(stream)) video = true;
                if (type == "audio" && codec == "aac") audio = true;
            }

            if (!video || !audio)
                throw new IOException("The downloaded MP4 must contain H.264 video and AAC audio.");
        }

        private static bool IsAttachedPicture(Dictionary<string, object> stream)
        {
            object raw;
            if (!stream.TryGetValue("disposition", out raw)) return false;
            Dictionary<string, object> disposition = raw as Dictionary<string, object>;
            if (disposition == null || !disposition.TryGetValue("attached_pic", out raw)) return false;
            return Convert.ToInt32(raw, CultureInfo.InvariantCulture) != 0;
        }

        private static string Text(Dictionary<string, object> values, string name)
        {
            object value;
            return values.TryGetValue(name, out value) ? Convert.ToString(value, CultureInfo.InvariantCulture) : null;
        }
    }
}
