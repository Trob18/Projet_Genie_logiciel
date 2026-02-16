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
            set { _selectedJob = value; OnPropertyChanged(); }
        }

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

        // stop global de jobs parallèles
        private CancellationTokenSource? _cts;

        public ICommand CreateJobCommand { get; }
        public ICommand DeleteJobCommand { get; }
        public ICommand ExecuteJobCommand { get; }
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

            CreateJobCommand = new RelayCommand(_ => CreateJob());
            DeleteJobCommand = new RelayCommand(_ => DeleteJob(), _ => SelectedJob != null);

            // Important : async void OK ici car RelayCommand(Action)
            ExecuteJobCommand = new RelayCommand(async _ => await ExecuteJobsParallelAsync(),
                                                _ => SelectedJob != null || (SelectedJobsList?.Count ?? 0) > 0);

            AddExtensionCommand = new RelayCommand(_ => AddExtension());
            RemoveExtensionCommand = new RelayCommand(param => RemoveExtension(param as string), param => param is string);

            StatusMessage = ResourceSettings.GetString("StatusReady");
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
                BackupJobs.Remove(SelectedJob);
                SaveJobs();
                StatusMessage = "Travail supprimé.";
            }
        }

        // PARALLÈLE (multithreading) : tous les jobs démarrent ensemble
        private async Task ExecuteJobsParallelAsync()
        {
            var jobsToRun = new List<BackupJob>();

            if (SelectedJobsList != null && SelectedJobsList.Count > 0)
                jobsToRun.AddRange(SelectedJobsList);
            else if (SelectedJob != null)
                jobsToRun.Add(SelectedJob);

            if (jobsToRun.Count == 0) return;

            // reset progress
            foreach (var j in jobsToRun) j.Progress = 0;
            ProgressValue = 0;

            StatusMessage = $"Exécution parallèle de {jobsToRun.Count} travaux...";

            // token global (si tu ajoutes un bouton Stop plus tard)
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            // 1 task par job
            var tasks = jobsToRun.Select(job => Task.Run(() =>
            {
                // handlers locaux => safe en parallèle
                EventHandler<BackupProgressEventArgs> progressHandler = (sender, args) =>
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        job.Progress = args.Percentage;

                        // progress global = moyenne
                        ProgressValue = (int)Math.Round(jobsToRun.Average(j => j.Progress));

                        StatusMessage = $"{job.Name}: {args.Percentage}%";

                        var stateLog = new StateLog
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
                        };
                        StateSettings.UpdateState(stateLog);
                    });
                };

                EventHandler<(string source, string target, long size, float time)> fileCopiedHandler = (sender, data) =>
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

                job.OnProgressUpdate += progressHandler;
                job.OnFileCopied += fileCopiedHandler;

                try
                {
                    // exécution avec token (stop possible plus tard)
                    job.Execute(token);

                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        job.Progress = 100;

                        StateSettings.UpdateState(new StateLog
                        {
                            BackupName = job.Name,
                            Timestamp = DateTime.Now,
                            State = "NON ACTIVE",
                            Progression = 100,
                            SourceFilePath = "Terminé",
                            TargetFilePath = ""
                        });
                    });
                }
                catch (OperationCanceledException)
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        StateSettings.UpdateState(new StateLog
                        {
                            BackupName = job.Name,
                            Timestamp = DateTime.Now,
                            State = "STOP_REQUESTED",
                            Progression = job.Progress
                        });
                    });
                }
                finally
                {
                    job.OnProgressUpdate -= progressHandler;
                    job.OnFileCopied -= fileCopiedHandler;
                }

            }, token)).ToList();

            try
            {
                await Task.WhenAll(tasks);

                StatusMessage = "Tous les travaux sont terminés !";
                ProgressValue = 100;

                await Task.Delay(1000);
                ProgressValue = 0;
            }
            catch (OperationCanceledException)
            {
                StatusMessage = "Arrêt demandé.";
            }
        }

        public class LanguageProxy
        {
            public string this[string key] => ResourceSettings.GetString(key);
        }
    }
}