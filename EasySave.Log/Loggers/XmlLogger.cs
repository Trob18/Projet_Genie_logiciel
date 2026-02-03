using EasySave.Log.Interfaces;
using EasySave.Log.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Serialization;

namespace EasySave.Log.Loggers
{
    public class XmlLogger : ILogger
    {
        private readonly string _logDirectory;

        public XmlLogger(string logDirectory)
        {
            _logDirectory = logDirectory;
        }

        public void WriteLog(LogEntry logEntry)
        {
            string fileName = $"{DateTime.Now:yyyy-MM-dd}.xml";
            string filePath = Path.Combine(_logDirectory, fileName);

            if (!Directory.Exists(_logDirectory))
            {
                Directory.CreateDirectory(_logDirectory);
            }

            var logs = new List<LogEntry>();
            var serializer = new XmlSerializer(typeof(List<LogEntry>));

            if (File.Exists(filePath))
            {
                try
                {
                    using (var stream = new FileStream(filePath, FileMode.Open))
                    {
                        if (stream.Length > 0)
                        {
                            logs = (List<LogEntry>)serializer.Deserialize(stream);
                        }
                    }
                }
                catch
                {
                    logs = new List<LogEntry>();
                }
            }

            logs.Add(logEntry);

            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                serializer.Serialize(stream, logs);
            }
        }
    }
}