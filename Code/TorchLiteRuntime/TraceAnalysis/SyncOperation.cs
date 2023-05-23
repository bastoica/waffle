namespace TraceAnalysis
{
    public enum SyncType
    {
        Acquire,

        Release
    }
    public class SyncOperation
    {
        public SyncType type;
        public string site;

        public SyncOperation(SyncType ty, string si)
        {
            type = ty;
            site = si;
        }
    }
}
