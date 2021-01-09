using System;
using System.Threading.Tasks;
using CloudWatchLogPump.Configuration;
using CloudWatchLogPump.Extensions;
using CloudWatchLogPump.Model;
using NodaTime;
using Serilog;

namespace CloudWatchLogPump
{
    public class JobRunner
    {
        private const int WaitOnIdle = 5 * 1000;
        private const int WaitOnBusy = 0;
        private const int WaitOnError = 30 * 1000;
        private const int WaitOnException = 5 * 60 * 1000;
        private readonly ILogger _logger;
        private readonly SubscriptionConfiguration _subscription;
        
        private TaskCompletionSource<bool> _runningTask;
        private TaskCompletionSource<bool> _stoppingTask;
        private Instant _lastLoop;
        private int? _remainingIterations;

        public bool Running => _runningTask != null;
        public Exception TerminatedBy { get; private set; }
        public double MillisSinceLastLoop => InstantUtils.Now.Minus(_lastLoop).TotalSeconds;
        
        public JobRunner(SubscriptionConfiguration subscription)
        {
            _subscription = subscription ?? throw new ArgumentNullException(nameof(subscription));
            _logger = Log.Logger.ForContext<JobRunner>().ForContext("SubscriptionId", subscription.Id);
            _runningTask = null;
            _stoppingTask = null;
            _lastLoop = InstantUtils.Now;
        }

        public Task<bool> Start(int? iterationCount)
        {
            if (Running)
            {
                _logger.Warning("Job is already running when requested to start");
                return _runningTask.Task;
            }

            _logger.Information("Start requested");
            
            // TODO: Concurrency
            _runningTask = new TaskCompletionSource<bool>();
            _stoppingTask = null;
            _remainingIterations = iterationCount;
            TerminatedBy = null;

            Task.Factory.StartNew(JobRoot);
            return _runningTask.Task;
        }

        public Task<bool> Stop()
        {
            if (!Running)
            {
                _logger.Warning("Job is already stopped when requested to stop");
                return Task.FromResult(true);
            }

            if (_stoppingTask != null)
            {
                _logger.Warning("Stop is already requested, ignoring duplicate stop request");
                return _stoppingTask.Task;
            }
            
            _logger.Information("Stop requested");
            _stoppingTask = new TaskCompletionSource<bool>();
            return _stoppingTask?.Task;
        }

        private async void JobRoot()
        {
            _logger.Debug("Runner entry");
            // TODO: Set initial value in progressDb

            try
            {
                while (_stoppingTask == null)
                {
                    if (_remainingIterations.HasValue)
                    {
                        _remainingIterations--;
                        if (_remainingIterations.Value < 0)
                            break;
                    }
                    
                    _logger.Debug("Starting new loop");
                    var startInstant = InstantUtils.Now;
                    UpdateLiveliness();

                    JobIterationResult iterationResult;

                    try
                    {
                        var currentProgress = DependencyContext.ProgressDb.Get(_subscription.Id);
                        _logger.Debug("Current progress: {Progress}", currentProgress);
                        
                        var iteration = new JobIteration(_subscription, currentProgress);
                        await iteration.Run();
                        
                        iterationResult = iteration.Result;
                        var newProgress = iteration.Progress;

                        _logger.Debug("Iteration completed as {IterationResults}, new progress: {Progress}", 
                            iterationResult, newProgress);

                        await DependencyContext.ProgressDb.Set(_subscription.Id, newProgress);
                    }
                    catch (Exception e)
                    {
                        _logger.Warning(e, "Job iteration threw exception");
                        iterationResult = JobIterationResult.Exception;
                    }

                    var finishInstant = InstantUtils.Now;
                    var totalTimeMillis = (int) finishInstant.Minus(startInstant).TotalMilliseconds;
                    
                    // TODO: Log read/write/total times
                    _logger.Information("Iteration complete");

                    var waitTarget = CalculateWaitTarget(iterationResult);

                    _logger.Debug("Waiting till {WaitTarget} for next iteration", waitTarget);
                    await WaitTill(waitTarget);
                }
                
                _logger.Information("Job stop succeeded, exiting background task root");
                _stoppingTask?.SetResult(true);
            }
            catch (Exception e)
            {
                _logger.Error(e, "JobRunner terminated by an exception");
                TerminatedBy = e;
            }
            finally
            {
                _stoppingTask?.SetResult(false);
                _runningTask?.SetResult(TerminatedBy == null);

                _stoppingTask = null;
                _runningTask = null;
            }
        }
        
        private static Instant CalculateWaitTarget(JobIterationResult result)
        {
            int wait = result switch
            {
                JobIterationResult.Idle => WaitOnIdle,
                JobIterationResult.ThereIsMore => WaitOnBusy,
                JobIterationResult.Error => WaitOnError,
                JobIterationResult.Exception => WaitOnException,
                _ => throw new ArgumentOutOfRangeException(nameof(result), result, null)
            };

            return InstantUtils.Now.Plus(Duration.FromMilliseconds(wait));
        }

        private void UpdateLiveliness()
        {
            _lastLoop = InstantUtils.Now;
        }

        private async Task WaitTill(Instant instant)
        {
            while(_stoppingTask == null)
            {
                UpdateLiveliness();
                
                var remaining = (int) instant.Minus(InstantUtils.Now).TotalMilliseconds;
                if (remaining <= 0)
                    return;

                await Task.Delay(Math.Min(3000, remaining));
            }
            
            _logger.Debug("Suspending wait because of stop request");
        }

    }
}