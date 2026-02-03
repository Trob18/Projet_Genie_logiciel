using System;

namespace EasySave.Log.Models
{
    public class LogEntry
    {
        public string Name { get; set; }
        public string SourceFile { get; set; }
        public string TargetFile { get; set; }
        public long FileSize { get; set; }
        public float TransferTime { get; set; }
        public DateTime Timestamp { get; set; }

        public LogEntry()
        {
            Timestamp = DateTime.Now;
        }

        public LogEntry(string name, string source, string target, long size, float time)
        {
            Name = name;
            SourceFile = source;
            TargetFile = target;
            FileSize = size;
            TransferTime = time;
            Timestamp = DateTime.Now;
        }
    }
}