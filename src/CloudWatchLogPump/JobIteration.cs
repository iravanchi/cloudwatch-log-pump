using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Amazon.CloudWatchLogs.Model;
using CloudWatchLogPump.Extensions;
using CloudWatchLogPump.Model;
using NodaTime;

namespace CloudWatchLogPump
{
    public class JobIteration
    {
        private readonly JobRunnerContext _context;

        public JobProgress Progress { get; private set; }
        public int RecordCount { get; private set; }
        public int ReadTimeMillis { get; private set; }
        public int WriteTimeMillis { get; private set; }
        public int WaitTimeMillis { get; private set; }
        public int SizeBytes { get; private set; }

        private FilterLogEventsResponse _readResponse;
        private List<List<TargetEventModel>> _outputBatches;

        public JobIteration(JobRunnerContext context, JobProgress currentProgress)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            Progress = currentProgress;
        }

        public async Task<bool> Run()
        {
            CalculateProgressBeforeRead();
            if (!IsRunnable(Progress))
                return false;

            await ReadRecords();
            
            // Not updating the Progress property yet, so that if there's an exception during HTTP call
            // to target, the records are not skipped.
            var nextProgress = CalculateProgressAfterRead();

            if (RecordCount > 0)
            {
                PrepareOutput();
                foreach (var batch in _outputBatches)
                    await SendBatch(batch);
            }

            Progress = nextProgress;
            return IsRunnable(nextProgress);
        }
        
        private void CalculateProgressBeforeRead()
        {
            var start = Progress?.NextIterationStart ?? _context.StartInstant;
            var endCutoffInstant = InstantUtils.SecondsAgo(_context.ClockSkewProtectionSeconds);
            var minEndInstant = start.Plus(Duration.FromSeconds(_context.MinIntervalSeconds));
            var maxEndInstant = start.Plus(Duration.FromSeconds(_context.MaxIntervalSeconds));
            
            var end = Progress?.NextIterationEnd ?? maxEndInstant;
            if (end > maxEndInstant) end = maxEndInstant;
            if (end > endCutoffInstant) end = endCutoffInstant;
            if (end < minEndInstant) end = minEndInstant;

            // Fix the iteration based on _context.End only if the _context.End is between progress start and end
            // (so that the iteration needs to be run partially). We don't need to consider any other cases, if
            // the Progress.end is passed _context.end, we won't run the iteration anyway. 
            if (_context.EndInstant.HasValue && 
                start < _context.EndInstant.Value &&
                end > _context.EndInstant.Value)
            {
                end = _context.EndInstant.Value;
            }

            Progress = new JobProgress(start, end, Progress?.NextToken);
        }

        private JobProgress CalculateProgressAfterRead()
        {
            // There's no need to consider _context.End here. We just calculate the prospective next iteration span
            // and it will be looked at and fixed before the next iteration, if needed.
            
            if (!string.IsNullOrWhiteSpace(_readResponse.NextToken))
                return new JobProgress(Progress.NextIterationStart, Progress.NextIterationEnd, _readResponse.NextToken);

            // If the current timespan is completed, we start from one millisecond after the end of this timespan 
            // because AWS APIs accept milliseconds as timespan, so the resolution is 1 ms. And the API is
            // inclusive for both start and end, so we don't want events with timestamp equal to previous end
            // be returned twice.
            var start = Progress.NextIterationEnd.Plus(Duration.FromMilliseconds(1));
            
            var endCutoffInstant = InstantUtils.SecondsAgo(_context.ClockSkewProtectionSeconds);
            var minEndInstant = start.Plus(Duration.FromSeconds(_context.MinIntervalSeconds));
            var maxEndInstant = start.Plus(Duration.FromSeconds(_context.MaxIntervalSeconds));

            if (minEndInstant > endCutoffInstant)
                return new JobProgress(start, minEndInstant, null);

            var end = maxEndInstant;
            if (end > endCutoffInstant) end = endCutoffInstant;

            return new JobProgress(start, end, null);
        }

        private bool IsRunnable(JobProgress progress)
        {
            if (_context.EndInstant.HasValue)
            {
                // Just check for the end. If this would have been a partial iteration (_context.End was between
                // Progress.Start and Progress.End), CalculateProgressBeforeRead method would have been adjusted it.
                if (progress.NextIterationEnd > _context.EndInstant.Value)
                    return false;
                
                // If the Progress.End is exactly equal to _context.End, allow it to be run without any regard
                // to cut-off, since it's the last iteration ever to be run.
                if (progress.NextIterationEnd == _context.EndInstant.Value)
                    return true;
            }
            
            var endCutoffInstant = InstantUtils.SecondsAgo(_context.ClockSkewProtectionSeconds);
            return progress.NextIterationEnd <= endCutoffInstant;
        }
        
