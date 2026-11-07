using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Windows.Forms;

namespace BareBonesDownloader
{
    internal sealed class SettingsForm : Form
    {
        private readonly AppSettings _initial;
        private readonly string _settingsPath;
        private AppSettings _result;
        private TextBox _audioDestination;
        private TextBox _videoDestination;
        private CheckBox _appendYouTubeUrlToFilename;
        private ComboBox _theme;
        private ComboBox _format;
        private ComboBox _qualityMode;
        private ComboBox _videoQuality;
        private NumericUpDown _quality;
        private NumericUpDown _bitrate;
        private NumericUpDown _audioSizeLimit;
        private NumericUpDown _videoSizeLimit;
        private CheckBox _audioSizeLimitEnabled;
        private CheckBox _videoSizeLimitEnabled;
        private Label _qualityLabel;
        private Label _bitrateLabel;
        private Label _qualityHelp;
        private CheckBox _replayGain;
        private Label _replayGainStatus;
        private readonly Dictionary<string, CheckBox> _metadata = new Dictionary<string, CheckBox>();
        private readonly Dictionary<string, Label> _previewValues = new Dictionary<string, Label>();
        private readonly Dictionary<string, Label> _availability = new Dictionary<string, Label>();
        private readonly string[] _fields = { "title", "artist", "album", "album_artist", "date", "track", "disc", "genre", "composer", "purl", "description" };
        private readonly string[] _labels = { "Title", "Artist", "Album", "Album artist", "Release date/year", "Track number", "Disc number", "Genre", "Composer", "Source URL", "Description/comment" };
        private readonly string[] _sample = { "Here Comes the Sun", "The Beatles", "Abbey Road", "The Beatles", "26 September 1969", "7", null, null, "George Harrison", null, null };

        public SettingsForm(AppSettings settings)
            : this(settings, SettingsStore.SettingsPath)
        {
        }

        internal SettingsForm(AppSettings settings, string settingsPath)
        {
            _initial = settings.Copy();
            _settingsPath = settingsPath;
            Text = "Settings";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(680, 540);
            Icon = AppTheme.CreateApplicationIcon();
            BuildUi();
            LoadSettings(_initial);
            AppTheme.Apply(this, _initial.Theme);
        }

        public AppSettings Result { get { return _result == null ? null : _result.Copy(); } }

