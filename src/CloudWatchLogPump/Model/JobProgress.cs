using NodaTime;

namespace CloudWatchLogPump.Model
{
    public class JobProgress
    {
        // Type is created as immutable to avoid problems with accidental changes to in-memory cache
        public JobProgress(Instant nextIterationStart, Instant nextIterationEnd, string nextToken)
        {
            NextIterationStart = nextIterationStart;
            NextIterationEnd = nextIterationEnd;
            NextToken = nextToken;
        }

        public Instant NextIterationStart { get; }
        public Instant NextIterationEnd { get; }
        public string NextToken { get; }
    }
}