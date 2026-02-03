using EasySave.Log.Interfaces;
using EasySave.Log.Loggers;

namespace EasySave.Log
{
    public static class LoggerCrea
    {
        /// <summary>
        /// Fabrique un logger selon le format demandé (json ou xml).
        /// </summary>
        /// <param name="format">"json" ou "xml"</param>
        /// <param name="logDirectory">Le dossier où stocker les fichiers</param>
        /// <returns>Une instance respectant l'interface ILogger</returns>
        public static ILogger CreateLogger(string format, string logDirectory)
        {
            switch (format.ToLower())
            {
                case "xml":
                    return new XmlLogger(logDirectory);

                case "json":
                default:
                    return new JsonLogger(logDirectory);
            }
        }
    }
}