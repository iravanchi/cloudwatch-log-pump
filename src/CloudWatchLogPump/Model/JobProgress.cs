using NodaTime;

namespace CloudWatchLogPump.Model
{
    public class JobProgress
    {
        public Instant CurrentIterationStart { get; set; }
        public Instant CurrentIterationEnd { get; set; }
        public string NextToken { get; set; }
    }
}