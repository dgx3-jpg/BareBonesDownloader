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
                AudioFormat = "best", QualityMode = "default", VariableQuality = 5,
                BitrateKbps = 192, TagTitle = true, TagArtist = true, TagAlbum = true,
                TagAlbumArtist = true, TagDate = true, TagTrack = true, TagDisc = true,
                TagGenre = true, TagComposer = true, TagSourceUrl = true,
                TagDescription = true, ReplayGain = true
            };
        }

        public AppSettings Copy()
        {
            return (AppSettings)MemberwiseClone();
        }

        public void Normalize()
        {
            if (String.IsNullOrEmpty(AudioFormat) || !AudioFormats.IsKnown(AudioFormat)) AudioFormat = "best";
            if (QualityMode != "default" && QualityMode != "variable" && QualityMode != "bitrate") QualityMode = "default";
            if (VariableQuality < 0 || VariableQuality > 10) VariableQuality = 5;
            if (BitrateKbps < 32 || BitrateKbps > 512) BitrateKbps = 192;
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
            string path = SettingsPath;
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
            if (settings == null) throw new ArgumentNullException("settings");
            settings.Normalize();
            if (!String.IsNullOrWhiteSpace(settings.Destination) && !Path.IsPathRooted(settings.Destination))
                throw new ArgumentException("The destination must be a full path.", "settings");
            if (!String.IsNullOrWhiteSpace(settings.Destination)) settings.Destination = Path.GetFullPath(settings.Destination);
            string filePath = SettingsPath;
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
