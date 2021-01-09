using CloudWatchLogPump.Configuration;

namespace CloudWatchLogPump
{
    public class DependencyContext
    {
        public static RootConfiguration Configuration { get; set; }
        public static JobMonitor Monitor { get; set; }
        public static ProgressDb ProgressDb { get; set; }
    }
}