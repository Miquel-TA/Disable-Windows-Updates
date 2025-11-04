using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace DisableWindowsUpdates
{
    internal enum LogLevel
    {
        Debug = 0,
        Info = 1,
        Warning = 2,
        Error = 3
    }

    internal static class Logger
    {
        private const LogLevel MinimumLevel = LogLevel.Info;

        private static readonly object SyncRoot = new object();
        private static readonly string LogDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "DisableWindowsUpdates",
            "logs");

        private static readonly string LogFilePath = Path.Combine(LogDirectory, "application.log");

        public static void Debug(string message)
        {
            Write(LogLevel.Debug, message, null);
        }

        public static void Info(string message)
        {
            Write(LogLevel.Info, message, null);
        }

        public static void Warning(string message)
        {
            Write(LogLevel.Warning, message, null);
        }

        public static void Error(string message, Exception exception)
        {
            Write(LogLevel.Error, message, exception);
        }

        public static void Error(string message)
        {
            Write(LogLevel.Error, message, null);
        }

        private static void Write(LogLevel level, string message, Exception exception)
        {
            if (level < MinimumLevel)
            {
                return;
            }

            try
            {
                var timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
                var builder = new StringBuilder();
                builder.Append('[')
                    .Append(timestamp)
                    .Append("] [")
                    .Append(level.ToString().ToUpperInvariant())
                    .Append("] ")
                    .Append(message);

                if (exception != null)
                {
                    builder.Append(" Exception: ")
                        .Append(exception.GetType().FullName)
                        .Append(" - ")
                        .Append(exception.Message)
                        .AppendLine()
                        .Append(exception.StackTrace);
                }

                lock (SyncRoot)
                {
                    Directory.CreateDirectory(LogDirectory);
                    File.AppendAllText(LogFilePath, builder.AppendLine().ToString(), Encoding.UTF8);
                }
            }
            catch
            {
                // Logging should never throw exceptions back to the caller.
            }
        }
    }
}
