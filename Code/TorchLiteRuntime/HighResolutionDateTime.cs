namespace TorchLiteRuntime
{
    using System;
    using System.Runtime.InteropServices;

    public class HighResolutionDateTime
    {
        private static DateTime UtcNow
        {
            get
            {
                if (!IsAvailable)
                {
                    throw new InvalidOperationException(
                        "High resolution clock isn't available.");
                }

                long filetime;
                GetSystemTimePreciseAsFileTime(out filetime);

                return DateTime.FromFileTimeUtc(filetime);
            }
        }

        private static DateTime UtcStart { get; set; }
        private static bool IsAvailable { get; set; }

        public HighResolutionDateTime()
        {
            try
            {
                long filetime;
                GetSystemTimePreciseAsFileTime(out filetime);
                IsAvailable = true;
                UtcStart = UtcNow;
            }
            catch (EntryPointNotFoundException)
            {
                // Not running Windows 8 or higher.
                IsAvailable = false;
            }
        }

        [DllImport("Kernel32.dll", CallingConvention = CallingConvention.Winapi)]
        private static extern void GetSystemTimePreciseAsFileTime(out long filetime);

        public long GetElapsedTicks()
        {
            TimeSpan elapsed = UtcNow - UtcStart;
            return (long)(elapsed.TotalMilliseconds * TimeSpan.TicksPerMillisecond);
        }
    }
}