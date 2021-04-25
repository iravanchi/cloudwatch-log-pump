using System.Collections.Generic;
using System.Linq;
using CloudWatchLogPump.Extensions;

namespace CloudWatchLogPump.Configuration
{
    public class SubscriptionConfiguration
    {
        public string Id { get; set; }
        
        public string Comments { get; set; }
        public string AwsRegion { get; set; }
        public string LogGroupName { get; set; }
        public string LogGroupPattern { get; set; }
        
        public string StartTimeIso { get; set; }
        public string EndTimeIso { get; set; }
        public int? StartTimeSecondsAgo { get; set; }
        public int? EndTimeSecondsAgo { get; set; }
        
        public string EventFilterPattern { get; set; }
        public string LogStreamNamePrefix { get; set; }
        public List<string> LogStreamNames { get; set; }
        public int? ReadMaxBatchSize { get; set; }
        public int? MinIntervalSeconds { get; set; }
        public int? MaxIntervalSeconds { get; set; }
        public int? ClockSkewProtectionSeconds { get; set; }

        public string TargetUrl { get; set; }
        public int? TargetTimeoutSeconds { get; set; }
        public int? TargetMaxBatchSize { get; set; }
        public string TargetSubscriptionData { get; set; }

        public List<SubscriptionConfiguration> Children { get; set; }

        public void ExtendFrom(SubscriptionConfiguration parent)
        {
            if (parent == null)
                return;
            
            if (parent.Id.HasValue())
                Id = Id.HasValue() ? parent.Id + "." + Id : parent.Id;

            Comments = Comments.Or(parent.Comments);
            AwsRegion = AwsRegion.Or(parent.AwsRegion);
            LogGroupName = LogGroupName.Or(parent.LogGroupName);
            LogGroupPattern = LogGroupPattern.Or(parent.LogGroupPattern);

            StartTimeIso = StartTimeIso.Or(parent.StartTimeIso);
            EndTimeIso = EndTimeIso.Or(parent.EndTimeIso);
            StartTimeSecondsAgo ??= parent.StartTimeSecondsAgo;
            EndTimeSecondsAgo ??= parent.EndTimeSecondsAgo;

            EventFilterPattern = EventFilterPattern.Or(parent.EventFilterPattern);
            LogStreamNamePrefix = LogStreamNamePrefix.Or(parent.LogStreamNamePrefix);
            if (parent.LogStreamNames.SafeAny())
                LogStreamNames = LogStreamNames.SafeUnion(parent.LogStreamNames).ToList();
            
            ReadMaxBatchSize ??= parent.ReadMaxBatchSize;
            MinIntervalSeconds ??= parent.MinIntervalSeconds;
            MaxIntervalSeconds ??= parent.MaxIntervalSeconds;
            ClockSkewProtectionSeconds ??= parent.ClockSkewProtectionSeconds;

            TargetUrl = TargetUrl.Or(parent.TargetUrl);
            TargetTimeoutSeconds ??= parent.TargetTimeoutSeconds;
            TargetMaxBatchSize ??= parent.TargetMaxBatchSize;
            TargetSubscriptionData = TargetSubscriptionData.Or(parent.TargetSubscriptionData);
        }
        
        public SubscriptionConfiguration Clone(bool deep)
        {
            return new SubscriptionConfiguration
            {
                Id = Id,
                Comments = Comments,
                AwsRegion = AwsRegion,
                LogGroupName = LogGroupName,
                LogGroupPattern = LogGroupPattern,

                StartTimeIso = StartTimeIso,
                EndTimeIso = EndTimeIso,
                StartTimeSecondsAgo = StartTimeSecondsAgo,
                EndTimeSecondsAgo = EndTimeSecondsAgo,

                EventFilterPattern = EventFilterPattern,
                LogStreamNamePrefix = LogStreamNamePrefix,
                LogStreamNames = deep ? LogStreamNames?.ToList() : LogStreamNames,
                ReadMaxBatchSize = ReadMaxBatchSize,
                MinIntervalSeconds = MinIntervalSeconds,
                MaxIntervalSeconds = MaxIntervalSeconds,
                ClockSkewProtectionSeconds = ClockSkewProtectionSeconds,
                
                TargetUrl = TargetUrl,
                TargetTimeoutSeconds = TargetTimeoutSeconds,
                TargetMaxBatchSize = TargetMaxBatchSize,
                TargetSubscriptionData = TargetSubscriptionData,
                
                Children = deep ? Children?.Select(c => c?.Clone(true)).ToList() : Children 
            };
        }
    }
}