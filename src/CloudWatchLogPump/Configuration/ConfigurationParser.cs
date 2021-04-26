using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Amazon;
using Amazon.CloudWatchLogs;
using Amazon.CloudWatchLogs.Model;
using CloudWatchLogPump.Extensions;
using Microsoft.Extensions.Configuration;
using NodaTime;
using NodaTime.Text;
using Serilog;
using InvalidOperationException = System.InvalidOperationException;

namespace CloudWatchLogPump.Configuration
{
    public static class ConfigurationParser
    {
        private static readonly Regex ConfigurationIdRegex = new Regex("^[0-9a-zA-Z_\\-\\.]+$");
        private static readonly Regex ConfigurationIdCleanupRegex = new Regex("[^0-9a-zA-Z_\\-\\.]");
        private static readonly InstantPattern InstantPattern = InstantPattern.General;
        
        public static void LoadConfiguration(string configFilePath)
        {
            var builder = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile(configFilePath, false);

            var config = builder.Build();
            DependencyContext.Configuration = config.Get<RootConfiguration>();
        }

        public static void FlattenSubscriptions()
        {
            var originalList = DependencyContext.Configuration.Subscriptions;
            if (originalList == null || !originalList.Any())
                return;

            var flattenedList = new List<SubscriptionConfiguration>();
            foreach (var subscription in originalList)
            {
                FlattenSubscriptionItem(flattenedList, null, subscription);
            }

            DependencyContext.Configuration.Subscriptions = flattenedList;
        }

        private static void FlattenSubscriptionItem(List<SubscriptionConfiguration> flattenedList, 
            SubscriptionConfiguration parent, SubscriptionConfiguration item)
        {
            var extendedItem = item.Clone(false);
            extendedItem.ExtendFrom(parent);
            extendedItem.Children = null;

            if (item.Children.SafeAny())
            {
                foreach (var child in item.Children)
                {
                    FlattenSubscriptionItem(flattenedList, extendedItem, child);
                }
            }
            else
            {
                flattenedList.Add(extendedItem);
            }
        }

        public static async Task ExpandSubscriptionPatterns()
        {
            var originalList = DependencyContext.Configuration.Subscriptions;
            if (originalList == null || !originalList.Any())
                return;

            var expandedList = new List<SubscriptionConfiguration>();
            foreach (var subscription in originalList)
            {
                if (subscription.LogGroupPattern.HasValue())
                    expandedList.AddRange(await ExpandSubscriptionPattern(subscription));
                else
                    expandedList.Add(subscription);
            }

            DependencyContext.Configuration.Subscriptions = expandedList;
        }

        private static async Task<List<SubscriptionConfiguration>> ExpandSubscriptionPattern(SubscriptionConfiguration originalSubscription)
        {
            var cw = new AmazonCloudWatchLogsClient(RegionEndpoint.GetBySystemName(originalSubscription.AwsRegion));
            var regex = new Regex(originalSubscription.LogGroupPattern, RegexOptions.IgnoreCase);
            var expandedList = new List<SubscriptionConfiguration>();
            var nextToken = (string) null;
            
            do
            {
                var response = await cw.DescribeLogGroupsAsync(new DescribeLogGroupsRequest {NextToken = nextToken});
                if (response.HttpStatusCode != HttpStatusCode.OK)
                    throw new InvalidOperationException($"Subscription '{originalSubscription.Id}' - " +
                                                        $"Could not query AWS CloudWatch Logs for list of log groups");

                nextToken = response.NextToken;
                foreach (var logGroup in response.LogGroups.OrEmpty())
                {
                    if (!regex.IsMatch(logGroup.LogGroupName))
                        continue;

                    var clonedSubscription = originalSubscription.Clone(false);
                    clonedSubscription.LogGroupPattern = null;
                    clonedSubscription.LogGroupName = logGroup.LogGroupName;
                    clonedSubscription.Id += "." + ConfigurationIdCleanupRegex.Replace(logGroup.LogGroupName, "_");
                    expandedList.Add(clonedSubscription);
                }
            } 
            while (nextToken.HasValue());

            Log.Logger.Information(
                "Expanded {OriginalId} into {ExpandedCount} subscriptions for log groups: {ExpandedList}",
                originalSubscription.Id,
                expandedList.Count,
                string.Join(", ", expandedList.Select(s => s.LogGroupName)));
            
            return expandedList;
        }