        private void BuildUi()
        {
            TabControl tabs = new TabControl { Dock = DockStyle.Fill };
            TabPage general = new TabPage("General");
            TabPage audio = new TabPage("Audio");
            TabPage video = new TabPage("Video");
            TabPage metadata = new TabPage("Metadata");
            TabPage appearance = new TabPage("Appearance");
            tabs.TabPages.Add(general); tabs.TabPages.Add(audio); tabs.TabPages.Add(video);
            tabs.TabPages.Add(metadata); tabs.TabPages.Add(appearance);
            Controls.Add(tabs);

            TableLayoutPanel generalLayout = PageLayout();
            generalLayout.ColumnCount = 3;
            generalLayout.ColumnStyles.Clear();
            generalLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
            generalLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            generalLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            generalLayout.Dock = DockStyle.Top;
            generalLayout.Height = 156;
            generalLayout.AutoScroll = false;
            generalLayout.RowCount = 4;
            generalLayout.RowStyles.Clear();
            generalLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            generalLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            generalLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            generalLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            AddLabel(generalLayout, "Audio folder", 0);
            _audioDestination = new TextBox { ReadOnly = true, Dock = DockStyle.Fill, TabStop = false };
            Button browseAudio = new Button { Text = "Choose folder…", AutoSize = true, Tag = "audio" };
            browseAudio.Click += BrowseDestination;
            generalLayout.Controls.Add(_audioDestination, 1, 0);
            generalLayout.Controls.Add(browseAudio, 2, 0);
            AddLabel(generalLayout, "Video folder", 1);
            _videoDestination = new TextBox { ReadOnly = true, Dock = DockStyle.Fill, TabStop = false };
            Button browseVideo = new Button { Text = "Choose folder…", AutoSize = true, Tag = "video" };
            browseVideo.Click += BrowseDestination;
            generalLayout.Controls.Add(_videoDestination, 1, 1);
            generalLayout.Controls.Add(browseVideo, 2, 1);
            AddLabel(generalLayout, "Filenames", 2);
            _appendYouTubeUrlToFilename = new CheckBox { Text = "Append YouTube URL to audio and video filenames", AutoSize = true, Anchor = AnchorStyles.Left };
            generalLayout.Controls.Add(_appendYouTubeUrlToFilename, 1, 2);
            generalLayout.SetColumnSpan(_appendYouTubeUrlToFilename, 2);
            Label urlFilenameHelp = new Label { Text = "Uses a Windows-safe URL form and shortens the title slightly.", AutoSize = true, Anchor = AnchorStyles.Left };
            generalLayout.Controls.Add(urlFilenameHelp, 1, 3); generalLayout.SetColumnSpan(urlFilenameHelp, 2);
            general.Controls.Add(generalLayout);

            TableLayoutPanel audioLayout = PageLayout();
            audioLayout.RowCount = 10;
            AddLabel(audioLayout, "Output format", 0);
            _format = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 300 };
            foreach (AudioFormatOption option in AudioFormats.All) _format.Items.Add(option);
            audioLayout.Controls.Add(_format, 1, 0);
            AddLabel(audioLayout, "Encoding quality", 1);
            _qualityMode = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 170 };
            audioLayout.Controls.Add(_qualityMode, 1, 1);
            _quality = new NumericUpDown { Minimum = 0, Maximum = 10, Width = 90 };
            _qualityLabel = new Label { Text = "Quality (0 highest, 10 lowest)", AutoSize = true, Anchor = AnchorStyles.Left };
            audioLayout.Controls.Add(_qualityLabel, 0, 2); audioLayout.Controls.Add(_quality, 1, 2);
            _bitrate = new NumericUpDown { Minimum = 32, Maximum = 512, Increment = 16, Width = 90 };
            _bitrateLabel = new Label { Text = "Bitrate (kbps)", AutoSize = true, Anchor = AnchorStyles.Left };
            audioLayout.Controls.Add(_bitrateLabel, 0, 3); audioLayout.Controls.Add(_bitrate, 1, 3);
            _qualityHelp = new Label { AutoSize = true, MaximumSize = new Size(600, 0), Text = "These settings control output encoding. Quality is limited by the source audio." };
            audioLayout.Controls.Add(_qualityHelp, 0, 4); audioLayout.SetColumnSpan(_qualityHelp, 2);
            AddLabel(audioLayout, "Maximum file size", 5);
            _audioSizeLimitEnabled = new CheckBox { Text = "Limit to", AutoSize = true, Anchor = AnchorStyles.Left };
            _audioSizeLimit = CreateSizeLimitControl();
            audioLayout.Controls.Add(CreateSizeLimitRow(_audioSizeLimitEnabled, _audioSizeLimit), 1, 5);
            Label audioSizeHelp = new Label { AutoSize = true, MaximumSize = new Size(600, 0),
                Text = "Files over the cap are re-encoded at a lower bitrate. Best available and lossless formats become AAC in M4A when re-encoding is needed. Sizes use decimal MB." };
            audioLayout.Controls.Add(audioSizeHelp, 0, 6); audioLayout.SetColumnSpan(audioSizeHelp, 2);
            _audioSizeLimitEnabled.CheckedChanged += delegate { _audioSizeLimit.Enabled = _audioSizeLimitEnabled.Checked; };
            AddLabel(audioLayout, "ReplayGain", 7);
            _replayGain = new CheckBox { Text = "Add ReplayGain tags", AutoSize = true };
            audioLayout.Controls.Add(_replayGain, 1, 7);
            Label replayGainHelp = new Label { AutoSize = true, MaximumSize = new Size(600, 0),
                Text = "Scans supported audio after encoding and adds per-track loudness tags without changing the audio." };
            audioLayout.Controls.Add(replayGainHelp, 0, 8); audioLayout.SetColumnSpan(replayGainHelp, 2);
            _replayGainStatus = new Label { AutoSize = true, Text = "" };
            audioLayout.Controls.Add(_replayGainStatus, 1, 9);
            _replayGain.CheckedChanged += delegate { UpdateReplayGainStatus(); };
            _format.SelectedIndexChanged += delegate { UpdateReplayGainStatus(); };
            _format.SelectedIndexChanged += delegate { RefreshQualityOptions(); UpdateAudioControls(); UpdatePreview(); };
            _qualityMode.SelectedIndexChanged += delegate { UpdateAudioControls(); };
            audio.Controls.Add(audioLayout);

