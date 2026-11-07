using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using System.Drawing;
using System.Web.Script.Serialization;

namespace BareBonesDownloader
{
    internal sealed class DownloaderForm : Form
    {
        private const int MaximumLogCharacters = 60000;
        private const string FinalPathPrefix = "BBD_FILE:";
        private static readonly HashSet<string> AudioExtensions = new HashSet<string>(
            new string[] { ".aac", ".alac", ".flac", ".m4a", ".mka", ".mp3", ".oga",
                ".ogg", ".opus", ".vorbis", ".wav", ".webm", ".wma" },
            StringComparer.OrdinalIgnoreCase);

        private readonly object _sync = new object();
        private readonly LinkedList<QueueItem> _pending = new LinkedList<QueueItem>();
        private readonly HashSet<string> _reservedVideoIds =
            new HashSet<string>(StringComparer.Ordinal);
        private readonly AutoResetEvent _wakeWorker = new AutoResetEvent(false);
        private readonly Thread _worker;

        private TextBox _destinationBox;
        private TextBox _linksBox;
        private TextBox _logBox;
        private Button _settingsButton;
        private Button _addButton;
        private Button _cancelButton;
        private Button _clearWaitingButton;
        private ListView _queueView;

        private string _destinationPath;
        private AppSettings _settings;
        private QueueItem _currentItem;
        private ProcessJob _currentJob;
        private bool _folderPaused;
        private bool _folderPromptPending;
        private bool _shutdownRequested;
        private bool _allowClose;
        private volatile bool _closeAfterWorker;

        public DownloaderForm()
        {
            _settings = AppSettings.Defaults();
            Text = "Bare Bones Audio Downloader";
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(720, 520);
            Size = new Size(800, 590);
            BuildInterface();

            _worker = new Thread(new ThreadStart(WorkerLoop));
            _worker.IsBackground = true;
            _worker.Name = "Bare Bones download queue";
            _worker.Start();

            Load += OnFormLoaded;
            FormClosing += OnFormClosing;
            Resize += delegate { ResizeQueueColumns(); };
        }

