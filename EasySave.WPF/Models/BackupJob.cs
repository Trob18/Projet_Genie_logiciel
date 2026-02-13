using EasySave.WPF.Enumerations;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using EasySave.WPF.Config;
using System.Threading;

namespace EasySave.WPF.Models
{
    public class BackupJob
    {
        public string Name { get; set; }
        public string SourceDirectory { get; set; }
        public string TargetDirectory { get; set; }
        public BackupType Type { get; set; }
        public BackupState State { get; set; }

        public event EventHandler<BackupProgressEventArgs> OnProgressUpdate;
        public event EventHandler<(string source, string target, long size, float time)> OnFileCopied;

        public BackupJob(string name, string source, string target, BackupType type)
        {
            Name = name;
            SourceDirectory = source;
            TargetDirectory = target;
            Type = type;
            State = BackupState.Inactive;
        }

        public BackupJob() { }

        // Compatibilité
        public void Execute() => Execute(CancellationToken.None);

        // Stop réel
        public void Execute(CancellationToken ct)
        {
            State = BackupState.Active;

            if (!Directory.Exists(SourceDirectory))
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
                if (!string.IsNullOrEmpty(targetDir) && !Directory.Exists(targetDir))
                    Directory.CreateDirectory(targetDir);

                long currentFileSize = new FileInfo(filePath).Length;

                bool shouldCopy = false;

                if (Type == BackupType.Full)
                    shouldCopy = true;
                else if (Type == BackupType.Differential)
                {
                    if (!File.Exists(targetFilePath) ||
                        File.GetLastWriteTime(filePath) > File.GetLastWriteTime(targetFilePath))
                        shouldCopy = true;
                }

                if (shouldCopy)
                {
                    long copyTime;
                    long encryptionTime = 0;

                    var swCopy = Stopwatch.StartNew();
                    try
                    {
                        File.Copy(filePath, targetFilePath, true);
                        swCopy.Stop();
                        copyTime = swCopy.ElapsedMilliseconds;
                    }
                    catch
                    {
                        swCopy.Stop();
                        // temps négatif en cas d’erreur (CDC)
                        OnFileCopied?.Invoke(this, (filePath, targetFilePath, currentFileSize, -swCopy.ElapsedMilliseconds));
                        State = BackupState.Error;
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
                        // ✅ Stop avant CryptoSoft aussi
                        ct.ThrowIfCancellationRequested();

                        try
                        {
                            ProcessStartInfo startInfo = new ProcessStartInfo
                            {
                                FileName = cryptoSoftPath,
                                Arguments = $"\"{targetFilePath}\" \"{encryptionKey}\"",
                                UseShellExecute = false,
                                RedirectStandardOutput = true,
                                CreateNoWindow = true
                            };

                            using (Process process = Process.Start(startInfo))
                            {
                                process.WaitForExit();
                                encryptionTime = process.ExitCode; // >=0 temps / <0 erreur
                            }
                        }
                        catch
                        {
                            // Si crash lancement CryptoSoft => code erreur négatif
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
    }
}