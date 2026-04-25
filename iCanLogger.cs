using System;
using System.Threading; 

namespace industREAL.CAN.CORE
{
    /// <summary>Defines the levels of logging severity.</summary>
    public enum LogLevel { Trace, Debug, Info, Warn, Error, Fatal }

    /// <summary>Interface for the consumer's logging implementation (e.g., Serilog, NLog, or custom).</summary>
    public interface ILogSink
    {
        /// <summary>Checks if a log level is currently enabled by the sink.</summary>
        bool IsEnabled(LogLevel level);

        /// <summary>Writes a log message to the sink.</summary>
        /// <param name="level">The severity level of the log message.</param>
        /// <param name="message">The message template string.</param>
        /// <param name="ex">An optional exception associated with the log.</param>
        /// <param name="args">Optional arguments for formatting the message template.</param>
        void Log(LogLevel level, string message, System.Exception? ex = null, params object[] args);
    }

    /// <summary>Static facade used by your DLL code to access the logger.</summary>
    public static class Log
    {
        private sealed class NullSink : ILogSink
        {
            public bool IsEnabled(LogLevel level) => false;
            public void Log(LogLevel level, string message, System.Exception? ex = null, params object[] args) { }
        }

        private static ILogSink _sink = new NullSink();

        /// <summary>Retrieves the current ILogSink instance in a thread-safe manner.</summary>
        private static ILogSink GetSink() => Volatile.Read(ref _sink);

        /// <summary>App sets this once at startup to plug in its logger implementation.</summary>
        public static ILogSink Sink
        {
            get => GetSink();
            // Ensures the sink is never null by falling back to NullSink. Thread-safe write.
            set => Volatile.Write(ref _sink, value ?? new NullSink());
        }

        // --- Convenience Methods Your DLL Will Call ---
        // These methods check IsEnabled() before calling Log() to avoid expensive argument processing 
        // and string formatting if the log level is disabled.

        public static void Trace(string msg, params object[] args)
        {
            var sink = GetSink();
            if (sink.IsEnabled(LogLevel.Trace))
                sink.Log(LogLevel.Trace, msg, null, args);
        }

        public static void Debug(string msg, params object[] args)
        {
            var sink = GetSink();
            if (sink.IsEnabled(LogLevel.Debug))
                sink.Log(LogLevel.Debug, msg, null, args);
        }

        public static void Info(string msg, params object[] args)
        {
            var sink = GetSink();
            if (sink.IsEnabled(LogLevel.Info))
                sink.Log(LogLevel.Info, msg, null, args);
        }

        public static void Warn(string msg, params object[] args)
        {
            var sink = GetSink();
            if (sink.IsEnabled(LogLevel.Warn))
                sink.Log(LogLevel.Warn, msg, null, args);
        }

        public static void Error(string msg, params object[] args)
        {
            var sink = GetSink();
            if (sink.IsEnabled(LogLevel.Error))
                sink.Log(LogLevel.Error, msg, null, args);
        }

        public static void Error(System.Exception ex, string msg, params object[] args)
        {
            var sink = GetSink();
            if (sink.IsEnabled(LogLevel.Error))
                sink.Log(LogLevel.Error, msg, ex, args);
        }

        public static void Fatal(string msg, params object[] args)
        {
            var sink = GetSink();
            if (sink.IsEnabled(LogLevel.Fatal))
                sink.Log(LogLevel.Fatal, msg, null, args);
        }

        public static void Fatal(System.Exception ex, string msg, params object[] args)
        {
            var sink = GetSink();
            if (sink.IsEnabled(LogLevel.Fatal))
                sink.Log(LogLevel.Fatal, msg, ex, args);
        }
    }
}

/*
 * 
 * public class ConsoleLogSink : industREAL.CAN.CORE.ILogSink
{
    private readonly industREAL.CAN.CORE.LogLevel _minLevel = industREAL.CAN.CORE.LogLevel.Debug;

    public bool IsEnabled(industREAL.CAN.CORE.LogLevel level)
    {
        return level >= _minLevel;
    }

    public void Log(industREAL.CAN.CORE.LogLevel level, string message, Exception? ex = null, params object[] args)
    {
        // This is where string formatting happens, only if IsEnabled was true
        string formattedMessage = (args.Length > 0) ? string.Format(message, args) : message;
        
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}][{level}] {formattedMessage}");

        if (ex != null)
        {
            Console.WriteLine($"\tException: {ex.Message}");
        }
    }
}

// In the application's startup code:
public static void Main()
{
    // 1. Plug in the custom logger at startup
    industREAL.CAN.CORE.Log.Sink = new ConsoleLogSink();

    // 2. Your library's code calls Log:
    industREAL.CAN.CORE.Log.Debug("CAN-FD initialized successfully on interface {0}.", "can0");
    
    try {
        // ... some CAN operation that fails ...
    } catch (Exception e) {
        industREAL.CAN.CORE.Log.Fatal(e, "CAN communication failure while trying to send message ID {0}.", 0x18F);
    }
}
 * 
 */