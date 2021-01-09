using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using CloudWatchLogPump.Model;
using NodaTime.Text;

namespace CloudWatchLogPump
{
    public class ProgressDb
    {
        private readonly string _basePath;
        private readonly bool _storageEnabled;
        private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);
        private readonly Regex _deserializeRegex = new Regex("^([^|]+)\\|([^|]+)\\|(.*)$");
        private readonly InstantPattern _instantPattern = InstantPattern.General;
        private readonly ConcurrentDictionary<string, JobProgress> _progresses 
            = new ConcurrentDictionary<string, JobProgress>();

        public ProgressDb(string basePath)
        {
            _basePath = basePath;
            _storageEnabled = !string.IsNullOrWhiteSpace(_basePath);

            if (_storageEnabled && !Directory.Exists(_basePath))
                Directory.CreateDirectory(_basePath);
        }

        public JobProgress Get(string key)
        {
            return _progresses.TryGetValue(key, out var progress) ? progress : null;
        }

        public async Task Set(string key, JobProgress progress)
        {
            await _semaphore.WaitAsync();
            try
            {
                _progresses[key] = progress;

                if (_storageEnabled)
                {
                    var filePath = GetFilePath(key);
                    var text = Serialize(progress);
                    await File.WriteAllTextAsync(filePath, text);
                }
            }
            finally
            {
                _semaphore.Release();
            }
        }

        public async Task LoadAll(IEnumerable<string> keys)
        {
            if (!_storageEnabled)
                return;
            
            foreach (var key in keys)
                await Load(key);
        }

        public async Task<JobProgress> Load(string key)
        {
            if (!_storageEnabled)
                return null;
            
            var filePath = GetFilePath(key);
            
            await _semaphore.WaitAsync();
            try
            {
                if (!File.Exists(filePath)) 
                    return null;
            
                var text = await File.ReadAllTextAsync(filePath);
                if (string.IsNullOrWhiteSpace(text))
                {
                    _progresses[key] = null;
                    return null;
                }
                
                var progress = Deserialize(text);
                _progresses[key] = progress
                                   ?? throw new ApplicationException($"The contents of file {filePath} does not match the expected input format for progressDb");

                return progress;
            }
            finally
            {
                _semaphore.Release();
            }
        }

        private string GetFilePath(string key)
        {
            return Path.Combine(_basePath, key);
        }

        private string Serialize(JobProgress progress)
        {
            return _instantPattern.Format(progress.CurrentIterationStart) + "|" +
                   _instantPattern.Format(progress.CurrentIterationEnd) + "|" +
                   (progress.NextToken ?? string.Empty);
        }

        private JobProgress Deserialize(string text)
        {
            var match = _deserializeRegex.Match(text);
            if (!match.Success)
                return null;

            var start = _instantPattern.Parse(match.Groups[1].ToString());
            var end = _instantPattern.Parse(match.Groups[2].ToString());
            var nextToken = match.Groups[3].ToString();

            if (!start.Success || !end.Success)
                return null;
            
            return new JobProgress
            {
                CurrentIterationStart = start.Value,
                CurrentIterationEnd = end.Value,
                NextToken = nextToken
            };
        }
    }
}