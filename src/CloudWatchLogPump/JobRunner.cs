using System;
using System.Threading.Tasks;
using CloudWatchLogPump.Extensions;
using CloudWatchLogPump.Model;
using NodaTime;

namespace CloudWatchLogPump
{
    public class JobRunner
    {
        private const int WaitOnIdle = 5 * 1000;
        private const int WaitOnBusy = 0;
        private const int WaitOnError = 30 * 1000;
        private const int WaitOnException = 5 * 60 * 1000;
        private readonly JobRunnerContext _context;
        
        private TaskCompletionSource<bool> _runningTask;
        private TaskCompletionSource<bool> _stoppingTask;
        private Instant _lastLoop;
        private int? _remainingIterations;

        public bool Running => _runningTask != null;
        public Exception TerminatedBy { get; private set; }
        public double MillisSinceLastLoop => InstantUtils.Now.Minus(_lastLoop).TotalSeconds;
        
        public JobRunner(JobRunnerContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _runningTask = null;
            _stoppingTask = null;
            _lastLoop = InstantUtils.Now;
        }

        public Task<bool> Start(int? iterationCount)
        {
            if (Running)
            {
                _context.Logger.Warning("Job is already running when requested to start");
                return _runningTask.Task;
            }

            _context.Logger.Information("Start requested");
            
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
                _context.Logger.Warning("Job is already stopped when requested to stop");
                return Task.FromResult(true);
            }

            if (_stoppingTask != null)
            {
                _context.Logger.Warning("Stop is already requested, ignoring duplicate stop request");
                return _stoppingTask.Task;
            }
            
            _context.Logger.Information("Stop requested");
            _stoppingTask = new TaskCompletionSource<bool>();
            return _stoppingTask?.Task;
        }

        private async void JobRoot()
        {
            _context.Logger.Debug("Runner entry");

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
                    
                    _context.Logger.Debug("Starting new loop");
                    var startInstant = InstantUtils.Now;
                    UpdateLiveliness();

                    JobIterationResult iterationResult;

                    try
                    {
                        var currentProgress = DependencyContext.ProgressDb.Get(_context.Id);
                        _context.Logger.Debug("Current progress: {@Progress}", currentProgress);
                        
                        var iteration = new JobIteration(_context, currentProgress);
                        iterationResult = await iteration.Run();
                        
                        var newProgress = iteration.Progress;

                        _context.Logger.Debug("Iteration completed as {IterationResults}, new progress: {@Progress}", 
                            iterationResult, newProgress);

                        await DependencyContext.ProgressDb.Set(_context.Id, newProgress);
                        
                        var finishInstant = InstantUtils.Now;
                        var totalTimeMillis = (int) finishInstant.Minus(startInstant).TotalMilliseconds;
                    
                        _context.Logger.Information("Iteration done: read {RecordCount,5} records in {ReadTime,4} ms, waited {WaitTime,4} ms, written in {WriteTime,4} ms. Total {TotalTime,5} ms {TotalSize,6} bytes",
                            iteration.RecordCount, iteration.ReadTimeMillis, iteration.WaitTimeMillis, iteration.WriteTimeMillis, totalTimeMillis, iteration.SizeBytes);
                    }
                    catch (Exception e)
                    {
                        _context.Logger.Warning(e, "Job iteration threw exception");
                        iterationResult = JobIterationResult.Exception;
                    }

                    var waitTarget = CalculateWaitTarget(iterationResult);

                    _context.Logger.Debug("Waiting till {WaitTarget} for next iteration", waitTarget);
                    await WaitTill(waitTarget);
                }
                
                _context.Logger.Information("Job stop succeeded, exiting job root");
                _stoppingTask?.SetResult(true);
            }
            catch (Exception e)
            {
                _context.Logger.Error(e, "JobRunner terminated by an exception");
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

                await Task.Delay(Math.Min(1000, remaining));
            }
            
            _context.Logger.Debug("Suspending wait because of stop request");
        }

    }
}