namespace TorchLiteRuntime
{
    using System.Collections.Generic;

    /// <summary>
    /// Delay injection plans.
    /// </summary>
    public enum DelayInjectionEvent
    {
        /// <summary>
        /// Inject delay before a field write.
        /// </summary>
        BeforeWrite,

        /// <summary>
        /// Inject delay after a field write.
        /// </summary>
        AfterWrite,

        /// <summary>
        /// Before a field read operation.
        /// </summary>
        BeforeRead,

        /// <summary>
        /// After a field read operation.
        /// </summary>
        AfterRead,

        /// <summary>
        /// Before a field is used (e.g., field.Method()).
        /// </summary>
        BeforeUse,

        /// <summary>
        /// After a field is used.
        /// </summary>
        AfterUse,
    }

    /// <summary>
    /// A delay injection plan.
    /// </summary>
    public class DelayInjectionPlan
    {
        /// <summary>
        /// Gets or sets the delay injection location.
        /// </summary>
        public TorchPoint targetStaticTp;

        /// <summary>
        /// Gets or sets how much delay to inject.
        /// </summary>
        public int DelayMs { get; set; }

        /// <summary>
        /// Gets or sets the delay injection probability for each instance.
        /// </summary>
        public double DelayProbability { get; set; }

        /// <summary>
        /// Gets or sets at what callback to inject delays.
        /// </summary>
        public DelayInjectionEvent DelayInjectionEvent { get; set; }

        /// <summary>
        /// </summary>
        public HashSet<string> ConflictOps = new HashSet<string>();
    }
}
