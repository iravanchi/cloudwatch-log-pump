using System;
using System.Linq;
using System.Threading.Tasks;
using Amazon;
using Amazon.CloudWatchLogs;
using Amazon.CloudWatchLogs.Model;
using CloudWatchLogPump.Configuration;
using CloudWatchLogPump.Model;

namespace CloudWatchLogPump
{
    public class JobIteration
    {
        private readonly SubscriptionConfiguration _subscription;

        public JobIterationResult Result { get; private set; }
        public JobProgress Progress { get; private set; }

        public JobIteration(SubscriptionConfiguration subscription, JobProgress currentProgress)
        {
            _subscription = subscription ?? throw new ArgumentNullException(nameof(subscription));
            Progress = currentProgress;
        }

        public async Task<JobIterationResult> Run()
        {
            throw new NotImplementedException();
        }

        public static async Task DescribeSubscriptionFilters()
        {
            var client = new AmazonCloudWatchLogsClient(RegionEndpoint.CACentral1);
            string nextToken = null;

            do
            {
                var logs = await client.FilterLogEventsAsync(new FilterLogEventsRequest()
                {
                    LogGroupName = "log_group_name",
                    StartTime = DateTimeOffset.UtcNow.AddMinutes(-5).ToUnixTimeMilliseconds(),

                    NextToken = nextToken
                });

                logs.Events.ForEach(e => { });

                if (logs.Events != null && logs.Events.Any())
                    Console.WriteLine($"Received {logs.Events.Count} events " +
                                      $"from {DateTimeOffset.FromUnixTimeMilliseconds(logs.Events[0].Timestamp):s} " +
                                      $"to {DateTimeOffset.FromUnixTimeMilliseconds(logs.Events[^1].Timestamp):s} ");

                nextToken = logs.NextToken;
            } while (!string.IsNullOrWhiteSpace(nextToken));
        }

        private static async Task UseDescribeLogStreams(IAmazonCloudWatchLogs client)
        {
            string nextToken = null;

            for (int i = 0; i < 5; i++)
            {
                var describeLogStreamsRequest = new DescribeLogStreamsRequest()
                {
                    LogGroupName = "log_group_name",
                    NextToken = nextToken,
                    OrderBy = OrderBy.LastEventTime,
                    Descending = true
                };

                var describeLogStreamsResult = await client.DescribeLogStreamsAsync(describeLogStreamsRequest);
                foreach (var stream in describeLogStreamsResult.LogStreams)
                    Console.WriteLine(stream.LogStreamName);

                Console.WriteLine();
                Console.WriteLine();
                Console.WriteLine();

                if (string.IsNullOrWhiteSpace(describeLogStreamsResult.NextToken))
                    break;

                nextToken = describeLogStreamsResult.NextToken;
            }
        }
    }
}