        public static void ValidateConfiguration()
        {
            var config = DependencyContext.Configuration;
            
            if (config.Subscriptions == null || !config.Subscriptions.Any())
                throw new ArgumentException("There are no subscriptions defined in the configuration file. " +
                                            "At least one subscription is required for the application to run.");

            config.Subscriptions.ForEach(ValidateSubscription);
            
            var duplicateIds = config.Subscriptions.GroupBy(s => s.Id).Where(g => g.Count() > 1).Select(g => g.Key).ToArray();
            if (duplicateIds.Any())
                throw new ArgumentException("There are duplicates in the subscription IDs: " + string.Join(", ", duplicateIds));
        }

        private static void ValidateSubscription(SubscriptionConfiguration config)
        {
            if (config.Id.IsNullOrWhitespace())
                throw new ArgumentException("Subscription ID is required for all subscriptions.");

            if (config.Id.Length > 100)
                throw new ArgumentException("Subscription ID cannot be longer than 100 characters.");

            if (!ConfigurationIdRegex.IsMatch(config.Id))
                throw new ArgumentException($"Subscription '{config.Id}' - Invalid characters in Subscription ID. Allowed characters are alpha numeric, dash, underscore and dot.");

            if (config.TargetUrl.IsNullOrWhitespace())
                throw new ArgumentException($"Subscription '{config.Id}' - targetUrl is required to be specified");

            if (config.AwsRegion.IsNullOrWhitespace())
                throw new ArgumentException($"Subscription '{config.Id}' - awsRegion is required to be specified");

            if (config.LogGroupName.IsNullOrWhitespace())
                throw new ArgumentException($"Subscription '{config.Id}' - logGroupName is required to be specified");

            if (config.LogGroupPattern.HasValue())
                throw new ArgumentException($"Subscription '{config.Id}' - logGroupPattern should have been expanded at this point and set to null");

            if (config.LogStreamNames != null &&
                config.LogStreamNames.SafeAny(lsn => lsn.HasValue()) &&
                config.LogStreamNamePrefix.HasValue())
            {
                throw new ArgumentException($"Subscription '{config.Id}' - logStreamNames and logStreamNamePrefix fields cannot be specified at the same time. AWS API allows only one of them to be specified in each call.");
            }
            
            if (config.StartTimeIso.HasValue() && config.StartTimeSecondsAgo.HasValue)
                throw new ArgumentException($"Subscription '{config.Id}' - startTimeIso and startTimeSecondsAgo cannot be specified at the same time. Only one of them allowed to be present in the configuration.");

            if (config.EndTimeIso.HasValue() && config.EndTimeSecondsAgo.HasValue)
                throw new ArgumentException($"Subscription '{config.Id}' - endTimeIso and endTimeSecondsAgo cannot be specified at the same time. Only one of them allowed to be present in the configuration.");

            if (config.Children != null)
                throw new ArgumentException($"Subscription '{config.Id}' - children need to be flattened in this point and Children property must have been set to null.");
        }

        public static void CoerceConfiguration()
        {
            DependencyContext.RunnerContexts = new Dictionary<string, JobRunnerContext>();
            
            if (DependencyContext.Configuration.Subscriptions.Count > 0)
                DependencyContext.Configuration.Subscriptions.ForEach(CoerceSubscription);
        }

