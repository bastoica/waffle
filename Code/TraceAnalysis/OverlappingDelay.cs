namespace TraceAnalysis
{
    using System;

    /// <summary>
    /// A potentially-racy access-pair.
    /// </summary>
    public class OverlappingDelay
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="OverlappingDelay"/> class.
        /// </summary>
        /// <param name="write">Read operation.</param>
        /// <param name="overlap">Write operation.</param>
        public OverlappingDelay(MemoryAccess intercept, MemoryAccess overlap)
        {
            this.Intercept = intercept;
            this.Overlap = overlap;
        }

        /// <summary>
        /// Gets or sets read operation.
        /// </summary>
        public MemoryAccess Intercept { get; set; }

        /// <summary>
        /// Gets or sets write operation.
        /// </summary>
        public MemoryAccess Overlap { get; set; }

        /// <summary>
        /// </summary>
        public long DynamicCounter;

        /// <summary>
        /// </summary>
        public long OverlapLength;
    }
}
