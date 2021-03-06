﻿using System;
using System.Threading.Tasks;
using CloudWatchLogPump.Extensions;
using NodaTime;

namespace CloudWatchLogPump
{
    public class JobRunner
    {
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

                    try
                    {
                        var currentProgress = DependencyContext.ProgressDb.Get(_context.Id);
                        _context.Logger.Debug("Current progress: {@Progress}", currentProgress);
                        
                        var iteration = new JobIteration(_context, currentProgress);
                        var thereIsMore = await iteration.Run();
                        
                        var newProgress = iteration.Progress;

                        _context.Logger.Debug("Iteration completed. There is more: {ThereIsMore}, new progress: {@Progress}", thereIsMore, newProgress);
                        await DependencyContext.ProgressDb.Set(_context.Id, newProgress);
                        
                        var finishInstant = InstantUtils.Now;
                        var totalTimeMillis = (int) finishInstant.Minus(startInstant).TotalMilliseconds;
                    
                        _context.Logger.Information("Iteration done: read {RecordCount,5} records in {ReadTime,5} ms, waited {WaitTime,5} ms, written in {WriteTime,5} ms. Total {TotalTime,6} ms {TotalSize,7} bytes",
                            iteration.RecordCount, iteration.ReadTimeMillis, iteration.WaitTimeMillis, iteration.WriteTimeMillis, totalTimeMillis, iteration.SizeBytes);

                        if (!thereIsMore)
                        {
                            var waitTarget = InstantUtils.Now.Plus(Duration.FromMilliseconds(Timing.Runner.MinWaitMillisOnIdle));
                            var nextIterationDue = newProgress.NextIterationEnd.Plus(
                                Duration.FromSeconds(_context.ClockSkewProtectionSeconds));
                            
                            if (waitTarget < nextIterationDue)
                                waitTarget = nextIterationDue;

                            _context.Logger.Debug("Waiting till {WaitTarget} for next iteration", waitTarget);
                            await WaitTill(waitTarget);
                        }
                    }
                    catch (Exception e)
                    {
                        var exceptionWaitTarget = InstantUtils.Now.Plus(Duration.FromMilliseconds(Timing.Runner.WaitMillisOnException));
                        _context.Logger.Warning(e, "Job iteration threw exception. Waiting till {WaitTarget} to continue", exceptionWaitTarget);
                        await WaitTill(exceptionWaitTarget);
                    }
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