        private static void CoerceSubscription(SubscriptionConfiguration config)
        {
            if (config.EventFilterPattern.IsNullOrWhitespace())
                config.EventFilterPattern = null;

            if (config.LogStreamNamePrefix.IsNullOrWhitespace())
                config.LogStreamNamePrefix = null;

            if (config.LogStreamNames.SafeAny())
            {
                config.LogStreamNames = config.LogStreamNames.Where(sn => sn.HasValue()).ToList();
                if (!config.LogStreamNames.Any())
                    config.LogStreamNames = null;
            }

            config.ReadMaxBatchSize ??= 10000;
            config.MinIntervalSeconds ??= 15;
            config.MaxIntervalSeconds ??= 300;
            config.ClockSkewProtectionSeconds ??= 15;
            config.TargetTimeoutSeconds ??= 60;
            config.TargetMaxBatchSize ??= 1;
            
            config.ReadMaxBatchSize = Math.Max(config.ReadMaxBatchSize.Value, 1);
            config.ReadMaxBatchSize = Math.Min(config.ReadMaxBatchSize.Value, 10000);
            
            config.MinIntervalSeconds = Math.Max(config.MinIntervalSeconds.Value, 5);
            config.MinIntervalSeconds = Math.Min(config.MinIntervalSeconds.Value, 10 * 60);
            
            config.MaxIntervalSeconds = Math.Max(config.MaxIntervalSeconds.Value, config.MinIntervalSeconds.Value);
            config.MaxIntervalSeconds = Math.Min(config.MaxIntervalSeconds.Value, 8 * 60 * 60);

            config.ClockSkewProtectionSeconds = Math.Max(config.ClockSkewProtectionSeconds.Value, 5);
            config.ClockSkewProtectionSeconds = Math.Min(config.ClockSkewProtectionSeconds.Value, 120);
            
            config.TargetTimeoutSeconds = Math.Max(config.TargetTimeoutSeconds.Value, 1);
            config.TargetTimeoutSeconds = Math.Min(config.TargetTimeoutSeconds.Value, 15 * 60);

            config.TargetMaxBatchSize = Math.Max(config.TargetMaxBatchSize.Value, 1);
            config.TargetMaxBatchSize = Math.Min(config.TargetMaxBatchSize.Value, 20000);
            
            DependencyContext.RunnerContexts.Add(config.Id, BuildJobRunnerContext(config));
        }

        private static JobRunnerContext BuildJobRunnerContext(SubscriptionConfiguration config)
        {
            var result = new JobRunnerContext
            {
                Id = config.Id,
                EventFilterPattern = config.EventFilterPattern,
                TargetUrl = config.TargetUrl,
                AwsRegion = config.AwsRegion,
                LogGroupName = config.LogGroupName,
                LogStreamNames = config.LogStreamNames,
                LogStreamNamePrefix = config.LogStreamNamePrefix,
                ReadMaxBatchSize = config.ReadMaxBatchSize.GetValueOrDefault(),
                MinIntervalSeconds = config.MinIntervalSeconds.GetValueOrDefault(),
                MaxIntervalSeconds = config.MaxIntervalSeconds.GetValueOrDefault(),
                ClockSkewProtectionSeconds = config.ClockSkewProtectionSeconds.GetValueOrDefault(),
                TargetMaxBatchSize = config.TargetMaxBatchSize.GetValueOrDefault(),
                TargetSubscriptionData = config.TargetSubscriptionData,
                StartInstant = ParseAbsoluteOrRelativeTime(config.StartTimeIso, config.StartTimeSecondsAgo) ?? InstantUtils.Now,
                EndInstant = ParseAbsoluteOrRelativeTime(config.EndTimeIso, config.EndTimeSecondsAgo),
                Logger = Log.Logger.ForContext("SubscriptionId", config.Id),
                AwsClient = new AmazonCloudWatchLogsClient(RegionEndpoint.GetBySystemName(config.AwsRegion)),
                HttpClient = new HttpClient(),
                Random = new Random(),
                NextInputBatchSequence = 1
            };

            result.HttpClient.Timeout = TimeSpan.FromSeconds(config.TargetTimeoutSeconds.GetValueOrDefault());
            
            return result;
        }

        private static Instant? ParseAbsoluteOrRelativeTime(string absoluteTime, int? relativeTimeSecondsAgo)
        {
            if (!string.IsNullOrWhiteSpace(absoluteTime))
            {
                var parsedTime = InstantPattern.Parse(absoluteTime);
                if (!parsedTime.Success)
                    throw new ArgumentException($"Time pattern '{absoluteTime}' could not be parsed. ISO-8601 based UTC format without fractions of second is expected.");

                return parsedTime.Value;
            }

            if (relativeTimeSecondsAgo.HasValue)
            {
                return InstantUtils.SecondsAgo(relativeTimeSecondsAgo.Value);
            }

            return null;
        }
    }
}