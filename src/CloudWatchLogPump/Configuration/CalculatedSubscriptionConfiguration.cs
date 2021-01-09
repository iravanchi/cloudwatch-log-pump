using System.Collections.Generic;
using NodaTime;

namespace CloudWatchLogPump.Configuration
{
    public class CalculatedSubscriptionConfiguration
    {
        public string Id { get; set; }
        public string LogGroupName { get; set; }
        
        public Instant StartInstant { get; set; }
        public Instant? EndInstant { get; set; }
        
        public string FilterPattern { get; set; }
        public string LogStreamNamePrefix { get; set; }
        public List<string> LogStreamNames { get; set; }
        
        public int MinIntervalSeconds { get; set; }
        public int MaxIntervalSeconds { get; set; }
        public string TargetUrl { get; set; }
        public int TargetMaxBatchSize { get; set; }
    }
}