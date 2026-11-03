using System;

namespace RawConverter.Common
{
    /// <summary>
    /// Message sink for the conversion library. Hosts (CLI, GUI) decide where messages go;
    /// by default nothing is written.
    /// </summary>
    public static class Log
    {
        public static Action<string> InfoSink { get; set; }
        public static Action<string> WarningSink { get; set; }

        public static void Info(string message)
        {
            InfoSink?.Invoke(message);
        }

        public static void Warning(string message)
        {
            WarningSink?.Invoke(message);
        }
    }
}
