using System;
using System.Linq;
using System.Threading.Tasks;
using Amazon;
using Amazon.CloudWatchLogs;
using Amazon.CloudWatchLogs.Model;

namespace CloudWatchLogPump
{
    class Program
    {
        static async Task Main(string[] args)
        {
            await DescribeSubscriptionFilters();
        }

        public static async Task DescribeSubscriptionFilters()
        {
            IAmazonCloudWatchLogs client = new AmazonCloudWatchLogsClient(RegionEndpoint.CACentral1);

            string nextToken = null;
            
            do
            {
                var logs = await client.FilterLogEventsAsync(new FilterLogEventsRequest()
                {
                    LogGroupName = "log_group_name",
                    StartTime = DateTimeOffset.UtcNow.AddMinutes(-5).ToUnixTimeMilliseconds(),
                    NextToken = nextToken
                });
                
                logs.Events.ForEach(e =>
                {
                });

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
