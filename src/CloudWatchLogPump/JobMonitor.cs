using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;
using Serilog;

namespace CloudWatchLogPump
{
    public class JobMonitor
    {
        private readonly ILogger _logger = Log.Logger.ForContext<JobMonitor>();
        private readonly ConcurrentDictionary<string, JobRunner> _runners;
        private volatile bool _stopping; // TODO Using a cancellation token makes more sense!

        public JobMonitor()
        {
            _runners = new ConcurrentDictionary<string, JobRunner>();
        }

        public void StartAll()
        {
            if (_stopping)
                throw new InvalidOperationException("Each instance of JobMonitor can be used once.");
            
            StartMonitor();
        }
        
        public void StopAll()
        {
            _stopping = true;
            _logger.Information("Stopping all subscriptions");
            
            var runners = _runners.Values.ToList();
            var stopTasks = runners.Select(r => r.Stop()).Cast<Task>().ToArray();

            Task.WaitAll(stopTasks);
        }

        private void StartMonitor()
        {
            Task.Factory.StartNew(MonitorRoot);
        }

        private async void MonitorRoot()
        {
            while (true)
            {
                if (_stopping)
                    return;
                
                foreach (var subscription in DependencyContext.RunnerContexts.Values)
                {
                    CheckJobLiveliness(subscription);
                    if (_stopping)
                        return;
                }

                for (var i = 0; i < Timing.Monitor.SecondsBetweenReCheckingJobRunners; i++)
                {
                    await Task.Delay(1000);
                    if (_stopping)
                        return;
                }
            }
        }

        private void CheckJobLiveliness(JobRunnerContext subscription)
        {
            if (!_runners.TryGetValue(subscription.Id, out var runner))
            {
                _logger.Information("No JobRunner for {SubscriptionId} yet, starting one.", subscription.Id);
                RecycleRunner(subscription);
                return;
            }

            if (!runner.Running && !_stopping)
            {
                _logger.Warning("JobRunner for {SubscriptionId} appears to be stopped. Recycling.", subscription.Id);
                RecycleRunner(subscription);
            }

            if (runner.MillisSinceLastLoop > Timing.Monitor.SecondsBeforeConsiderRunningJobUnresponsive * 1000)
            {
                _logger.Warning("JobRunner for {SubscriptionId} is not responding. Recycling.", subscription.Id);
                RecycleRunner(subscription);
            }
        }

        private void RecycleRunner(JobRunnerContext subscription)
        {
            // Create a new JobRunner for the subscription anyway.
            // If there's a runner present, replace it and stop the original one.
            
            _runners.AddOrUpdate(subscription.Id,
                id =>
                {
                    // addValueFactory; If the value is not present in the dictionary
                    var newRunner = new JobRunner(subscription);
                    newRunner.Start(null);
                    return newRunner;
                },
                (id, runner) =>
                {
                    // updateValueFactory; If there is a runner in the dictionary

                    // Stop the currently-running (probably already dead) runner to make sure it doesn't
                    // come back to life and cause problems
                    if (runner.Running)
                        runner.Stop();
                    
                    var newRunner = new JobRunner(subscription);
                    newRunner.Start(null);
                    return newRunner;
                });
        }
    }
}
