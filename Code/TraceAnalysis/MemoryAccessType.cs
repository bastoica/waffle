namespace TraceAnalysis
{
    /// <summary>
    /// Memory operation types.
    /// </summary>
    public enum MemoryAccessType
    {
        /// <summary>
        /// Uncategorized operation.
        /// </summary>
        None,

        /// <summary>
        /// Read operation.
        /// </summary>
        Read,

        /// <summary>
        /// Write operation.
        /// </summary>
        Write,

        /// <summary>
        /// Use operation (for method call).
        /// </summary>
        Use,

        /// <summary>
        /// Dispose operation.
        /// </summary>
        Dispose,

        /// <summary>
        /// Lock/unlock operation.
        /// </summary>
        Lock,
    }
}
