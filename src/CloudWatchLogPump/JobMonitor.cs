using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;
using CloudWatchLogPump.Configuration;
using Serilog;

namespace CloudWatchLogPump
{
    public class JobMonitor
    {
        private readonly ConcurrentDictionary<string, JobRunner> _runners;

        public JobMonitor()
        {
            _runners = new ConcurrentDictionary<string, JobRunner>();
        }

        public void StartAll()
        {
            DependencyContext.Configuration.Subscriptions.ForEach(Start);
        }
        
        public void Start(SubscriptionConfiguration subscription)
        {
            Log.Logger.Information("Starting all subscriptions");
            
            var runner = GetOrCreateRunner(subscription);
            runner.Start(null);
        }

        private JobRunner GetOrCreateRunner(SubscriptionConfiguration subscription)
        {
            return _runners.GetOrAdd(subscription.Id, _ => new JobRunner(subscription));
        }

        public void StopAll()
        {
            Log.Logger.Information("Stopping all subscriptions");
            
            var runners = _runners.Values.ToList();
            var stopTasks = runners.Select(r => r.Stop()).Cast<Task>().ToArray();

            Task.WaitAll(stopTasks);
        }
    }
}