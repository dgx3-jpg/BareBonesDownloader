using System;
using System.Collections.Generic;
using System.IO;

namespace BareBonesDownloader
{
    internal sealed class AudioFormatOption
    {
        public string Id;
        public string Label;
        public string Extension;
        public string Encoder;
        public string CopyCodecs;
        public bool HasQuality;
        public bool HasBitrate;
        public override string ToString() { return Label; }
    }

    internal static class AudioFormats
    {
        private static readonly AudioFormatOption[] Options = new AudioFormatOption[] {
            new AudioFormatOption { Id="best", Label="Best available (source format)", Extension="source", CopyCodecs="*" },
            new AudioFormatOption { Id="aac", Label="AAC (AAC-in-M4A)", Extension="m4a", Encoder="aac", CopyCodecs="aac", HasQuality=true, HasBitrate=true },
            new AudioFormatOption { Id="alac", Label="ALAC (ALAC-in-M4A)", Extension="m4a", Encoder="alac", CopyCodecs="alac" },
            new AudioFormatOption { Id="flac", Label="FLAC (.flac)", Extension="flac", Encoder="flac", CopyCodecs="flac" },
            new AudioFormatOption { Id="m4a", Label="M4A (AAC-in-M4A)", Extension="m4a", Encoder="aac", CopyCodecs="aac", HasQuality=true, HasBitrate=true },
            new AudioFormatOption { Id="mp3", Label="MP3 (.mp3)", Extension="mp3", Encoder="libmp3lame", CopyCodecs="mp3", HasQuality=true, HasBitrate=true },
            new AudioFormatOption { Id="opus", Label="Opus (.opus)", Extension="opus", Encoder="libopus", CopyCodecs="opus", HasBitrate=true },
            new AudioFormatOption { Id="vorbis", Label="Vorbis (.ogg)", Extension="ogg", Encoder="libvorbis", CopyCodecs="vorbis", HasQuality=true, HasBitrate=true },
            new AudioFormatOption { Id="wav", Label="WAV (PCM, .wav)", Extension="wav", Encoder="pcm_s16le", CopyCodecs="pcm_s16le" }
        };

        public static AudioFormatOption[] All { get { return (AudioFormatOption[])Options.Clone(); } }
        public static bool IsKnown(string id) { return Find(id) != null; }
        public static AudioFormatOption Find(string id)
        {
            foreach (AudioFormatOption option in Options)
                if (String.Equals(option.Id, id, StringComparison.OrdinalIgnoreCase)) return option;
            return null;
        }

        public static bool SupportsReplayGain(string id)
        {
            // Matches rsgain's supported extensions. Every listed yt-dlp format maps to one.
            AudioFormatOption option = Find(id);
            return option != null;
        }

        public static bool SupportsTag(string id, string field)
        {
            // MP4 does not expose a portable source-URL field; the WAV muxer cannot
            // reliably store album artist, composer, or source URL metadata.
            if ((id == "aac" || id == "alac" || id == "m4a") && field == "purl") return false;
            if (id == "wav" && (field == "composer" || field == "purl" || field == "album_artist")) return false;
            return Find(id) != null;
        }

        public static string ExtensionFor(string id, string inputPath)
        {
            AudioFormatOption option = Find(id);
            return option == null || option.Id == "best" ? Path.GetExtension(inputPath) : "." + option.Extension;
        }
    }
}
