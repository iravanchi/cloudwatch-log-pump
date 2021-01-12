using System;
using System.Collections.Generic;
using System.Net.Http;
using Amazon.CloudWatchLogs;
using NodaTime;
using Serilog;

namespace CloudWatchLogPump
{
    public class JobRunnerContext
    {
        public string Id { get; set; }
        public string AwsRegion { get; set; }
        public string LogGroupName { get; set; }
        
        public Instant StartInstant { get; set; }
        public Instant? EndInstant { get; set; }
        
        public string FilterPattern { get; set; }
        public string LogStreamNamePrefix { get; set; }
        public List<string> LogStreamNames { get; set; }
        
        public int ReadMaxBatchSize { get; set; }
        public int MinIntervalSeconds { get; set; }
        public int MaxIntervalSeconds { get; set; }
        public int ClockSkewProtectionSeconds { get; set; }
        
        public string TargetUrl { get; set; }
        public int TargetMaxBatchSize { get; set; }
        
        public ILogger Logger { get; set; }
        public AmazonCloudWatchLogsClient AwsClient { get; set; }
        public HttpClient HttpClient { get; set; }
        public Random Random { get; set; }
    }
}