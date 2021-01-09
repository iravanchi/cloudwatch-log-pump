using System.Collections.Generic;

namespace CloudWatchLogPump.Configuration
{
    public class SubscriptionConfiguration
    {
        public string Id { get; set; }
        public string LogGroupName { get; set; }
        public string RelativeStartTimestamp { get; set; }
        public string AbsoluteStartTimestamp { get; set; }
        public string RelativeEndTimestamp { get; set; }
        public string AbsoluteEndTimestamp { get; set; }
        public string FilterPattern { get; set; }
        public string LogStreamNamePrefix { get; set; }
        public List<string> LogStreamNames { get; set; }
        public int MinIntervalSeconds { get; set; }
        public int MaxIntervalSeconds { get; set; }
        public string TargetUrl { get; set; }
        public int TargetMaxBatchSize { get; set; }
    }
}