        private void BuildInterface()
        {
            TableLayoutPanel layout = new TableLayoutPanel();
            layout.Dock = DockStyle.Fill;
            layout.Padding = new Padding(8);
            layout.ColumnCount = 1;
            layout.RowCount = 8;
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 22F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 92F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38F));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 56F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 22F));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 44F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));

            Panel destinationPanel = new Panel();
            destinationPanel.Dock = DockStyle.Fill;
            Button changeDirectoryButton = new Button();
            changeDirectoryButton.Text = "Change directory…";
            changeDirectoryButton.Width = 142;
            changeDirectoryButton.Dock = DockStyle.Right;
            changeDirectoryButton.Click += OnChangeDirectory;
            _destinationBox = new TextBox();
            _destinationBox.Dock = DockStyle.Fill;
            _destinationBox.ReadOnly = true;
            _destinationBox.TabStop = false;
            destinationPanel.Controls.Add(changeDirectoryButton);
            destinationPanel.Controls.Add(_destinationBox);
            layout.Controls.Add(destinationPanel, 0, 0);

            Label linksLabel = new Label();
            linksLabel.Text = "YouTube links - one per line (whitespace also separates links)";
            linksLabel.Dock = DockStyle.Fill;
            linksLabel.TextAlign = ContentAlignment.MiddleLeft;
            layout.Controls.Add(linksLabel, 0, 1);

            _linksBox = new TextBox();
            _linksBox.Dock = DockStyle.Fill;
            _linksBox.Multiline = true;
            _linksBox.AcceptsReturn = true;
            _linksBox.AcceptsTab = false;
            _linksBox.ScrollBars = ScrollBars.Vertical;
            _linksBox.WordWrap = false;
            layout.Controls.Add(_linksBox, 0, 2);

            FlowLayoutPanel buttons = new FlowLayoutPanel();
            buttons.Dock = DockStyle.Fill;
            buttons.FlowDirection = FlowDirection.LeftToRight;
            buttons.WrapContents = false;
            buttons.Padding = new Padding(0, 2, 0, 0);
            _addButton = new Button();
            _addButton.Text = "Add to queue";
            _addButton.Width = 112;
            _addButton.Click += OnAddToQueue;
            _cancelButton = new Button();
            _cancelButton.Text = "Cancel current";
            _cancelButton.Width = 118;
            _cancelButton.Click += OnCancelCurrent;
            _clearWaitingButton = new Button();
            _clearWaitingButton.Text = "Clear waiting";
            _clearWaitingButton.Width = 112;
            _clearWaitingButton.Click += OnClearWaiting;
            buttons.Controls.Add(_addButton);
            buttons.Controls.Add(_cancelButton);
            buttons.Controls.Add(_clearWaitingButton);
            layout.Controls.Add(buttons, 0, 3);

            _queueView = new ListView();
            _queueView.Dock = DockStyle.Fill;
            _queueView.View = View.Details;
            _queueView.FullRowSelect = true;
            _queueView.HideSelection = false;
            _queueView.MultiSelect = false;
            _queueView.Columns.Add("YouTube video", 570);
            _queueView.Columns.Add("Status", 110);
            layout.Controls.Add(_queueView, 0, 4);

            Label logLabel = new Label();
            logLabel.Text = "Log";
            logLabel.Dock = DockStyle.Fill;
            logLabel.TextAlign = ContentAlignment.MiddleLeft;
            layout.Controls.Add(logLabel, 0, 5);

            _logBox = new TextBox();
            _logBox.Dock = DockStyle.Fill;
            _logBox.Multiline = true;
            _logBox.ReadOnly = true;
            _logBox.ScrollBars = ScrollBars.Vertical;
            _logBox.WordWrap = true;
            _logBox.MaxLength = MaximumLogCharacters;
            layout.Controls.Add(_logBox, 0, 6);

            Panel settingsPanel = new Panel();
            settingsPanel.Dock = DockStyle.Fill;
            _settingsButton = new Button();
            _settingsButton.Text = "Settings…";
            _settingsButton.Width = 100;
            _settingsButton.Dock = DockStyle.Right;
            _settingsButton.Click += OnSettings;
            settingsPanel.Controls.Add(_settingsButton);
            layout.Controls.Add(settingsPanel, 0, 7);

            Controls.Add(layout);
        }

        private void OnFormLoaded(object sender, EventArgs e)
        {
            string problem;
            _settings = SettingsStore.Load(out problem);
            string loadedDestination = _settings.Destination;
            lock (_sync)
                _destinationPath = loadedDestination;
            RefreshDestinationBox();

            if (!String.IsNullOrEmpty(problem))
                AppendLog(problem);

            string missingTools;
            if (!TryGetMissingTools(out missingTools))
            {
                AppendLog("Downloader tools are missing: " + missingTools);
                MessageBox.Show(this,
                    "Some required tools are missing. Close the app, run setup.cmd beside it, then reopen it.\r\n\r\n" +
                    missingTools,
                    "Setup required", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }

            if (String.IsNullOrEmpty(loadedDestination))
            {
                AppendLog("Choose an existing destination folder in Settings before adding downloads.");
                ShowSettings();
            }
            else if (!Directory.Exists(loadedDestination))
            {
                PromptForDestination("The saved destination folder is no longer available.");
            }

            UpdateButtonStates();
        }

        private void OnSettings(object sender, EventArgs e)
        {
            ShowSettings();
        }

        private void OnChangeDirectory(object sender, EventArgs e)
        {
            string current;
            lock (_sync) current = _destinationPath;

            using (FolderBrowserDialog dialog = new FolderBrowserDialog())
            {
                dialog.Description = "Choose an existing folder for downloaded audio.";
                dialog.ShowNewFolderButton = false;
                if (String.IsNullOrEmpty(current) || !Directory.Exists(current))
                    current = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
                if (!String.IsNullOrEmpty(current) && Directory.Exists(current))
                    dialog.SelectedPath = current;
                if (dialog.ShowDialog(this) != DialogResult.OK)
                    return;

                string selected = Path.GetFullPath(dialog.SelectedPath);
                string problem;
                if (!IsDestinationWritable(selected, out problem))
                {
                    MessageBox.Show(this, problem, "Destination unavailable",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                AppSettings updated;
                lock (_sync)
                {
                    updated = _settings.Copy();
                    updated.Destination = selected;
                }
                try
                {
                    SettingsStore.Save(updated);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "Could not save the destination setting. " + ex.Message,
                        "Settings could not be saved", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                lock (_sync)
                {
                    _settings = updated;
                    _destinationPath = selected;
                    if (_folderPaused) _folderPaused = false;
                }
                RefreshDestinationBox();
                AppendLog("Destination changed.");
                _wakeWorker.Set();
                UpdateButtonStates();
            }
        }

        private bool ShowSettings()
        {
            AppSettings current;
            lock (_sync) current = _settings == null ? AppSettings.Defaults() : _settings.Copy();
            using (SettingsForm dialog = new SettingsForm(current))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return false;
                AppSettings updated = dialog.Result;
                lock (_sync)
                {
                    _settings = updated.Copy();
                    _destinationPath = updated.Destination;
                    if (_folderPaused)
                        _folderPaused = false;
                }
                RefreshDestinationBox();
                AppendLog("Settings saved.");
                _wakeWorker.Set();
                UpdateButtonStates();
                return true;
            }
        }

        private void PromptForDestination(string reason)
        {
            MessageBox.Show(this, reason + "\r\nChoose a different existing folder.",
                "Choose destination", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            ShowSettings();
        }

        private void OnAddToQueue(object sender, EventArgs e)
        {
            string destination;
            lock (_sync)
                destination = _destinationPath;

            if (String.IsNullOrEmpty(destination))
            {
                if (!ShowSettings())
                    return;
            }

            string missingTools;
            if (!TryGetMissingTools(out missingTools))
            {
                AppendLog("Cannot add downloads because setup is incomplete: " + missingTools);
                MessageBox.Show(this,
                    "Run setup.cmd beside the app, then reopen it.\r\n\r\n" + missingTools,
                    "Setup required", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string input = _linksBox.Text;
            if (String.IsNullOrWhiteSpace(input))
                return;

            string[] tokens = Regex.Split(input.Trim(), @"\s+");
            List<string> rejected = new List<string>();
            int added = 0;
            int duplicates = 0;
            int i;

            for (i = 0; i < tokens.Length; i++)
            {
                if (String.IsNullOrWhiteSpace(tokens[i]))
                    continue;

                YoutubeVideo video;
                string error;
                if (!YoutubeUrlParser.TryParse(tokens[i], out video, out error))
                {
                    QueueItem rejectedItem = new QueueItem();
                    rejectedItem.DisplayUrl = tokens[i];
                    rejectedItem.Status = "Failed";
                    rejectedItem.Row = new ListViewItem(new string[] { tokens[i], "Failed" });
                    _queueView.Items.Add(rejectedItem.Row);
                    rejected.Add(tokens[i]);
                    AppendLog("Rejected link: " + tokens[i] + " - " + error);
                    continue;
                }

                QueueItem item = new QueueItem();
                item.VideoId = video.Id;
                item.CanonicalUrl = video.CanonicalUrl;
                item.DisplayUrl = video.CanonicalUrl;
                item.Status = "Waiting";
                lock (_sync)
                {
                    item.Settings = _settings.Copy();
                    if (String.IsNullOrWhiteSpace(item.Settings.Destination)) item.Settings.Destination = _destinationPath;
                }
                item.Row = new ListViewItem(new string[] { item.DisplayUrl, item.Status });

                bool duplicate;
                lock (_sync)
                {
                    duplicate = _reservedVideoIds.Contains(video.Id);
                    if (!duplicate && !_shutdownRequested)
                    {
                        _reservedVideoIds.Add(video.Id);
                        _pending.AddLast(item);
                    }
                    else if (_shutdownRequested)
                    {
                        duplicate = true;
                    }
                }

                if (duplicate)
                {
                    duplicates++;
                    AppendLog("Ignored duplicate video ID " + video.Id +
                        "; it is already waiting or downloading.");
                    continue;
                }

                _queueView.Items.Add(item.Row);
                added++;
            }

            _linksBox.Text = String.Join(Environment.NewLine, rejected.ToArray());
            if (added > 0)
            {
                AppendLog("Added " + added.ToString() + " video(s) to the queue.");
                _wakeWorker.Set();
            }
            else if (duplicates > 0)
            {
                AppendLog("No new videos were added.");
            }

            ResizeQueueColumns();
            UpdateButtonStates();
        }

        private void OnCancelCurrent(object sender, EventArgs e)
        {
            QueueItem item;
            ProcessJob job;
            lock (_sync)
            {
                item = _currentItem;
                if (item == null)
                    return;
                item.CancelRequested = true;
                job = _currentJob;
            }

            AppendLog("Cancellation requested for " + item.DisplayUrl);
            if (job != null)
            {
                try
                {
                    job.Terminate();
                }
                catch (Exception ex)
                {
                    AppendLog("Could not stop the downloader immediately: " + ex.Message);
                }
            }
        }

        private void OnClearWaiting(object sender, EventArgs e)
        {
            List<QueueItem> removed = new List<QueueItem>();
            lock (_sync)
            {
                LinkedListNode<QueueItem> node = _pending.First;
                while (node != null)
                {
                    LinkedListNode<QueueItem> next = node.Next;
                    removed.Add(node.Value);
                    _reservedVideoIds.Remove(node.Value.VideoId);
                    _pending.Remove(node);
                    node = next;
                }
            }

            int i;
            for (i = 0; i < removed.Count; i++)
                SetItemStatus(removed[i], "Cancelled");

            if (removed.Count > 0)
                AppendLog("Cleared " + removed.Count.ToString() + " waiting download(s).");
            UpdateButtonStates();
        }

        private void WorkerLoop()
        {
            try
            {
                while (true)
                {
                    QueueItem item = null;
                    bool wait = false;
                    lock (_sync)
                    {
                        if (_shutdownRequested)
                            break;
                        if (_folderPaused || _pending.Count == 0)
                            wait = true;
                        else
                        {
                            item = _pending.First.Value;
                            _pending.RemoveFirst();
                            _currentItem = item;
                        }
                    }

                    if (wait)
                    {
                        _wakeWorker.WaitOne();
                        continue;
                    }

                    if (item == null)
                        continue;

                    PostToUi(UpdateButtonStates);
                    if (ShouldStop(item))
                    {
                        FinishItem(item, "Cancelled");
                        continue;
                    }

                    string destination = item.Settings == null ? null : item.Settings.Destination;

                    string folderProblem;
                    if (!IsDestinationWritable(destination, out folderProblem))
                    {
                        bool shuttingDown;
                        lock (_sync)
                        {
                            shuttingDown = _shutdownRequested;
                            _currentItem = null;
                            if (shuttingDown)
                                _reservedVideoIds.Remove(item.VideoId);
                            else
                            {
                                _pending.AddFirst(item);
                                _folderPaused = true;
                            }
                        }

                        if (shuttingDown)
                            SetItemStatus(item, "Cancelled");
                        else
                        {
                            AppendLog(folderProblem);
                            RequestFolderPrompt(folderProblem);
                        }
                        PostToUi(UpdateButtonStates);
                        continue;
                    }

                    SetItemStatus(item, "Downloading");
                    AppendLog("Starting " + item.DisplayUrl);

                    ProcessOutcome outcome = RunDownload(item, destination);
                    bool cancelled;
                    lock (_sync)
                        cancelled = item.CancelRequested || _shutdownRequested;

                    if (cancelled || outcome.Cancelled)
                        FinishItem(item, "Cancelled");
                    else
                    {
                        if (outcome.ExistingFileSkipped)
                            AppendLog("An audio file with this name is already in the destination; it was left unchanged.");
                        if (outcome.Success)
                        {
                            if (!String.IsNullOrEmpty(outcome.ReplayGainErrorText))
                                AppendLog("ReplayGain tagging could not be completed; the audio file was still saved. " +
                                    outcome.ReplayGainErrorText);
                            AppendLog("Finished: " + outcome.FinalPath);
                            FinishItem(item, "Finished");
                        }
                        else
                        {
                            if (!String.IsNullOrEmpty(outcome.ErrorText))
                                AppendLog("Failed: " + outcome.ErrorText);
                            FinishItem(item, "Failed");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                AppendLog("The queue worker stopped after an unexpected error: " + ex.Message);
            }
            finally
            {
                if (_closeAfterWorker)
                    PostToUi(delegate
                    {
                        _allowClose = true;
                        Close();
                    });
                else
                    PostToUi(UpdateButtonStates);
            }
        }

        private void FinishItem(QueueItem item, string status)
        {
            lock (_sync)
            {
                if (_currentItem == item)
                    _currentItem = null;
                _reservedVideoIds.Remove(item.VideoId);
            }
            SetItemStatus(item, status);
            PostToUi(UpdateButtonStates);
        }

        private ProcessOutcome RunDownload(QueueItem item, string destination)
        {
            ProcessOutcome outcome = new ProcessOutcome();
            string appDirectory = AppDomain.CurrentDomain.BaseDirectory;
            string toolsDirectory = Path.GetFullPath(Path.Combine(appDirectory, "tools"));
            string downloader = Path.Combine(toolsDirectory, "yt-dlp.exe");
            string ffmpeg = Path.Combine(toolsDirectory, "ffmpeg.exe");
            string ffprobe = Path.Combine(toolsDirectory, "ffprobe.exe");
            string deno = Path.Combine(toolsDirectory, "deno.exe");
            string rsgain = Path.Combine(toolsDirectory, "rsgain", "rsgain.exe");

            string missing = null;
            if (!File.Exists(downloader)) missing = downloader;
            else if (!File.Exists(ffmpeg)) missing = ffmpeg;
            else if (!File.Exists(ffprobe)) missing = ffprobe;
            else if (!File.Exists(deno)) missing = deno;
            if (missing != null)
            {
                outcome.ErrorText = "A required tool is missing: " + missing + ". Close the app and run setup.cmd.";
                return outcome;
            }

            if (ShouldStop(item))
            {
                outcome.Cancelled = true;
                return outcome;
            }

            Process process = null;
            ProcessJob job = null;
            DownloaderOutputState output = new DownloaderOutputState();
            string workRoot = Path.Combine(appDirectory, "work");
            string workingDirectory = null;
            FileStream workLock = null;
            bool removeWorkingFiles = false;
            bool processStarted = false;

            try
            {
                if (!Regex.IsMatch(item.VideoId, @"\A[A-Za-z0-9_-]{11}\z"))
                    throw new InvalidOperationException("The video ID is invalid.");
                Directory.CreateDirectory(workRoot);
                workLock = new FileStream(Path.Combine(workRoot, item.VideoId + ".lock"),
                    FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                workingDirectory = Path.Combine(workRoot, item.VideoId);
                Directory.CreateDirectory(workingDirectory);

                ProcessStartInfo startInfo = new ProcessStartInfo();
                startInfo.FileName = downloader;
                startInfo.Arguments = BuildDownloaderArguments(
                    item.CanonicalUrl, workingDirectory, toolsDirectory, deno);
                startInfo.WorkingDirectory = appDirectory;
                startInfo.UseShellExecute = false;
                startInfo.CreateNoWindow = true;
                startInfo.WindowStyle = ProcessWindowStyle.Hidden;
                startInfo.RedirectStandardOutput = true;
                startInfo.RedirectStandardError = true;
                startInfo.StandardOutputEncoding = Encoding.UTF8;
                startInfo.StandardErrorEncoding = Encoding.UTF8;

                process = new Process();
                process.StartInfo = startInfo;
                process.OutputDataReceived += delegate(object sender, DataReceivedEventArgs args)
                {
                    ObserveDownloaderLine(args.Data, output);
                };
                process.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs args)
                {
                    ObserveDownloaderLine(args.Data, output);
                };

                if (!process.Start())
                {
                    outcome.ErrorText = "Windows could not start yt-dlp.";
                    return outcome;
                }
                processStarted = true;

                job = new ProcessJob();
                try
                {
                    job.AssignProcess(process);
                }
                catch
                {
                    try
                    {
                        process.Kill();
                        process.WaitForExit();
                    }
                    catch { }
                    throw;
                }

                bool stopImmediately;
                lock (_sync)
                {
                    _currentJob = job;
                    stopImmediately = item.CancelRequested || _shutdownRequested;
                }

                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                if (stopImmediately)
                    job.Terminate();

                bool terminationSent = stopImmediately;
                while (!process.WaitForExit(250))
                {
                    if (!terminationSent && ShouldStop(item))
                    {
                        job.Terminate();
                        terminationSent = true;
                    }
                }
                process.WaitForExit();

                if (!terminationSent && ShouldStop(item))
                {
                    job.Terminate();
                    terminationSent = true;
                }

                bool treeStopped = job.WaitUntilEmpty(15000);
                if (!treeStopped)
                {
                    job.Terminate();
                    treeStopped = job.WaitUntilEmpty(5000);
                }

                if (!treeStopped)
                {
                    outcome.ErrorText = "The downloader process tree did not stop cleanly.";
                    return outcome;
                }

                if (ShouldStop(item))
                {
                    outcome.Cancelled = true;
                    return outcome;
                }

                if (process.ExitCode != 0)
                {
                    outcome.ErrorText = "yt-dlp exited with code " + process.ExitCode.ToString() +
                        ". See the log for details.";
                    return outcome;
                }

                string finalPath = FindExistingAudioPath(output, workingDirectory);
                bool existingWorkingFileSkipped;
                lock (output.Sync)
                    existingWorkingFileSkipped = output.ExistingFileSkipped;

                if (finalPath == null)
                {
                    outcome.ErrorText = existingWorkingFileSkipped
                        ? "yt-dlp skipped an existing file, but the resulting audio file could not be verified."
                        : output.LiveVideoFiltered
                            ? "This video is currently live. Live stream recording is not supported."
                            : "yt-dlp completed but did not report an audio file in the working folder.";
                    return outcome;
                }

                finalPath = AudioEncoding.Apply(finalPath, toolsDirectory, item.Settings, job,
                    delegate { return ShouldStop(item); });
                MusicMetadata.ValidateAudio(finalPath, toolsDirectory, job,
                    delegate { return ShouldStop(item); });
                try
                {
                    string infoFile = MusicMetadata.FindInfoFile(workingDirectory, item.VideoId);
                    MusicMetadata.EnsureTags(finalPath, infoFile, toolsDirectory, item.Settings, job,
                        delegate { return ShouldStop(item); }, AppendLog);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    AppendLog("Metadata warning: " + ex.Message + " The audio will still be saved with its available tags.");
                }

                string destinationFile = Path.Combine(destination, Path.GetFileName(finalPath));
                if (File.Exists(destinationFile))
                {
                    outcome.Success = true;
                    outcome.ExistingFileSkipped = true;
                    outcome.FinalPath = destinationFile;
                    removeWorkingFiles = true;
                    return outcome;
                }
                bool replayGainCancelled = false;
                string replayGainError = null;
                if (item.Settings.ReplayGain && SupportsReplayGainPath(finalPath) &&
                    !RunReplayGain(item, finalPath, job, out replayGainCancelled, out replayGainError))
                {
                    if (replayGainCancelled)
                    {
                        outcome.Cancelled = true;
                        return outcome;
                    }
                    outcome.ReplayGainErrorText = replayGainError;
                }
                if (ShouldStop(item))
                {
                    outcome.Cancelled = true;
                    return outcome;
                }

                bool existingFile;
                outcome.FinalPath = DownloadFiles.PublishAudio(finalPath, destination,
                    delegate { return ShouldStop(item); }, out existingFile);
                outcome.ExistingFileSkipped = existingFile;
                outcome.Success = true;
                removeWorkingFiles = true;
                return outcome;
            }
            catch (OperationCanceledException)
            {
                outcome.Cancelled = true;
                return outcome;
            }
            catch (Exception ex)
            {
                outcome.ErrorText = processStarted
                    ? "Could not complete the download: " + ex.Message
                    : "Could not launch yt-dlp: " + ex.Message;
                return outcome;
            }
            finally
            {
                if (process != null && processStarted)
                {
                    try
                    {
                        if (!process.HasExited)
                        {
                            if (job != null)
                                job.Terminate();
                            else
                                process.Kill();
                            process.WaitForExit();
                        }
                    }
                    catch { }
                }

                lock (_sync)
                {
                    if (_currentJob == job)
                        _currentJob = null;
                }

                if (job != null)
                    job.Dispose();
                if (process != null)
                    process.Dispose();
                if (removeWorkingFiles && workingDirectory != null)
                {
                    try { DownloadFiles.CleanupWorkingDirectory(workRoot, workingDirectory); }
                    catch (Exception ex) { AppendLog("Could not remove temporary download files: " + ex.Message); }
                }
                if (workLock != null)
                    workLock.Dispose();
            }
        }

        private bool RunReplayGain(
            QueueItem item, string audioPath, ProcessJob job,
            out bool cancelled, out string error)
        {
            cancelled = false;
            error = null;
            if (ShouldStop(item))
            {
                cancelled = true;
                return false;
            }

            string rsgainPath = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory, "tools", "rsgain", "rsgain.exe");
            if (!File.Exists(rsgainPath))
            {
                error = "rsgain.exe is missing. Run setup.cmd to restore the tools.";
                return false;
            }

            Process process = null;
            bool processStarted = false;
            try
            {
                ProcessStartInfo startInfo = new ProcessStartInfo();
                startInfo.FileName = rsgainPath;
                startInfo.Arguments = "custom -s i " + QuoteWindowsArgument(audioPath);
                startInfo.WorkingDirectory = Path.GetDirectoryName(rsgainPath);
                startInfo.UseShellExecute = false;
                startInfo.CreateNoWindow = true;
                startInfo.WindowStyle = ProcessWindowStyle.Hidden;
                startInfo.RedirectStandardOutput = true;
                startInfo.RedirectStandardError = true;
                startInfo.StandardOutputEncoding = Encoding.UTF8;
                startInfo.StandardErrorEncoding = Encoding.UTF8;

                process = new Process();
                process.StartInfo = startInfo;
                process.OutputDataReceived += delegate(object sender, DataReceivedEventArgs args)
                {
                    if (args.Data != null)
                        AppendLog("[ReplayGain] " + args.Data);
                };
                process.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs args)
                {
                    if (args.Data != null)
                        AppendLog("[ReplayGain] " + args.Data);
                };

                if (!process.Start())
                {
                    error = "Windows could not start rsgain.";
                    return false;
                }
                processStarted = true;

                try
                {
                    job.AssignProcess(process);
                }
                catch
                {
                    try
                    {
                        process.Kill();
                        process.WaitForExit();
                    }
                    catch { }
                    throw;
                }

                bool stopImmediately;
                lock (_sync)
                    stopImmediately = item.CancelRequested || _shutdownRequested;

                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                if (stopImmediately)
                    job.Terminate();

                bool terminationSent = stopImmediately;
                while (!process.WaitForExit(250))
                {
                    if (!terminationSent && ShouldStop(item))
                    {
                        job.Terminate();
                        terminationSent = true;
                    }
                }
                process.WaitForExit();

                bool treeStopped = job.WaitUntilEmpty(15000);
                if (!treeStopped)
                {
                    job.Terminate();
                    treeStopped = job.WaitUntilEmpty(5000);
                }
                if (!treeStopped)
                {
                    error = "The ReplayGain process did not stop cleanly.";
                    return false;
                }

                if (ShouldStop(item))
                {
                    cancelled = true;
                    return false;
                }
                if (process.ExitCode != 0)
                {
                    error = "rsgain exited with code " + process.ExitCode.ToString() + ".";
                    return false;
                }

                AppendLog("ReplayGain track tags added.");
                return true;
            }
            catch (Exception ex)
            {
                if (ShouldStop(item))
                    cancelled = true;
                else
                    error = "Could not write ReplayGain tags: " + ex.Message;
                return false;
            }
            finally
            {
                if (process != null && processStarted)
                {
                    try
                    {
                        if (!process.HasExited)
                        {
                            job.Terminate();
                            process.WaitForExit();
                        }
                    }
                    catch { }
                }
                if (process != null)
                    process.Dispose();
            }
        }

        private static bool SupportsReplayGainPath(string path)
        {
            string extension = Path.GetExtension(path);
            return new HashSet<string>(new string[] {
                ".aiff", ".flac", ".ape", ".mp2", ".mp3", ".m4a", ".ogg", ".oga",
                ".opus", ".wav", ".wv", ".wma", ".mka", ".mkv", ".webm", ".dsf"
            }, StringComparer.OrdinalIgnoreCase).Contains(extension);
        }

        private void ObserveDownloaderLine(string line, DownloaderOutputState output)
        {
            if (line == null)
                return;

            string displayLine = line.Length > 4000 ? line.Substring(0, 4000) + " ..." : line;
            AppendLog("[yt-dlp] " + displayLine);

            lock (output.Sync)
            {
                if (line.IndexOf("already been downloaded", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    line.IndexOf("already exists", StringComparison.OrdinalIgnoreCase) >= 0)
                    output.ExistingFileSkipped = true;

                if (line.IndexOf("does not pass filter", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    line.IndexOf("match filter", StringComparison.OrdinalIgnoreCase) >= 0)
                    output.LiveVideoFiltered = true;

                // Only the dedicated --print record is a path. Diagnostics can contain
                // characters such as > or quotes that Path.IsPathRooted rejects on Framework.
                if (!line.StartsWith(FinalPathPrefix, StringComparison.Ordinal))
                    return;
                try
                {
                    string candidate = CleanPrintedPath(line.Substring(FinalPathPrefix.Length).Trim());
                    if (candidate.Length > 2 && Path.IsPathRooted(candidate) &&
                        output.CandidatePaths.Count < 20)
                        output.CandidatePaths.Add(Path.GetFullPath(candidate));
                }
                catch (ArgumentException) { }
                catch (NotSupportedException) { }
                catch (PathTooLongException) { }
            }
        }

        private static string CleanPrintedPath(string value)
        {
            // yt-dlp prints JSON with ASCII escapes so Windows code pages cannot
            // lose characters in filenames. Decode the complete JSON string once.
            if (value.Length > 0 && value[0] == '"')
                return new JavaScriptSerializer().Deserialize<string>(value);
            return value;
        }

        private static string FindExistingAudioPath(DownloaderOutputState output, string destination)
        {
            List<string> candidates;
            lock (output.Sync)
                candidates = new List<string>(output.CandidatePaths);

            string root;
            try
            {
                root = Path.GetFullPath(destination).TrimEnd(
                    Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                    Path.DirectorySeparatorChar;
            }
            catch
            {
                return null;
            }

            int i;
            for (i = candidates.Count - 1; i >= 0; i--)
            {
                try
                {
                    string fullPath = Path.GetFullPath(CleanPrintedPath(candidates[i]));
                    if (!fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (!AudioExtensions.Contains(Path.GetExtension(fullPath)))
                        continue;
                    if (File.Exists(fullPath))
                        return fullPath;
                }
                catch { }
            }
            return null;
        }

        private static string BuildDownloaderArguments(
            string canonicalUrl, string destination, string toolsDirectory, string denoPath)
        {
            string[] values = new string[]
            {
                "--ignore-config",
                "--encoding", "utf-8",
                "--no-playlist",
                "--format", "bestaudio/best",
                "--extract-audio",
                "--embed-metadata",
                "--write-info-json",
                "--no-write-playlist-metafiles",
                "--no-embed-info-json",
                "--audio-format", "best",
                "--no-keep-video",
                "--ffmpeg-location", toolsDirectory,
                "--js-runtimes", "deno:" + denoPath,
                "--match-filters", "!is_live",
                "--windows-filenames",
                "--no-force-overwrites",
                "--post-overwrites",
                "--newline",
                "--color", "never",
                "--print", "after_move:" + FinalPathPrefix + "%(filepath)j",
                "--paths", destination,
                "--output", "%(title).150B.%(ext)s",
                "--",
                canonicalUrl
            };

            StringBuilder arguments = new StringBuilder();
            int i;
            for (i = 0; i < values.Length; i++)
            {
                if (i > 0)
                    arguments.Append(' ');
                arguments.Append(QuoteWindowsArgument(values[i]));
            }
            return arguments.ToString();
        }

        private static string QuoteWindowsArgument(string value)
        {
            StringBuilder builder = new StringBuilder();
            builder.Append('"');
            int backslashes = 0;
            int i;
            for (i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (c == '\\')
                {
                    backslashes++;
                    continue;
                }

                if (c == '"')
                {
                    builder.Append('\\', backslashes * 2 + 1);
                    builder.Append('"');
                }
                else
                {
                    if (backslashes > 0)
                        builder.Append('\\', backslashes);
                    builder.Append(c);
                }
                backslashes = 0;
            }

            if (backslashes > 0)
                builder.Append('\\', backslashes * 2);
            builder.Append('"');
            return builder.ToString();
        }

        private bool ShouldStop(QueueItem item)
        {
            lock (_sync)
                return item.CancelRequested || _shutdownRequested;
        }

        internal static bool IsDestinationWritable(string destination, out string problem)
        {
            problem = null;
            if (String.IsNullOrWhiteSpace(destination) || !Directory.Exists(destination))
            {
                problem = "The destination folder is missing. The queue is paused; choose an existing folder.";
                return false;
            }

            string probe = Path.Combine(destination, ".BareBonesDownloader-write-check-" +
                Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                using (FileStream stream = new FileStream(
                    probe, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    stream.WriteByte(0);
                }
                File.Delete(probe);
                return true;
            }
            catch (Exception ex)
            {
                try
                {
                    if (File.Exists(probe))
                        File.Delete(probe);
                }
                catch { }
                problem = "The destination folder is unavailable or not writable. The queue is paused. " +
                    ex.Message;
                return false;
            }
        }

        private void RequestFolderPrompt(string reason)
        {
            lock (_sync)
            {
                if (_folderPromptPending || _shutdownRequested)
                    return;
                _folderPromptPending = true;
            }

            PostToUi(delegate
            {
                lock (_sync)
                    _folderPromptPending = false;
                if (_shutdownRequested || IsDisposed)
                    return;

                MessageBox.Show(this, reason + "\r\nChoose a valid folder to resume the queue.",
                    "Destination needed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                if (!ShowSettings())
                    AppendLog("The queue remains paused until a valid destination is chosen.");
                UpdateButtonStates();
            });
        }

        private bool TryGetMissingTools(out string missing)
        {
            string tools = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tools");
            List<string> absent = new List<string>();
            string[] names = new string[] { "yt-dlp.exe", "ffmpeg.exe", "ffprobe.exe", "deno.exe" };
            int i;
            for (i = 0; i < names.Length; i++)
            {
                if (!File.Exists(Path.Combine(tools, names[i])))
                    absent.Add(names[i]);
            }
            missing = String.Join(", ", absent.ToArray());
            return absent.Count == 0;
        }

        private void SetItemStatus(QueueItem item, string status)
        {
            item.Status = status;
            PostToUi(delegate
            {
                if (item.Row != null && item.Row.ListView == _queueView &&
                    item.Row.SubItems.Count > 1)
                    item.Row.SubItems[1].Text = status;
                UpdateButtonStates();
            });
        }

        private void RefreshDestinationBox()
        {
            if (_destinationBox == null)
                return;
            string destination;
            lock (_sync)
                destination = _destinationPath;
            _destinationBox.Text = String.IsNullOrEmpty(destination)
                ? "(Choose an existing folder)" : destination;
        }

        private void ResizeQueueColumns()
        {
            if (_queueView == null || _queueView.Columns.Count < 2)
                return;
            _queueView.Columns[1].Width = 110;
            _queueView.Columns[0].Width = Math.Max(250, _queueView.ClientSize.Width - 116);
        }

        private void UpdateButtonStates()
        {
            if (IsDisposed || _addButton == null)
                return;

            bool current;
            bool waiting;
            bool paused;
            bool shuttingDown;
            lock (_sync)
            {
                current = _currentItem != null;
                waiting = _pending.Count > 0;
                paused = _folderPaused;
                shuttingDown = _shutdownRequested;
            }

            _settingsButton.Enabled = !shuttingDown;
            _addButton.Enabled = !shuttingDown;
            _cancelButton.Enabled = !shuttingDown && current;
            _clearWaitingButton.Enabled = !shuttingDown && waiting;
        }

        private void AppendLog(string message)
        {
            if (String.IsNullOrEmpty(message))
                return;
            PostToUi(delegate { AppendLogNow(message); });
        }

        private void AppendLogNow(string message)
        {
            if (_logBox == null || _logBox.IsDisposed)
                return;

            string line = DateTime.Now.ToString("HH:mm:ss") + "  " + message + Environment.NewLine;
            if (line.Length > MaximumLogCharacters)
                line = line.Substring(line.Length - MaximumLogCharacters);

            if (_logBox.TextLength + line.Length > MaximumLogCharacters)
            {
                int removeCount = _logBox.TextLength + line.Length - MaximumLogCharacters;
                string existing = _logBox.Text;
                int newline = existing.IndexOf('\n', Math.Min(removeCount, existing.Length));
                if (newline >= 0)
                    removeCount = newline + 1;
                _logBox.Select(0, Math.Min(removeCount, _logBox.TextLength));
                _logBox.SelectedText = String.Empty;
            }

            _logBox.AppendText(line);
            _logBox.SelectionStart = _logBox.TextLength;
            _logBox.ScrollToCaret();
        }

        private void PostToUi(MethodInvoker action)
        {
            if (IsDisposed || Disposing)
                return;
            try
            {
                if (InvokeRequired)
                    BeginInvoke(action);
                else
                    action();
            }
            catch (InvalidOperationException) { }
        }

        private void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            if (_allowClose)
                return;

            bool unfinished;
            lock (_sync)
                unfinished = _currentItem != null || _pending.Count > 0;

            if (unfinished)
            {
                DialogResult answer = MessageBox.Show(this,
                    "There is unfinished work. Closing will stop the current download and lose the waiting queue.\r\n\r\nKeep the window open?",
                    "Downloads are still running", MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                    MessageBoxDefaultButton.Button1);
                if (answer == DialogResult.Yes)
                {
                    e.Cancel = true;
                    return;
                }

                e.Cancel = true;
                _closeAfterWorker = true;
                BeginShutdown();
                return;
            }

            BeginShutdown();
        }

        private void BeginShutdown()
        {
            ProcessJob job;
            lock (_sync)
            {
                _shutdownRequested = true;
                if (_currentItem != null)
                    _currentItem.CancelRequested = true;
                _pending.Clear();
                job = _currentJob;
            }

            if (job != null)
            {
                try { job.Terminate(); }
                catch (Exception ex) { AppendLog("Could not stop a process during closing: " + ex.Message); }
            }
            _wakeWorker.Set();
        }

        private sealed class QueueItem
        {
            public string VideoId;
            public string CanonicalUrl;
            public string DisplayUrl;
            public string Status;
            public ListViewItem Row;
            public bool CancelRequested;
            public AppSettings Settings;
        }

        private sealed class ProcessOutcome
        {
            public bool Success;
            public bool Cancelled;
            public bool ExistingFileSkipped;
            public string FinalPath;
            public string ErrorText;
            public string ReplayGainErrorText;
        }

        private sealed class DownloaderOutputState
        {
            public readonly object Sync = new object();
            public readonly List<string> CandidatePaths = new List<string>();
            public bool ExistingFileSkipped;
            public bool LiveVideoFiltered;
        }
    }
}