            TableLayoutPanel videoLayout = PageLayout();
            videoLayout.RowCount = 3;
            AddLabel(videoLayout, "Maximum resolution", 0);
            _videoQuality = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220 };
            _videoQuality.Items.Add(new VideoQualityOption(0, "Best available"));
            _videoQuality.Items.Add(new VideoQualityOption(2160, "2160p (4K)"));
            _videoQuality.Items.Add(new VideoQualityOption(1440, "1440p"));
            _videoQuality.Items.Add(new VideoQualityOption(1080, "1080p"));
            _videoQuality.Items.Add(new VideoQualityOption(720, "720p"));
            _videoQuality.Items.Add(new VideoQualityOption(480, "480p"));
            _videoQuality.Items.Add(new VideoQualityOption(360, "360p"));
            videoLayout.Controls.Add(_videoQuality, 1, 0);
            AddLabel(videoLayout, "Maximum file size", 1);
            _videoSizeLimitEnabled = new CheckBox { Text = "Limit to", AutoSize = true, Anchor = AnchorStyles.Left };
            _videoSizeLimit = CreateSizeLimitControl();
            videoLayout.Controls.Add(CreateSizeLimitRow(_videoSizeLimitEnabled, _videoSizeLimit), 1, 1);
            Label videoHelp = new Label { AutoSize = true, MaximumSize = new Size(600, 0),
                Text = "The resolution is a ceiling. Files over the size cap are re-encoded as MP4 at lower bitrates; very small caps can make quality poor or impossible. Sizes use decimal MB." };
            videoLayout.Controls.Add(videoHelp, 0, 2); videoLayout.SetColumnSpan(videoHelp, 2);
            _videoSizeLimitEnabled.CheckedChanged += delegate { _videoSizeLimit.Enabled = _videoSizeLimitEnabled.Checked; };
            video.Controls.Add(videoLayout);

            TableLayoutPanel metadataLayout = PageLayout();
            metadataLayout.ColumnCount = 3;
            metadataLayout.ColumnStyles.Clear();
            metadataLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 44));
            metadataLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));
            metadataLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 14));
            metadataLayout.Controls.Add(new Label { Text = "Write", AutoSize = true, Font = new Font(Font, FontStyle.Bold) }, 0, 0);
            metadataLayout.Controls.Add(new Label { Text = "Example tags (sample data)", AutoSize = true, Font = new Font(Font, FontStyle.Bold) }, 1, 0);
            metadataLayout.Controls.Add(new Label { Text = "Format", AutoSize = true, Font = new Font(Font, FontStyle.Bold) }, 2, 0);
            for (int i = 0; i < _fields.Length; i++)
            {
                int row = i + 1;
                CheckBox check = new CheckBox { Text = _labels[i], AutoSize = true, Tag = _fields[i] };
                check.CheckedChanged += delegate { UpdatePreview(); };
                _metadata.Add(_fields[i], check);
                Label value = new Label { AutoSize = true, Anchor = AnchorStyles.Left };
                Label available = new Label { AutoSize = true, Anchor = AnchorStyles.Left };
                _previewValues.Add(_fields[i], value); _availability.Add(_fields[i], available);
                metadataLayout.Controls.Add(check, 0, row);
                metadataLayout.Controls.Add(value, 1, row);
                metadataLayout.Controls.Add(available, 2, row);
            }
            metadataLayout.AutoScroll = true;
            metadata.Controls.Add(metadataLayout);

            FlowLayoutPanel appearanceLayout = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, Padding = new Padding(16), WrapContents = false };
            appearanceLayout.Controls.Add(new Label { Text = "Theme", AutoSize = true });
            _theme = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220 };
            _theme.Items.Add("Light");
            _theme.Items.Add("Dark");
            appearanceLayout.Controls.Add(_theme);
            appearance.Controls.Add(appearanceLayout);

            FlowLayoutPanel buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 42, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(6) };
            Button cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Width = 90 };
            Button save = new Button { Text = "Save", Width = 90 };
            save.Click += SaveSettings;
            buttons.Controls.Add(cancel); buttons.Controls.Add(save);
            Controls.Add(buttons);
            AcceptButton = save; CancelButton = cancel;
        }

        private static TableLayoutPanel PageLayout()
        {
            TableLayoutPanel layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(14), ColumnCount = 2, AutoScroll = true };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 205));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            return layout;
        }

        private static void AddLabel(TableLayoutPanel layout, string text, int row)
        {
            layout.Controls.Add(new Label { Text = text, AutoSize = true, Anchor = AnchorStyles.Left }, 0, row);
        }

        private static NumericUpDown CreateSizeLimitControl()
        {
            return new NumericUpDown { Minimum = 1, Maximum = 16384, Value = 8, Width = 110,
                DecimalPlaces = 0, ThousandsSeparator = true, Enabled = false, Anchor = AnchorStyles.Left };
        }

        private static FlowLayoutPanel CreateSizeLimitRow(CheckBox enabled, NumericUpDown size)
        {
            FlowLayoutPanel row = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false,
                FlowDirection = FlowDirection.LeftToRight, AutoSize = false, Height = 28, Margin = new Padding(0) };
            Label unit = new Label { Text = "MB", AutoSize = true, Anchor = AnchorStyles.Left,
                Margin = new Padding(6, 6, 0, 0) };
            row.Controls.Add(enabled);
            row.Controls.Add(size);
            row.Controls.Add(unit);
            return row;
        }

        private void LoadSettings(AppSettings settings)
        {
            SetDestination(_audioDestination, settings.Destination);
            SetDestination(_videoDestination, settings.VideoDestination);
            _appendYouTubeUrlToFilename.Checked = settings.AppendYouTubeUrlToFilename;
            _theme.SelectedIndex = settings.Theme == "dark" ? 1 : 0;
            SetVideoQuality(settings.VideoMaxHeight);
            _audioSizeLimitEnabled.Checked = settings.AudioMaxSizeMb > 0;
            _audioSizeLimit.Value = settings.AudioMaxSizeMb > 0 ? settings.AudioMaxSizeMb : 8;
            _audioSizeLimit.Enabled = _audioSizeLimitEnabled.Checked;
            _videoSizeLimitEnabled.Checked = settings.VideoMaxSizeMb > 0;
            _videoSizeLimit.Value = settings.VideoMaxSizeMb > 0 ? settings.VideoMaxSizeMb : 8;
            _videoSizeLimit.Enabled = _videoSizeLimitEnabled.Checked;
            AudioFormatOption option = AudioFormats.Find(settings.AudioFormat);
            _format.SelectedItem = option;
            RefreshQualityOptions();
            SetQualityMode(settings.QualityMode);
            _quality.Value = settings.VariableQuality;
            _bitrate.Value = settings.BitrateKbps;
            _metadata["title"].Checked = settings.TagTitle; _metadata["artist"].Checked = settings.TagArtist;
            _metadata["album"].Checked = settings.TagAlbum; _metadata["album_artist"].Checked = settings.TagAlbumArtist;
            _metadata["date"].Checked = settings.TagDate; _metadata["track"].Checked = settings.TagTrack;
            _metadata["disc"].Checked = settings.TagDisc; _metadata["genre"].Checked = settings.TagGenre;
            _metadata["composer"].Checked = settings.TagComposer; _metadata["purl"].Checked = settings.TagSourceUrl;
            _metadata["description"].Checked = settings.TagDescription;
            _replayGain.Checked = settings.ReplayGain;
            UpdateAudioControls(); UpdatePreview(); UpdateReplayGainStatus();
        }

        private static void SetDestination(TextBox box, string path)
        {
            box.Tag = String.IsNullOrWhiteSpace(path) ? null : path;
            box.Text = String.IsNullOrWhiteSpace(path) ? "(Choose an existing folder)" : path;
        }

        private void BrowseDestination(object sender, EventArgs e)
        {
            bool video = (string)((Control)sender).Tag == "video";
            TextBox destination = video ? _videoDestination : _audioDestination;
            using (FolderBrowserDialog dialog = new FolderBrowserDialog())
            {
                dialog.Description = video ? "Choose an existing folder for videos." : "Choose an existing folder for downloaded audio.";
                dialog.ShowNewFolderButton = false;
                string selected = destination.Tag as string;
                Environment.SpecialFolder fallback = video ? Environment.SpecialFolder.MyVideos : Environment.SpecialFolder.MyMusic;
                if (String.IsNullOrEmpty(selected) || !Directory.Exists(selected)) selected = Environment.GetFolderPath(fallback);
                if (!String.IsNullOrEmpty(selected) && Directory.Exists(selected)) dialog.SelectedPath = selected;
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    SetDestination(destination, Path.GetFullPath(dialog.SelectedPath));
                }
            }
        }

        private void UpdateAudioControls()
        {
            AudioFormatOption option = _format.SelectedItem as AudioFormatOption;
            bool supportsQuality = option != null && option.HasQuality;
            bool supportsBitrate = option != null && option.HasBitrate;
            bool enabled = option != null && option.Id != "best";
            _qualityMode.Enabled = enabled && (supportsQuality || supportsBitrate);
            bool variable = _qualityMode.SelectedItem as string == "Variable quality" && supportsQuality;
            bool bitrate = _qualityMode.SelectedItem as string == "Target bitrate" && supportsBitrate;
            _quality.Enabled = variable; _qualityLabel.Enabled = variable;
            _bitrate.Enabled = bitrate; _bitrateLabel.Enabled = bitrate;
            _qualityHelp.Text = !enabled
                ? "Best available preserves the source audio where possible. These settings control output encoding; quality is limited by the source audio."
                : "These settings control output encoding. Quality is limited by the source audio.";
        }

        private void RefreshQualityOptions()
        {
            AudioFormatOption option = _format.SelectedItem as AudioFormatOption;
            string selected = CurrentQualityMode();
            _qualityMode.Items.Clear();
            _qualityMode.Items.Add("Default");
            if (option != null && option.HasQuality) _qualityMode.Items.Add("Variable quality");
            if (option != null && option.HasBitrate) _qualityMode.Items.Add("Target bitrate");
            SetQualityMode(selected);
        }

        private string CurrentQualityMode()
        {
            string selected = _qualityMode.SelectedItem as string;
            if (selected == "Variable quality") return "variable";
            if (selected == "Target bitrate") return "bitrate";
            return "default";
        }

        private void SetQualityMode(string mode)
        {
            string wanted = mode == "variable" ? "Variable quality" : mode == "bitrate" ? "Target bitrate" : "Default";
            int index = _qualityMode.Items.IndexOf(wanted);
            _qualityMode.SelectedIndex = index < 0 ? 0 : index;
        }

        private void SetVideoQuality(int maximumHeight)
        {
            for (int i = 0; i < _videoQuality.Items.Count; i++)
            {
                VideoQualityOption option = _videoQuality.Items[i] as VideoQualityOption;
                if (option != null && option.Height == maximumHeight)
                {
                    _videoQuality.SelectedIndex = i;
                    return;
                }
            }
            _videoQuality.SelectedIndex = 0;
        }

        private void UpdatePreview()
        {
            AudioFormatOption option = _format.SelectedItem as AudioFormatOption;
            foreach (string field in _fields)
            {
                bool supported = option == null || option.Id == "best" || AudioFormats.SupportsTag(option.Id, field);
                _metadata[field].Enabled = supported;
                _availability[field].Text = supported ? "" : "Unavailable";
                int index = Array.IndexOf(_fields, field);
                string value = _sample[index];
                _previewValues[field].Text = supported && _metadata[field].Checked && !String.IsNullOrEmpty(value) ? value : "";
            }
        }

        private void UpdateReplayGainStatus()
        {
            AudioFormatOption option = _format.SelectedItem as AudioFormatOption;
            bool supported = option != null && (option.Id == "best" || AudioFormats.SupportsReplayGain(option.Id));
            _replayGainStatus.Text = !_replayGain.Checked ? "Not calculated." :
                supported ? "Calculated after download for supported files." : "Unavailable for this format.";
        }

        private void SaveSettings(object sender, EventArgs e)
        {
            string audioDestination = _audioDestination.Tag as string;
            string videoDestination = _videoDestination.Tag as string;
            string problem;
            if (!ValidateChangedDestination(_initial.Destination, audioDestination, "Audio", out problem) ||
                !ValidateChangedDestination(_initial.VideoDestination, videoDestination, "Video", out problem))
            {
                MessageBox.Show(this, problem, "Destination unavailable", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            AudioFormatOption format = _format.SelectedItem as AudioFormatOption;
            if (format == null) format = AudioFormats.Find("best");
            AppSettings value = _initial.Copy();
            value.Destination = audioDestination; value.VideoDestination = videoDestination;
            value.AppendYouTubeUrlToFilename = _appendYouTubeUrlToFilename.Checked;
            value.Theme = _theme.SelectedIndex == 1 ? "dark" : "light"; value.AudioFormat = format.Id;
            VideoQualityOption videoQuality = _videoQuality.SelectedItem as VideoQualityOption;
            value.VideoMaxHeight = videoQuality == null ? 0 : videoQuality.Height;
            value.AudioMaxSizeMb = _audioSizeLimitEnabled.Checked ? Decimal.ToInt32(_audioSizeLimit.Value) : 0;
            value.VideoMaxSizeMb = _videoSizeLimitEnabled.Checked ? Decimal.ToInt32(_videoSizeLimit.Value) : 0;
            value.QualityMode = CurrentQualityMode();
            value.VariableQuality = Decimal.ToInt32(_quality.Value); value.BitrateKbps = Decimal.ToInt32(_bitrate.Value);
            value.TagTitle = _metadata["title"].Checked; value.TagArtist = _metadata["artist"].Checked;
            value.TagAlbum = _metadata["album"].Checked; value.TagAlbumArtist = _metadata["album_artist"].Checked;
            value.TagDate = _metadata["date"].Checked; value.TagTrack = _metadata["track"].Checked;
            value.TagDisc = _metadata["disc"].Checked; value.TagGenre = _metadata["genre"].Checked;
            value.TagComposer = _metadata["composer"].Checked; value.TagSourceUrl = _metadata["purl"].Checked;
            value.TagDescription = _metadata["description"].Checked; value.ReplayGain = _replayGain.Checked;
            value.Normalize();
            try { SettingsStore.SaveToPath(value, _settingsPath); }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not save settings. " + ex.Message, "Settings not saved", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            _result = value;
            DialogResult = DialogResult.OK;
            Close();
        }

        private bool ValidateChangedDestination(string original, string updated, string kind, out string problem)
        {
            problem = null;
            if (String.Equals(original, updated, StringComparison.OrdinalIgnoreCase)) return true;
            if (String.IsNullOrWhiteSpace(updated)) return true;
            if (DownloaderForm.IsDestinationWritable(updated, out problem)) return true;
            problem = kind + " folder. " + problem;
            return false;
        }

        private sealed class VideoQualityOption
        {
            public readonly int Height;
            private readonly string _label;
            public VideoQualityOption(int height, string label) { Height = height; _label = label; }
            public override string ToString() { return _label; }
        }
    }
}
