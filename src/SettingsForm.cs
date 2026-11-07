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
        private AppSettings _result;
        private TextBox _destination;
        private ComboBox _format;
        private ComboBox _qualityMode;
        private NumericUpDown _quality;
        private NumericUpDown _bitrate;
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
        {
            _initial = settings.Copy();
            Text = "Settings";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(680, 540);
            BuildUi();
            LoadSettings(_initial);
        }

        public AppSettings Result { get { return _result == null ? null : _result.Copy(); } }

        private void BuildUi()
        {
            TabControl tabs = new TabControl { Dock = DockStyle.Fill };
            TabPage general = new TabPage("General");
            TabPage audio = new TabPage("Audio");
            TabPage metadata = new TabPage("Metadata");
            TabPage replay = new TabPage("ReplayGain");
            tabs.TabPages.Add(general); tabs.TabPages.Add(audio); tabs.TabPages.Add(metadata); tabs.TabPages.Add(replay);
            Controls.Add(tabs);

            TableLayoutPanel generalLayout = PageLayout();
            Label destinationLabel = new Label { Text = "Music destination", AutoSize = true, Anchor = AnchorStyles.Left };
            _destination = new TextBox { ReadOnly = true, Dock = DockStyle.Fill };
            Button browse = new Button { Text = "Choose folder…", AutoSize = true };
            browse.Click += BrowseDestination;
            generalLayout.Controls.Add(destinationLabel, 0, 0);
            generalLayout.Controls.Add(_destination, 0, 1);
            generalLayout.Controls.Add(browse, 1, 1);
            general.Controls.Add(generalLayout);

            TableLayoutPanel audioLayout = PageLayout();
            audioLayout.RowCount = 6;
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
            _format.SelectedIndexChanged += delegate { RefreshQualityOptions(); UpdateAudioControls(); UpdatePreview(); };
            _qualityMode.SelectedIndexChanged += delegate { UpdateAudioControls(); };
            audio.Controls.Add(audioLayout);

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

            FlowLayoutPanel replayLayout = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, Padding = new Padding(16), WrapContents = false };
            _replayGain = new CheckBox { Text = "Add ReplayGain tags", AutoSize = true };
            replayLayout.Controls.Add(_replayGain);
            replayLayout.Controls.Add(new Label { AutoSize = true, MaximumSize = new Size(600, 0), Text = "When enabled, the app scans each supported track after encoding and adds track-level loudness tags." });
            _replayGainStatus = new Label { AutoSize = true, Text = "" };
            replayLayout.Controls.Add(_replayGainStatus);
            _replayGain.CheckedChanged += delegate { UpdateReplayGainStatus(); };
            _format.SelectedIndexChanged += delegate { UpdateReplayGainStatus(); };
            replay.Controls.Add(replayLayout);

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

        private void LoadSettings(AppSettings settings)
        {
            _destination.Text = settings.Destination ?? "(Choose an existing folder)";
            if (String.IsNullOrEmpty(settings.Destination)) _destination.Tag = null;
            else _destination.Tag = settings.Destination;
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

        private void BrowseDestination(object sender, EventArgs e)
        {
            using (FolderBrowserDialog dialog = new FolderBrowserDialog())
            {
                dialog.Description = "Choose an existing folder for downloaded audio.";
                dialog.ShowNewFolderButton = false;
                string selected = _destination.Tag as string;
                if (String.IsNullOrEmpty(selected) || !Directory.Exists(selected)) selected = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
                if (!String.IsNullOrEmpty(selected) && Directory.Exists(selected)) dialog.SelectedPath = selected;
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    _destination.Tag = Path.GetFullPath(dialog.SelectedPath);
                    _destination.Text = (string)_destination.Tag;
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
            string destination = _destination.Tag as string;
            string problem = "Choose an existing writable destination folder.";
            if (String.IsNullOrWhiteSpace(destination) || !DownloaderForm.IsDestinationWritable(destination, out problem))
            {
                MessageBox.Show(this, String.IsNullOrWhiteSpace(destination) ? "Choose an existing writable destination folder." : problem,
                    "Destination unavailable", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            AudioFormatOption format = _format.SelectedItem as AudioFormatOption;
            if (format == null) format = AudioFormats.Find("best");
            AppSettings value = _initial.Copy();
            value.Destination = destination; value.AudioFormat = format.Id;
            value.QualityMode = CurrentQualityMode();
            value.VariableQuality = Decimal.ToInt32(_quality.Value); value.BitrateKbps = Decimal.ToInt32(_bitrate.Value);
            value.TagTitle = _metadata["title"].Checked; value.TagArtist = _metadata["artist"].Checked;
            value.TagAlbum = _metadata["album"].Checked; value.TagAlbumArtist = _metadata["album_artist"].Checked;
            value.TagDate = _metadata["date"].Checked; value.TagTrack = _metadata["track"].Checked;
            value.TagDisc = _metadata["disc"].Checked; value.TagGenre = _metadata["genre"].Checked;
            value.TagComposer = _metadata["composer"].Checked; value.TagSourceUrl = _metadata["purl"].Checked;
            value.TagDescription = _metadata["description"].Checked; value.ReplayGain = _replayGain.Checked;
            value.Normalize();
            try { SettingsStore.Save(value); }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not save settings. " + ex.Message, "Settings not saved", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            _result = value;
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
