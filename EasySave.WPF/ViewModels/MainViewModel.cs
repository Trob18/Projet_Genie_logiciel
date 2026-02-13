using EasySave.Log;
using EasySave.Log.Interfaces;
using EasySave.WPF.Config;
using EasySave.WPF.Enumerations;
using EasySave.WPF.Models;
using EasySave.WPF.State;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace EasySave.WPF.ViewModels
{
    public class MainViewModel : ViewModelBase
    {
        private readonly Language _startupLanguage;

        public ObservableCollection<BackupJob> BackupJobs { get; set; }

        private BackupJob _selectedJob;
        public BackupJob SelectedJob
        {
            get => _selectedJob;
            set
            {
                // si on change de job, on désabonne de l'ancien (sécurité)
                DetachJobHandlers(_selectedJob);

                _selectedJob = value;
                OnPropertyChanged();

                CommandManager.InvalidateRequerySuggested();
            }
        }

        // sélection multiple (apport 68-bis)
        public List<BackupJob> SelectedJobsList { get; set; } = new List<BackupJob>();

        public LanguageProxy Labels { get; } = new LanguageProxy();

        private string _jobName;
        public string JobName { get => _jobName; set { _jobName = value; OnPropertyChanged(); } }

        private string _sourcePath;
        public string SourcePath { get => _sourcePath; set { _sourcePath = value; OnPropertyChanged(); } }

        private string _targetPath;
        public string TargetPath { get => _targetPath; set { _targetPath = value; OnPropertyChanged(); } }

        private BackupType _selectedType;
        public BackupType SelectedType { get => _selectedType; set { _selectedType = value; OnPropertyChanged(); } }

        public ObservableCollection<string> EncryptedExtensionsList { get; set; }

        private string _newExtensionInput;
        public string NewExtensionInput { get => _newExtensionInput; set { _newExtensionInput = value; OnPropertyChanged(); } }

        public bool EncryptAll
        {
            get => AppSettings.Instance.EncryptAll;
            set
            {
                if (AppSettings.Instance.EncryptAll != value)
                {
                    AppSettings.Instance.EncryptAll = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(EncryptAll));
                }
            }
        }

        private int _progressValue;
        public int ProgressValue { get => _progressValue; set { _progressValue = value; OnPropertyChanged(); } }

        private string _statusMessage;
        public string StatusMessage { get => _statusMessage; set { _statusMessage = value; OnPropertyChanged(); } }

        private Visibility _restartWarningVisibility;
        public Visibility RestartWarningVisibility
        {
            get => _restartWarningVisibility;
            set { _restartWarningVisibility = value; OnPropertyChanged(); }
        }

        public ObservableCollection<string> LogFormats { get; } = new ObservableCollection<string> { "json", "xml" };

        public string SelectedLogFormat
        {
            get => AppSettings.Instance.LogFormat;
            set
            {
                if (AppSettings.Instance.LogFormat != value)
                {
                    AppSettings.Instance.LogFormat = value;
                    OnPropertyChanged();
                    UpdateLogger(false);
                }
            }
        }

        public Language SelectedLanguage
        {
            get => AppSettings.Instance.Language;
            set
            {
                if (AppSettings.Instance.Language != value)
                {
                    AppSettings.Instance.Language = value;
                    OnPropertyChanged();

                    RestartWarningVisibility = (value == _startupLanguage)
                        ? Visibility.Collapsed
                        : Visibility.Visible;
                }
            }
        }

        private readonly string _jobsFilePath;
        private ILogger _logger;

        // anti double-run
        private bool _isRunning;
        public bool IsRunning
        {
            get => _isRunning;
            private set
            {
                _isRunning = value;
                OnPropertyChanged();
                CommandManager.InvalidateRequerySuggested();
            }
        }

        // Stop réel
        private CancellationTokenSource? _cts;

        // handlers stockés (évite empilement)
        private EventHandler<BackupProgressEventArgs>? _progressHandler;
        private EventHandler<(string source, string target, long size, float time)>? _fileCopiedHandler;

        public ICommand CreateJobCommand { get; }
        public ICommand DeleteJobCommand { get; }
        public ICommand ExecuteJobCommand { get; }
        public ICommand StopJobCommand { get; }
        public ICommand AddExtensionCommand { get; }
        public ICommand RemoveExtensionCommand { get; }

        public string this[string key] => ResourceSettings.GetString(key);

        public MainViewModel()
        {
            _startupLanguage = AppSettings.Instance.Language;
            RestartWarningVisibility = Visibility.Collapsed;
            SelectedType = BackupType.Full;

            _jobsFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "jobs.json");

            UpdateLogger(true);

            BackupJobs = new ObservableCollection<BackupJob>();
            LoadJobs();

            EncryptedExtensionsList = new ObservableCollection<string>(
                AppSettings.Instance.EncryptedExtensions
                    .Split(new char[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(ext => ext.ToLower().Trim())
            );

            CreateJobCommand = new RelayCommand(_ => CreateJob(), _ => !IsRunning);
            DeleteJobCommand = new RelayCommand(_ => DeleteJob(), _ => SelectedJob != null && !IsRunning);

            // Async + multi jobs (séquentiel) + stop réel
            ExecuteJobCommand = new RelayCommand(async _ => await ExecuteJobsAsync(), _ => CanRunJobs());

            StopJobCommand = new RelayCommand(_ => RequestStop(), _ => IsRunning);

            AddExtensionCommand = new RelayCommand(_ => AddExtension(), _ => !IsRunning);
            RemoveExtensionCommand = new RelayCommand(param => RemoveExtension(param as string), param => param is string && !IsRunning);

            StatusMessage = ResourceSettings.GetString("StatusReady");
        }

        private bool CanRunJobs()
        {
            if (IsRunning) return false;
            if (SelectedJobsList != null && SelectedJobsList.Count > 0) return true;
            return SelectedJob != null;
        }

        private void UpdateLogger(bool isStartup)
        {
            _logger = LoggerCrea.CreateLogger(AppSettings.Instance.LogFormat, AppSettings.Instance.LogDirectory);

            if (!isStartup)
                StatusMessage = $"Logger : {AppSettings.Instance.LogFormat.ToUpper()} active.";
        }

        private void LoadJobs()
        {
            if (File.Exists(_jobsFilePath))
            {
                try
                {
                    string json = File.ReadAllText(_jobsFilePath);
                    var jobs = JsonSerializer.Deserialize<ObservableCollection<BackupJob>>(json);
                    if (jobs != null)
                        foreach (var job in jobs) BackupJobs.Add(job);
                }
                catch (Exception ex)
                {
                    StatusMessage = $"Erreur chargement : {ex.Message}";
                }
            }
        }

        private void SaveJobs()
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            string json = JsonSerializer.Serialize(BackupJobs, options);
            File.WriteAllText(_jobsFilePath, json);
        }

        private void AddExtension()
        {
            if (!string.IsNullOrWhiteSpace(NewExtensionInput))
            {
                string newExt = NewExtensionInput.ToLower().Trim();
                if (!newExt.StartsWith(".")) newExt = "." + newExt;

                if (!EncryptedExtensionsList.Contains(newExt))
                {
                    EncryptedExtensionsList.Add(newExt);
                    SaveEncryptedExtensions();
                    NewExtensionInput = "";
                }
            }
        }

        private void RemoveExtension(string extension)
        {
            if (!string.IsNullOrWhiteSpace(extension))
            {
                EncryptedExtensionsList.Remove(extension);
                SaveEncryptedExtensions();
            }
        }

        private void SaveEncryptedExtensions()
        {
            AppSettings.Instance.EncryptedExtensions = string.Join(", ", EncryptedExtensionsList);
        }

        private void CreateJob()
        {
            if (BackupJobs.Count >= 5)
            {
                MessageBox.Show("Max 5 jobs !", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (string.IsNullOrWhiteSpace(JobName) || string.IsNullOrWhiteSpace(SourcePath) || string.IsNullOrWhiteSpace(TargetPath))
            {
                StatusMessage = "Champs vides !";
                return;
            }

            var newJob = new BackupJob(JobName, SourcePath, TargetPath, SelectedType);
            BackupJobs.Add(newJob);
            SaveJobs();

            StatusMessage = $"{JobName} créé.";
            JobName = ""; SourcePath = ""; TargetPath = "";
        }

        private void DeleteJob()
        {
            if (SelectedJob != null)
            {
                DetachJobHandlers(SelectedJob);
                BackupJobs.Remove(SelectedJob);
                SaveJobs();
                StatusMessage = "Travail supprimé.";
            }
        }

        // Exécution séquentielle (pas parallèle) + stop réel + pas de freeze UI
        private async Task ExecuteJobsAsync()
        {
            if (IsRunning) return;

            // jobs à exécuter : multi-select sinon single
            var jobsToRun = new List<BackupJob>();
            if (SelectedJobsList != null && SelectedJobsList.Count > 0)
                jobsToRun.AddRange(SelectedJobsList);
            else if (SelectedJob != null)
                jobsToRun.Add(SelectedJob);

            if (jobsToRun.Count == 0) return;

            IsRunning = true;

            _cts?.Cancel();
            _cts?.Dispose();
            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            try
            {
                StatusMessage = $"Exécution de {jobsToRun.Count} travaux...";
                ProgressValue = 0;

                foreach (var job in jobsToRun)
                {
                    token.ThrowIfCancellationRequested();

                    // Reset visuel
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        job.Progress = 0;
                        ProgressValue = 0;
                        StatusMessage = $"Exécution : {job.Name}...";
                    });

                    // handlers propres par job
                    DetachJobHandlers(job);
                    AttachJobHandlers(job);

                    await Task.Run(() =>
                    {
                        // Stop réel ici
                        job.Execute(token);
                    }, token);

                    // fin job
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        job.Progress = 100;
                        ProgressValue = 100;
                        StatusMessage = $"{job.Name} terminé !";
                    });

                    StateSettings.UpdateState(new StateLog
                    {
                        BackupName = job.Name,
                        Timestamp = DateTime.Now,
                        State = "NON ACTIVE",
                        Progression = 100,
                        SourceFilePath = "Terminé",
                        TargetFilePath = ""
                    });

                    DetachJobHandlers(job);

                    // petit délai UX (optionnel)
                    await Task.Delay(300, token);

                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        job.Progress = 0; // si tu veux reset après
                        ProgressValue = 0;
                    });
                }

                StatusMessage = "Tous les travaux sont terminés !";
            }
            catch (OperationCanceledException)
            {
                StatusMessage = "Arrêt demandé.";
            }
            finally
            {
                // sécurité
                foreach (var j in BackupJobs)
                    DetachJobHandlers(j);

                IsRunning = false;

                _cts?.Dispose();
                _cts = null;
            }
        }

        private void RequestStop()
        {
            _cts?.Cancel();
        }

        private void AttachJobHandlers(BackupJob job)
        {
            if (job == null) return;

            _progressHandler = (sender, args) =>
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    ProgressValue = args.Percentage;
                    StatusMessage = $"{job.Name}: {args.Percentage}%";

                    job.Progress = args.Percentage;

                    StateSettings.UpdateState(new StateLog
                    {
                        BackupName = job.Name,
                        Timestamp = DateTime.Now,
                        State = "ACTIVE",
                        TotalFilesToCopy = args.TotalFiles,
                        TotalFilesSize = args.TotalSize,
                        Progression = args.Percentage,
                        NbFilesLeftToDo = args.TotalFiles - args.FilesProcessed,
                        NbFilesSizeLeftToDo = args.TotalSize - args.SizeProcessed,
                        SourceFilePath = args.CurrentSourcePath,
                        TargetFilePath = args.CurrentTargetPath
                    });
                });
            };

            _fileCopiedHandler = (sender, data) =>
            {
                var logEntry = new Log.Models.LogEntry
                {
                    Name = job.Name,
                    SourceFile = data.source,
                    TargetFile = data.target,
                    FileSize = data.size,
                    TransferTime = data.time,
                };
                _logger.WriteLog(logEntry);
            };

            job.OnProgressUpdate += _progressHandler;
            job.OnFileCopied += _fileCopiedHandler;
        }

        private void DetachJobHandlers(BackupJob job)
        {
            if (job == null) return;

            if (_progressHandler != null)
                job.OnProgressUpdate -= _progressHandler;

            if (_fileCopiedHandler != null)
                job.OnFileCopied -= _fileCopiedHandler;

            _progressHandler = null;
            _fileCopiedHandler = null;
        }

        public class LanguageProxy
        {
            public string this[string key] => ResourceSettings.GetString(key);
        }
    }
}