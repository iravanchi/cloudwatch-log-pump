using System.Collections.Generic;

namespace CloudWatchLogPump.Configuration
{
    public class RootConfiguration
    {
        public List<SubscriptionConfiguration> Subscriptions { get; set; }
        
        public string ProgressDbPath { get; set; }
        public string LogFolderPath { get; set; }
        public bool EnableDebugConsoleLog { get; set; }
        public bool EnableDebugFileLog { get; set; }
    }
}