        private async Task ReadRecords()
        {
            var beforeRead = InstantUtils.Now;
            
            var request = new FilterLogEventsRequest()
            {
                LogGroupName = _context.LogGroupName,
                StartTime = Progress.NextIterationStart.ToUnixTimeMilliseconds(),
                EndTime = Progress.NextIterationEnd.ToUnixTimeMilliseconds(),
                NextToken = Progress.NextToken,
                FilterPattern = _context.FilterPattern,
                LogStreamNamePrefix = _context.LogStreamNamePrefix,
                LogStreamNames = _context.LogStreamNames,
                Limit = _context.ReadMaxBatchSize
            };

            _context.Logger.Debug("Calling AWS API with input {@Request}", request);

            _readResponse = await _context.AwsClient.FilterLogEventsAsync(request);
            if (_readResponse.HttpStatusCode != HttpStatusCode.OK)
                throw new ApplicationException("AWS Service call did not return OK status code");

            RecordCount = _readResponse.Events.Count;
            SizeBytes = (int) _readResponse.ContentLength;
            
            var afterRead = InstantUtils.Now;
            ReadTimeMillis = (int) afterRead.Minus(beforeRead).TotalMilliseconds;
        }

        private void PrepareOutput()
        {
            _outputBatches = new List<List<TargetEventModel>>();
            
            var currentBatch = new List<TargetEventModel>(_context.TargetMaxBatchSize);
            _outputBatches.Add(currentBatch);

            foreach (var inputEvent in _readResponse.Events)
            {
                if (currentBatch.Count >= _context.TargetMaxBatchSize)
                {
                    currentBatch = new List<TargetEventModel>(_context.TargetMaxBatchSize);
                    _outputBatches.Add(currentBatch);
                }
                
                currentBatch.Add(new TargetEventModel
                {
                    CloudWatchRegion = _context.AwsRegion,
                    CloudWatchEventId = inputEvent.EventId,
                    CloudWatchIngestionTime = DateTimeOffset.FromUnixTimeMilliseconds(inputEvent.IngestionTime),
                    CloudWatchTimestamp = DateTimeOffset.FromUnixTimeMilliseconds(inputEvent.Timestamp),
                    CloudWatchLogGroup = _context.LogGroupName,
                    CloudWatchLogStream = inputEvent.LogStreamName,
                    CloudWatchMessage = inputEvent.Message
                });
            }
        }

        private async Task SendBatch(List<TargetEventModel> batch)
        {
            const int maxWaitBaseMillis = 2000;
            const int maxWaitMultiplier = 4;
            const int maxWaitCoefficientUpperLimit = maxWaitMultiplier * maxWaitMultiplier * maxWaitMultiplier;
            
            var maxWaitCoefficient = 1;
            
            while (true)
            {
                _context.Logger.Debug("Calling HTTP target with a batch of {BatchSize} messages", batch.Count);

                var beforeWrite = InstantUtils.Now;
                var json = JsonSerializer.Serialize(batch);
                var postResult = await _context.HttpClient.PostAsync(_context.TargetUrl, new StringContent(json));
                var afterWrite = InstantUtils.Now;
                WriteTimeMillis += (int) afterWrite.Minus(beforeWrite).TotalMilliseconds;

                _context.Logger.Debug("HTTP Call ended with status code {HttpStatus} {HttpStatusReason}", 
                    postResult.StatusCode, postResult.ReasonPhrase);
                
                if (postResult.IsSuccessStatusCode) 
                    return;

                if (!postResult.StatusCode.IsRetryable())
                    throw new ApplicationException("Target write failed with status code " + postResult.StatusCode);
                
                if (maxWaitCoefficient > maxWaitCoefficientUpperLimit)
                    throw new ApplicationException("Target write failed after too many retries, with status code " + postResult.StatusCode);
                
                await WaitRandom(maxWaitCoefficient * maxWaitBaseMillis);
                maxWaitCoefficient *= maxWaitMultiplier;
            }
        }

        private async Task WaitRandom(int maxWaitMillis)
        {
            var waitMillis = _context.Random.Next(maxWaitMillis);
            _context.Logger.Debug("Waiting before retry for {WaitTime} ms", waitMillis);

            var beforeWait = InstantUtils.Now;
            await Task.Delay(waitMillis);
            var afterWait = InstantUtils.Now;
            WaitTimeMillis += (int) afterWait.Minus(beforeWait).TotalMilliseconds;
        }
    }
}