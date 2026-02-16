using EasySave.WPF.Config;
using EasySave.WPF.Enumerations;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;

namespace EasySave.WPF.Models
{
    public class BackupJob : INotifyPropertyChanged
    {
        public string Name { get; set; } = string.Empty;
        public string SourceDirectory { get; set; } = string.Empty;
        public string TargetDirectory { get; set; } = string.Empty;

        public BackupType Type { get; set; }
        public BackupState State { get; set; } = BackupState.Inactive;

        public event EventHandler<BackupProgressEventArgs>? OnProgressUpdate;
        public event EventHandler<(string source, string target, long size, float time)>? OnFileCopied;

        private int _progress;
        public int Progress
        {
            get => _progress;
            set { _progress = value; OnPropertyChanged(); }
        }

        public BackupJob(string name, string source, string target, BackupType type)
        {
            Name = name;
            SourceDirectory = source;
            TargetDirectory = target;
            Type = type;
            State = BackupState.Inactive;
        }

        public BackupJob()
        {
            
        }

        // Compatibilité
        public void Execute() => Execute(CancellationToken.None);

        // Stop réel (entre 2 fichiers)
        public void Execute(CancellationToken ct)
        {
            State = BackupState.Active;

            if (string.IsNullOrWhiteSpace(SourceDirectory) || !Directory.Exists(SourceDirectory))
            {
                State = BackupState.Error;
                return;
            }

            // Extensions à chiffrer
            List<string> encryptedExtensions = AppSettings.Instance.EncryptedExtensions
                .Split(new char[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(ext => ext.ToLower().Trim())
                .ToList();

            var allFiles = Directory.GetFiles(SourceDirectory, "*.*", SearchOption.AllDirectories);

            int totalFiles = allFiles.Length;
            int processedCount = 0;

            long totalSize = 0;
            foreach (var f in allFiles) totalSize += new FileInfo(f).Length;

            long currentSizeProcessed = 0;

            string cryptoSoftPath = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "..", "..", "..", "..",
                "CryptoSoft", "bin", "Debug", "net8.0", "win-x64", "CryptoSoft.exe"
            );

            string encryptionKey = "EasySaveEncryptionKey";

            foreach (var filePath in allFiles)
            {
                // STOP entre 2 fichiers
                ct.ThrowIfCancellationRequested();

                string relativePath = Path.GetRelativePath(SourceDirectory, filePath);
                string targetFilePath = Path.Combine(TargetDirectory, relativePath);

                string? targetDir = Path.GetDirectoryName(targetFilePath);
                if (!string.IsNullOrWhiteSpace(targetDir) && !Directory.Exists(targetDir))
                    Directory.CreateDirectory(targetDir);

                long currentFileSize = new FileInfo(filePath).Length;

                bool shouldCopy = false;
                if (Type == BackupType.Full)
                {
                    shouldCopy = true;
                }
                else if (Type == BackupType.Differential)
                {
                    if (!File.Exists(targetFilePath) || File.GetLastWriteTime(filePath) > File.GetLastWriteTime(targetFilePath))
                        shouldCopy = true;
                }

                if (shouldCopy)
                {
                    long copyTime;
                    long encryptionTime = 0;

                    ct.ThrowIfCancellationRequested();

                    Stopwatch swCopy = Stopwatch.StartNew();
                    try
                    {
                        File.Copy(filePath, targetFilePath, true);
                        swCopy.Stop();
                        copyTime = swCopy.ElapsedMilliseconds;
                    }
                    catch
                    {
                        swCopy.Stop();
                        OnFileCopied?.Invoke(this, (filePath, targetFilePath, currentFileSize, -swCopy.ElapsedMilliseconds));
                        State = BackupState.Error;

                        processedCount++;
                        currentSizeProcessed += currentFileSize;

                        OnProgressUpdate?.Invoke(this, new BackupProgressEventArgs(
                            totalFiles,
                            processedCount,
                            totalSize,
                            currentSizeProcessed,
                            Path.GetFileName(filePath),
                            filePath,
                            targetFilePath
                        ));
                        continue;
                    }

                    bool shouldEncrypt = AppSettings.Instance.EncryptAll;
                    if (!shouldEncrypt)
                    {
                        string ext = Path.GetExtension(filePath).ToLower();
                        shouldEncrypt = encryptedExtensions.Contains(ext);
                    }

                    if (shouldEncrypt)
                    {
                        ct.ThrowIfCancellationRequested();

                        try
                        {
                            var startInfo = new ProcessStartInfo
                            {
                                FileName = cryptoSoftPath,
                                Arguments = $"\"{targetFilePath}\" \"{encryptionKey}\"",
                                UseShellExecute = false,
                                RedirectStandardOutput = true,
                                CreateNoWindow = true
                            };

                            using var process = Process.Start(startInfo);
                            if (process != null)
                            {
                                process.WaitForExit();
                                encryptionTime = process.ExitCode; // >=0 temps, <0 erreur
                            }
                            else
                            {
                                encryptionTime = -1;
                            }
                        }
                        catch
                        {
                            encryptionTime = -1;
                        }
                    }

                    OnFileCopied?.Invoke(this, (filePath, targetFilePath, currentFileSize, copyTime + encryptionTime));
                }

                processedCount++;
                currentSizeProcessed += currentFileSize;

                OnProgressUpdate?.Invoke(this, new BackupProgressEventArgs(
                    totalFiles,
                    processedCount,
                    totalSize,
                    currentSizeProcessed,
                    Path.GetFileName(filePath),
                    filePath,
                    targetFilePath
                ));
            }

            State = BackupState.Inactive;
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}