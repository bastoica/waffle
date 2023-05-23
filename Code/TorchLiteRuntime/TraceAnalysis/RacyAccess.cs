namespace TraceAnalysis
{
    using System;

    /// <summary>
    /// Write operation types.
    /// </summary>
    public enum WriteType
    {
        /// <summary>
        /// Writes null to a field, whose original value was not null.
        /// </summary>
        NullToNonNull,

        /// <summary>
        /// Writes a non-null value to a field, whose original value was null.
        /// </summary>
        NonNullToNull,

        /// <summary>
        /// Dispose of an field object.
        /// </summary>
        Dispose,

        /// <summary>
        /// Writes a non-null balue to a field, whose original bvalue was non-null.
        /// </summary>
        Other,

        /// <summary>
        /// Not a write operation.
        /// </summary>
        NotWrite,
    }

    /// <summary>
    /// Read operation types.
    /// </summary>
    public enum ReadType
    {
        /// <summary>
        /// Read a field (ldfld)
        /// </summary>
        Read,

        /// <summary>
        /// Use of a field, such as "Call field.method()"
        /// </summary>
        Use,
    }

    /// <summary>
    /// A potentially-racy access-pair.
    /// </summary>
    public class RacyAccess
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="RacyAccess"/> class.
        /// </summary>
        /// <param name="read">Read operation.</param>
        /// <param name="readType">Read operation type.</param>
        /// <param name="write">Write operation.</param>
        /// <param name="writeType">Write operation type.</param>
        public RacyAccess(MemoryAccess read, ReadType readType, MemoryAccess write, WriteType writeType)
        {
            this.Read = read;
            this.ReadType = readType;
            this.Write = write;
            this.WriteType = writeType;
            this.AccessGapMs = (int)Math.Ceiling(1.0 * Math.Abs(read.Timestamp - write.Timestamp) / TimeSpan.TicksPerMillisecond);
        }

        /// <summary>
        /// Gets or sets the read operation.
        /// </summary>
        public MemoryAccess Read { get; set; }

        /// <summary>
        /// Gets or sets the read type.
        /// </summary>
        public ReadType ReadType { get; set; }

        /// <summary>
        /// Gets or sets the write operation.
        /// </summary>
        public MemoryAccess Write { get; set; }

        /// <summary>
        /// Gets or sets the write type.
        /// </summary>
        public WriteType WriteType { get; set; }

        /// <summary>
        /// Gets or sets gaps between two accesses.
        /// </summary>
        public int AccessGapMs { get; set; }
    }
}
