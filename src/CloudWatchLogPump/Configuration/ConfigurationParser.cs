using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using CloudWatchLogPump.Extensions;
using Microsoft.Extensions.Configuration;
using NodaTime;
using NodaTime.Text;

namespace CloudWatchLogPump.Configuration
{
    public static class ConfigurationParser
    {
        private static readonly Regex ConfigurationIdRegex = new Regex("^[0-9a-zA-Z_\\-\\.]+$");
        private static readonly InstantPattern InstantPattern = InstantPattern.General;
        
        public static void LoadConfiguration(string configFilePath)
        {
            var builder = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile(configFilePath, false);

            var config = builder.Build();
            DependencyContext.Configuration = config.Get<RootConfiguration>();
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
            if (string.IsNullOrWhiteSpace(config.Id))
                throw new ArgumentException("Subscription ID is required for all subscriptions.");

            if (config.Id.Length > 100)
                throw new ArgumentException("Subscription ID cannot be longer than 100 characters.");

            if (!ConfigurationIdRegex.IsMatch(config.Id))
                throw new ArgumentException($"Subscription '{config.Id}' - Invalid characters in Subscription ID. Allowed characters are alpha numeric, dash, underscore and dot.");

            if (string.IsNullOrWhiteSpace(config.TargetUrl))
                throw new ArgumentException($"Subscription '{config.Id}' - targetUrl is required to be specified");

            if (string.IsNullOrWhiteSpace(config.LogGroupName))
                throw new ArgumentException($"Subscription '{config.Id}' - logGroupName is required to be specified");

            if (string.IsNullOrWhiteSpace(config.StartTimeIso) && config.StartTimeSecondsAgo.HasValue)
            {
                throw new ArgumentException($"Subscription '{config.Id}' - startTimeIso and startTimeSecondsAgo cannot be specified at the same time. Only one of them allowed to be present in the configuration.");
            }

            if (string.IsNullOrWhiteSpace(config.EndTimeIso) && config.EndTimeSecondsAgo.HasValue)
            {
                throw new ArgumentException($"Subscription '{config.Id}' - endTimeIso and endTimeSecondsAgo cannot be specified at the same time. Only one of them allowed to be present in the configuration.");
            }
        }

        public static void CoerceConfiguration()
        {
            DependencyContext.Subscriptions = new Dictionary<string, CalculatedSubscriptionConfiguration>();
            
            if (DependencyContext.Configuration.Subscriptions.Count > 0)
                DependencyContext.Configuration.Subscriptions.ForEach(CoerceSubscription);
        }

        private static void CoerceSubscription(SubscriptionConfiguration config)
        {
            if (string.IsNullOrWhiteSpace(config.FilterPattern))
                config.FilterPattern = null;

            if (string.IsNullOrWhiteSpace(config.LogStreamNamePrefix))
                config.LogStreamNamePrefix = null;

            if (config.LogStreamNames != null)
            {
                config.LogStreamNames = config.LogStreamNames.Where(sn => !string.IsNullOrWhiteSpace(sn)).ToList();
                if (!config.LogStreamNames.Any())
                    config.LogStreamNames = null;
            }

            config.MinIntervalSeconds = Math.Max(config.MinIntervalSeconds, 5);
            config.MinIntervalSeconds = Math.Min(config.MinIntervalSeconds, 10 * 60);
            
            config.MaxIntervalSeconds = Math.Max(config.MaxIntervalSeconds, config.MinIntervalSeconds);
            config.MaxIntervalSeconds = Math.Min(config.MaxIntervalSeconds, 8 * 60 * 60);

            config.TargetMaxBatchSize = Math.Max(config.TargetMaxBatchSize, 1);
            
            DependencyContext.Subscriptions.Add(config.Id, BuildCalculatedSubscriptionConfiguration(config));
        }

        private static CalculatedSubscriptionConfiguration BuildCalculatedSubscriptionConfiguration(SubscriptionConfiguration config)
        {
            var result = new CalculatedSubscriptionConfiguration
            {
                Id = config.Id,
                FilterPattern = config.FilterPattern,
                TargetUrl = config.TargetUrl,
                LogGroupName = config.LogGroupName,
                LogStreamNames = config.LogStreamNames,
                LogStreamNamePrefix = config.LogStreamNamePrefix,
                MinIntervalSeconds = config.MinIntervalSeconds,
                MaxIntervalSeconds = config.MaxIntervalSeconds,
                TargetMaxBatchSize = config.TargetMaxBatchSize,
                StartInstant = ParseAbsoluteOrRelativeTime(config.StartTimeIso, config.StartTimeSecondsAgo) ?? InstantUtils.Now,
                EndInstant = ParseAbsoluteOrRelativeTime(config.EndTimeIso, config.EndTimeSecondsAgo)
            };
            
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