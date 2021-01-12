using System.Collections.Generic;

namespace CloudWatchLogPump.Configuration
{
    public class SubscriptionConfiguration
    {
        public string Id { get; set; }
        public string AwsRegion { get; set; }
        public string LogGroupName { get; set; }
        
        public string StartTimeIso { get; set; }
        public string EndTimeIso { get; set; }
        public int? StartTimeSecondsAgo { get; set; }
        public int? EndTimeSecondsAgo { get; set; }
        
        public string FilterPattern { get; set; }
        public string LogStreamNamePrefix { get; set; }
        public List<string> LogStreamNames { get; set; }
        public int? ReadMaxBatchSize { get; set; }
        public int? MinIntervalSeconds { get; set; }
        public int? MaxIntervalSeconds { get; set; }
        public int? ClockSkewProtectionSeconds { get; set; }
        
        public string TargetUrl { get; set; }
        public int? TargetTimeoutSeconds { get; set; }
        public int TargetMaxBatchSize { get; set; }
    }
}