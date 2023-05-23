// <copyright file="FileLogger.cs" company="Microsoft Research">
// Copyright (c) Microsoft Research. All rights reserved.
// </copyright>

namespace TorchLiteRuntime
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// File logger.
    /// </summary>
    public class FileLogger
    {
        /// <summary>
        /// File path for logging.
        /// </summary>
        private readonly string filePath;

        /// <summary>
        /// Log lock.
        /// </summary>
        private readonly object logFileLock;

        /// <summary>
        /// High resolution clock.
        /// </summary>
        private readonly HighResolutionDateTime highResClock = new HighResolutionDateTime();

        /// <summary>
        /// Write buffer.
        /// </summary>
        private readonly List<string> writeBuffer = new List<string>();

        /// <summary>
        /// Max write buffer size.
        /// </summary>
        private readonly int MAX_BUFFERED_LINES = 1 << 20;

        private bool shutdown = false;

        /// <summary>
        /// Initializes a new instance of the <see cref="FileLogger"/> class.
        /// </summary>
        /// <param name="filePath">File path to log.</param>
        /// <param name="append">Indicates if the existing logs should be appended or overwritten.</param>
        public FileLogger(string filePath, bool append = false)
        {
            this.filePath = filePath;
            this.logFileLock = new object();
            lock (this.logFileLock)
            {
                if (!append && File.Exists(this.filePath))
                {
                    File.Delete(this.filePath);
                }
            }
        }

        public void Log(string line, bool isBufferingActive = false)
        {
            if (Constants.DisableLogging)
            {
                return; // logging has been disabled
            }

            var taskId = (Task.CurrentId != null) ? Task.CurrentId : 0;
#if VCLOCKS_ENABLED
            string logLine = $"{highResClock.GetElapsedTicks()}\t{Thread.CurrentThread.ManagedThreadId}\t{taskId}\t{VectorClock.Now}\t{line}";    
#else
            string logLine = $"{highResClock.GetElapsedTicks()}\t{Thread.CurrentThread.ManagedThreadId}\t{taskId}\t{0}\t{line}";
#endif
            if (isBufferingActive == false)
            {
                this.Log(new List<string> { logLine });
            }
            else
            {
                lock (this.logFileLock)
                {
                    this.writeBuffer.Add(logLine);
                    if (this.writeBuffer.Count >= MAX_BUFFERED_LINES)
                    {
                        this.Log(this.writeBuffer);
                        this.writeBuffer.Clear();
                    }
                }
            }
        }

        public void Flush()
        {
            lock (this.logFileLock)
            {
                if (this.writeBuffer.Count > 0)
                {
                    this.Log(this.writeBuffer);
                    this.writeBuffer.Clear();
                }
            }
        }

        public void LogVerbose(string line)
        {
            var taskId = (Task.CurrentId != null) ? Task.CurrentId : 0;
#if VCLOCKS_ENABLED
            string logLine = $"{highResClock.GetElapsedTicks()}\t{Thread.CurrentThread.ManagedThreadId}\t{taskId}\t{VectorClock.Now}\t{line}";    
#else
            string logLine = $"{highResClock.GetElapsedTicks()}\t{Thread.CurrentThread.ManagedThreadId}\t{taskId}\t{0}\t{line}";
#endif
            this.Log(new List<string> { logLine });
        }

        /// <summary>
        /// write formatted strings.
        /// </summary>
        /// <param name="format">string format.</param>
        /// <param name="args">arguments.</param>
        public void Log(string format, params object[] args)
        {
            string line = string.Format(format, args);
            var taskId = (Task.CurrentId != null) ? Task.CurrentId : 0;
#if VCLOCKS_ENABLED
            string logLine = $"{highResClock.GetElapsedTicks()}\t{Thread.CurrentThread.ManagedThreadId}\t{taskId}\t{VectorClock.Now}\t{line}";
#else
            string logLine = $"{highResClock.GetElapsedTicks()}\t{Thread.CurrentThread.ManagedThreadId}\t{taskId}\t{0}\t{line}";
#endif
            this.Log(new List<string> { logLine });
        }

        /// <summary>
        /// Write a list of lines.
        /// </summary>
        /// <param name="lines">list of lines to write.</param>
        public void Log(List<string> lines)
        {
            lock (this.logFileLock)
            {
                try
                {
                    using (var fs = new FileStream(this.filePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
                    {
                        using (StreamWriter streamWriter = new StreamWriter(fs))
                        {
                            foreach (string line in lines)
                            {
                                streamWriter.WriteLine($"{line}");
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    throw e;
                }
            }
        }

        /// <summary>
        /// Cleanup and gracefully close output.
        /// </summary>
        public void Shutdown()
        {
            if (!this.shutdown)
            {
                try
                {
                    this.Log("#INFO: Shutting down Torch run-time", true);
                    this.Log(this.writeBuffer);
                }
                catch (Exception exception)
                {
                    Console.Error.WriteLine($"#ERROR: {exception.GetType()} thrown shutting down Torch run-time. {exception.Message}");
                    Console.Error.WriteLine(exception.StackTrace);
                }
                finally
                {
                    this.shutdown = true;
                }
            }
        }
    }
}
