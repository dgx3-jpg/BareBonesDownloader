using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace BareBonesDownloader
{
    [DataContract]
    internal sealed class AppSettings
    {
        [DataMember(Name = "destination", EmitDefaultValue = false)] public string Destination { get; set; }
        [DataMember(Name = "videoDestination", EmitDefaultValue = false)] public string VideoDestination { get; set; }
        [DataMember(Name = "theme", EmitDefaultValue = false)] public string Theme { get; set; }
        [DataMember(Name = "audioMaxSizeMb", EmitDefaultValue = false)] public int AudioMaxSizeMb { get; set; }
        [DataMember(Name = "videoMaxSizeMb", EmitDefaultValue = false)] public int VideoMaxSizeMb { get; set; }
        [DataMember(Name = "videoMaxHeight", EmitDefaultValue = false)] public int VideoMaxHeight { get; set; }
        [DataMember(Name = "appendYouTubeUrlToFilename", EmitDefaultValue = false)] public bool AppendYouTubeUrlToFilename { get; set; }
        [DataMember(Name = "audioFormat", EmitDefaultValue = false)] public string AudioFormat { get; set; }
        [DataMember(Name = "qualityMode", EmitDefaultValue = false)] public string QualityMode { get; set; }
        [DataMember(Name = "variableQuality", EmitDefaultValue = false)] public int VariableQuality { get; set; }
        [DataMember(Name = "bitrateKbps", EmitDefaultValue = false)] public int BitrateKbps { get; set; }
        [DataMember(Name = "tagTitle")] public bool TagTitle { get; set; }
        [DataMember(Name = "tagArtist")] public bool TagArtist { get; set; }
        [DataMember(Name = "tagAlbum")] public bool TagAlbum { get; set; }
        [DataMember(Name = "tagAlbumArtist")] public bool TagAlbumArtist { get; set; }
        [DataMember(Name = "tagDate")] public bool TagDate { get; set; }
        [DataMember(Name = "tagTrack")] public bool TagTrack { get; set; }
        [DataMember(Name = "tagDisc")] public bool TagDisc { get; set; }
        [DataMember(Name = "tagGenre")] public bool TagGenre { get; set; }
        [DataMember(Name = "tagComposer")] public bool TagComposer { get; set; }
        [DataMember(Name = "tagSourceUrl")] public bool TagSourceUrl { get; set; }
        [DataMember(Name = "tagDescription")] public bool TagDescription { get; set; }
        [DataMember(Name = "replayGain")] public bool ReplayGain { get; set; }

        public static AppSettings Defaults()
        {
            return new AppSettings {
                Theme = "light", AudioFormat = "best", QualityMode = "default", VariableQuality = 5,
                BitrateKbps = 192, TagTitle = true, TagArtist = true, TagAlbum = true,
                TagAlbumArtist = true, TagDate = true, TagTrack = true, TagDisc = true,
                TagGenre = true, TagComposer = true, TagSourceUrl = true,
                TagDescription = true, ReplayGain = true,
                AudioMaxSizeMb = 0, VideoMaxSizeMb = 0, VideoMaxHeight = 0, AppendYouTubeUrlToFilename = false
            };
        }

        public AppSettings Copy()
        {
            return (AppSettings)MemberwiseClone();
        }

        public void Normalize()
        {
            if (Theme != "light" && Theme != "dark") Theme = "light";
            if (String.IsNullOrEmpty(AudioFormat) || !AudioFormats.IsKnown(AudioFormat)) AudioFormat = "best";
            if (QualityMode != "default" && QualityMode != "variable" && QualityMode != "bitrate") QualityMode = "default";
            if (VariableQuality < 0 || VariableQuality > 10) VariableQuality = 5;
            if (BitrateKbps < 32 || BitrateKbps > 512) BitrateKbps = 192;
            if (AudioMaxSizeMb < 0 || AudioMaxSizeMb > 16384) AudioMaxSizeMb = 0;
            if (VideoMaxSizeMb < 0 || VideoMaxSizeMb > 16384) VideoMaxSizeMb = 0;
            if (VideoMaxHeight != 0 && VideoMaxHeight != 360 && VideoMaxHeight != 480 &&
                VideoMaxHeight != 720 && VideoMaxHeight != 1080 && VideoMaxHeight != 1440 &&
                VideoMaxHeight != 2160) VideoMaxHeight = 0;
            AudioFormatOption format = AudioFormats.Find(AudioFormat);
            if (format == null || format.Id == "best" ||
                (QualityMode == "variable" && !format.HasQuality) ||
                (QualityMode == "bitrate" && !format.HasBitrate)) QualityMode = "default";
        }
    }

    internal static class SettingsStore
    {
        public static string SettingsPath
        {
            get
            {
                string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                return Path.Combine(Path.Combine(local, "BareBonesDownloader"), "settings.json");
            }
        }

        public static AppSettings Load(out string problem)
        {
            problem = null;
            return LoadFromPath(SettingsPath, out problem);
        }

        public static AppSettings LoadFromPath(string path, out string problem)
        {
            problem = null;
            AppSettings settings = AppSettings.Defaults();
            if (!File.Exists(path)) return settings;
            try
            {
                string json = File.ReadAllText(path, Encoding.UTF8);
                using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                    settings = new DataContractJsonSerializer(typeof(AppSettings)).ReadObject(stream) as AppSettings;
                if (settings == null) settings = AppSettings.Defaults();
                AppSettings defaults = AppSettings.Defaults();
                if (!Has(json, "audioFormat")) settings.AudioFormat = defaults.AudioFormat;
                if (!Has(json, "audioMaxSizeMb")) settings.AudioMaxSizeMb = defaults.AudioMaxSizeMb;
                if (!Has(json, "videoMaxSizeMb")) settings.VideoMaxSizeMb = defaults.VideoMaxSizeMb;
                if (!Has(json, "videoMaxHeight")) settings.VideoMaxHeight = defaults.VideoMaxHeight;
                if (!Has(json, "appendYouTubeUrlToFilename")) settings.AppendYouTubeUrlToFilename = defaults.AppendYouTubeUrlToFilename;
                if (!Has(json, "qualityMode")) settings.QualityMode = defaults.QualityMode;
                if (!Has(json, "variableQuality")) settings.VariableQuality = defaults.VariableQuality;
                if (!Has(json, "bitrateKbps")) settings.BitrateKbps = defaults.BitrateKbps;
                if (!Has(json, "tagTitle")) settings.TagTitle = defaults.TagTitle;
                if (!Has(json, "tagArtist")) settings.TagArtist = defaults.TagArtist;
                if (!Has(json, "tagAlbum")) settings.TagAlbum = defaults.TagAlbum;
                if (!Has(json, "tagAlbumArtist")) settings.TagAlbumArtist = defaults.TagAlbumArtist;
                if (!Has(json, "tagDate")) settings.TagDate = defaults.TagDate;
                if (!Has(json, "tagTrack")) settings.TagTrack = defaults.TagTrack;
                if (!Has(json, "tagDisc")) settings.TagDisc = defaults.TagDisc;
                if (!Has(json, "tagGenre")) settings.TagGenre = defaults.TagGenre;
                if (!Has(json, "tagComposer")) settings.TagComposer = defaults.TagComposer;
                if (!Has(json, "tagSourceUrl")) settings.TagSourceUrl = defaults.TagSourceUrl;
                if (!Has(json, "tagDescription")) settings.TagDescription = defaults.TagDescription;
                if (!Has(json, "replayGain")) settings.ReplayGain = defaults.ReplayGain;
                settings.Normalize();
                return settings;
            }
            catch (Exception ex)
            {
                problem = "The saved settings could not be read. Defaults will be used. " + ex.Message;
                return AppSettings.Defaults();
            }
        }

        private static bool Has(string json, string property)
        {
            return json.IndexOf("\"" + property + "\"", StringComparison.Ordinal) >= 0;
        }

        public static void Save(AppSettings settings)
        {
            SaveToPath(settings, SettingsPath);
        }

        public static void SaveToPath(AppSettings settings, string filePath)
        {
            if (settings == null) throw new ArgumentNullException("settings");
            settings.Normalize();
            if (!String.IsNullOrWhiteSpace(settings.Destination) && !Path.IsPathRooted(settings.Destination))
                throw new ArgumentException("The destination must be a full path.", "settings");
            if (!String.IsNullOrWhiteSpace(settings.VideoDestination) && !Path.IsPathRooted(settings.VideoDestination))
                throw new ArgumentException("The video destination must be a full path.", "settings");
            if (!String.IsNullOrWhiteSpace(settings.Destination)) settings.Destination = Path.GetFullPath(settings.Destination);
            if (!String.IsNullOrWhiteSpace(settings.VideoDestination)) settings.VideoDestination = Path.GetFullPath(settings.VideoDestination);
            Directory.CreateDirectory(Path.GetDirectoryName(filePath));
            string temporaryPath = filePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (FileStream stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    new DataContractJsonSerializer(typeof(AppSettings)).WriteObject(stream, settings);
                if (File.Exists(filePath)) File.Replace(temporaryPath, filePath, null);
                else File.Move(temporaryPath, filePath);
            }
            finally { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); }
        }
    }
}
