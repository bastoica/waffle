namespace TorchLiteRuntime
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Torch.Log4Net4Torch;
    using Microsoft.Torch.Log4Net4Torch.Appender;
    using Microsoft.Torch.Log4Net4Torch.Config;
    using Microsoft.Torch.Log4Net4Torch.Core;
    using Microsoft.Torch.Log4Net4Torch.Layout;
    using Microsoft.Torch.Log4Net4Torch.Repository.Hierarchy;

    /// <summary>
    /// A logger based on log4net.
    /// </summary>
    public class Log4NetLogger
    {
        private readonly ILog logger;
        private readonly string logFile;
        private readonly object lockObj = new object();
        private readonly object syncObj = new object();
        private readonly List<String> writeBuffer = new List<String>();
        private volatile bool shutdown;
        private readonly HighResolutionDateTime highResClock;
        private readonly int MAX_BUFFERED_LINES = 1<<16;

        /// <summary>
        /// Initializes a new instance of the <see cref="Log4NetLogger"/> class.
        /// </summary>
        /// <param name="logFile">Log file.</param>
        public Log4NetLogger(string logFile, HighResolutionDateTime clock)
        {
            this.logFile = logFile;
            if (File.Exists(this.logFile))
            {
                try
                {
                    File.Delete(this.logFile);
                }
                catch
                {
                }
            }

            this.highResClock = clock;

            Hierarchy hierarchy = (Hierarchy)LogManager.GetRepository();
            hierarchy.Root.RemoveAllAppenders(); /*Remove any other appenders*/

            var layout = new PatternLayout("%message%newline"); // ("%date %-5level %logger - %message%newline");
            layout.ActivateOptions(); // According to the docs this must be called as soon as any properties have been changed.

            FileAppender fileAppender = new FileAppender()
            {
                File = logFile,
                AppendToFile = true,
                Encoding = Encoding.UTF8,
                Threshold = Level.Debug,
                LockingModel = new FileAppender.MinimalLock(),
                Layout = layout,
                ImmediateFlush = true, //false,
            };
            fileAppender.ActivateOptions();

            ConsoleAppender consoleAppender = new ConsoleAppender();
            consoleAppender.Layout = layout;
            consoleAppender.ActivateOptions();

            IAppender[] appenders = { fileAppender }; // { fileAppender, consoleAppender };
            BasicConfigurator.Configure(appenders);
            this.logger = LogManager.GetLogger(Path.GetFileNameWithoutExtension(logFile));
            ((Hierarchy)LogManager.GetRepository()).Root.Level = Level.Debug;

            //this.HookShutdown(this);
        }

        /// <summary>
        /// Writes a line to the logger.
        /// </summary>
        /// <param name="line">A line to write.</param>
        public void Log(string line)
        {
            var taskId = (Task.CurrentId != null) ? Task.CurrentId : 0;
            this.logger.Debug($"{highResClock.GetElapsedTicks()}\t{Thread.CurrentThread.ManagedThreadId}\t{taskId}\t{VectorClock.Now}\t{line}");
        }

        /// <summary>
        /// Writes a list of lines to the logger.
        /// </summary>
        /// <param name="lines">A list of lines to write.</param>
        public void Log(List<string> lines)
        {
            lock (this.lockObj)
            {
                foreach (string line in lines)
                {
                    this.Log(line);
                }
            }
        }

        /// <summary>
        /// write formatted strings.
        /// </summary>
        /// <param name="format">string format.</param>
        /// <param name="args">arguments.</param>
        public void Log(string format, params object[] args)
        {
            string line = string.Format(format, args);
            this.Log(line);
        }

        /// <summary>
        /// Writes a list of lines to the logger.
        /// </summary>
        public void WriteOutBufferUnsafe()
        {
            foreach (string line in this.writeBuffer)
            {
                this.logger.Debug($"{line}");
            }
            this.writeBuffer.Clear();
        }

        /// <summary>
        /// save string to write buffer
        /// </summary>
        /// <param name="logger"></param>
        public void LogToWriteBuffer(string line)
        {
            lock (this.lockObj)
            {
                var taskId = (Task.CurrentId != null) ? Task.CurrentId : 0;
                this.writeBuffer.Add($"{highResClock.GetElapsedTicks()}\t{Thread.CurrentThread.ManagedThreadId}\t{taskId}\t{VectorClock.Now}\t{line}");
                if (writeBuffer.Count >= MAX_BUFFERED_LINES)
                {
                    this.WriteOutBufferUnsafe();
                }
            }
        }
/*
        private void HookShutdown(Log4NetLogger logger)
        {
            AppDomain.CurrentDomain.DomainUnload += (s, e) => this.Shutdown(logger);
            AppDomain.CurrentDomain.ProcessExit += (s, e) => this.Shutdown(logger);
            AppDomain.CurrentDomain.UnhandledException += (s, e) => this.Shutdown(logger);
            AppDomain.CurrentDomain.AssemblyResolve += (s, e) =>
            {
                this.Shutdown(logger);
                return null;
            };
            AppDomain.CurrentDomain.ReflectionOnlyAssemblyResolve += (s, e) =>
            {
                this.Shutdown(logger);
                return null;
            };
            AppDomain.CurrentDomain.ResourceResolve += (s, e) =>
            {
                this.Shutdown(logger);
                return null;
            };
        }
*/
        public void Shutdown(Log4NetLogger logger)
        {
            lock (this.syncObj)
            {
                if (!this.shutdown)
                {
                    try
                    {
                        if (logger != null)
                        {
                            if (this.writeBuffer.Count > 0)
                            {
                                this.WriteOutBufferUnsafe();
                            }
                            logger.Log("#INFO: Shutting down Torch run-time");
                            this.ShutDownLogger(logger);
                        }
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

        private void ShutDownLogger(Log4NetLogger logger)
        {
            var rep = LogManager.GetRepository();
            foreach (IAppender appender in rep.GetAppenders())
            {
                var buffered = appender as BufferingAppenderSkeleton;
                if (buffered != null)
                {
                    buffered.Flush();
                }
            }

            logger.logger.Logger.Repository.Shutdown();
        }
    }
}
