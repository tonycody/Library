using System;

namespace Library
{
    public enum LogMessageLevel
    {
        Information,
        Warning,
        Error,
    }

    public class LogEventArgs : EventArgs
    {
        private LogMessageLevel _logMessageLevel;
        private string _message;
        private Exception _exception;

        public LogMessageLevel MessageLevel
        {
            get
            {
                return _logMessageLevel;
            }
            set
            {
                _logMessageLevel = value;
            }
        }

        public string Message
        {
            get
            {
                return _message;
            }
            set
            {
                _message = value;
            }
        }

        public Exception Exception
        {
            get
            {
                return _exception;
            }
            set
            {
                _exception = value;
            }
        }
    }

    public delegate void LogEventHandler(object sender, LogEventArgs e);

    public static class Log
    {
        public static event LogEventHandler LogEvent;

        private static void LogEventHandler(LogEventArgs e)
        {
            if (LogEvent != null)
            {
                LogEvent(null, e);
            }
        }

        private static string FromException(Exception exception)
        {
            if (exception == null) return "";

            string message1 = "";
            string message2 = "";
            string message3 = "";

            message1 = exception.GetType().ToString();

            if (exception.Message != null)
                message2 = exception.Message;

            if (exception.StackTrace != null)
                message3 = exception.StackTrace;

            return string.Format(
                "Exception:\t{0}\r\n" +
                "Message:\t{1}\r\n" +
                "StackTrace:\r\n{2}",
                message1, message2, message3);
        }

        public static void Information(string value)
        {
            Log.LogEventHandler(new LogEventArgs() { MessageLevel = LogMessageLevel.Information, Message = value });
        }

        public static void Information(Exception exception)
        {
            Log.LogEventHandler(new LogEventArgs() { MessageLevel = LogMessageLevel.Information, Message = Log.FromException(exception), Exception = exception });
        }

        public static void Warning(string value)
        {
            Log.LogEventHandler(new LogEventArgs() { MessageLevel = LogMessageLevel.Warning, Message = value });
        }

        public static void Warning(Exception exception)
        {
            Log.LogEventHandler(new LogEventArgs() { MessageLevel = LogMessageLevel.Warning, Message = Log.FromException(exception), Exception = exception });
        }

        public static void Error(string value)
        {
            Log.LogEventHandler(new LogEventArgs() { MessageLevel = LogMessageLevel.Error, Message = value });
        }

        public static void Error(Exception exception)
        {
            Log.LogEventHandler(new LogEventArgs() { MessageLevel = LogMessageLevel.Error, Message = Log.FromException(exception), Exception = exception });
        }
    }
}
