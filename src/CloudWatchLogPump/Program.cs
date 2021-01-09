using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CloudWatchLogPump.Configuration;
using CloudWatchLogPump.Extensions;
using Microsoft.Extensions.Configuration;
using NodaTime;
using Serilog;
using Serilog.Events;
using Serilog.Sinks.SystemConsole.Themes;

namespace CloudWatchLogPump
{
    class Program
    {
        private static Regex _configurationIdRegex = new Regex("^[0-9a-zA-Z_\\-\\.]+$");
        
        static void Main(string[] args)
        {
            var configFilePath = "appsettings.json";
            if (args.Length > 0)
                configFilePath = args[0];
            
            SetupConfiguration(configFilePath);
            CoerceConfiguration(DependencyContext.Configuration);
            SetupLogging();
            
            Log.Logger.Information("Application started.");
            Log.Logger.Debug($"Configuration: {DependencyContext.Configuration.Dump()}");
            ValidateConfiguration(DependencyContext.Configuration);
            // TODO: Coerce start / end times

            SetupProgressDb().GetAwaiter().GetResult();
            SetupMonitor();
            DependencyContext.Monitor.StartAll();

            Console.ReadLine();
            DependencyContext.Monitor.StopAll();
        }
        
        private static void SetupConfiguration(string configFilePath)
        {
            var builder = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile(configFilePath, false);

            var config = builder.Build();
            DependencyContext.Configuration = config.Get<RootConfiguration>();
        }
        
        private static void SetupLogging()
        {
            var consoleLogLevel = DependencyContext.Configuration.EnableDebugConsoleLog
                ? LogEventLevel.Debug
                : LogEventLevel.Information;
            
            var maxSubscriptionIdLength = DependencyContext.Configuration.Subscriptions.Max(s => s.Id.Length);
            string logMessageTemplate = $"[{{Timestamp:HH:mm:ss}} {{Level:u3}} {{SubscriptionId,-{maxSubscriptionIdLength}}}] {{Message:lj}}{{NewLine}}{{Exception}}";

            var config = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.Console(consoleLogLevel, logMessageTemplate, theme: AnsiConsoleTheme.Code)
                .Destructure.ByTransforming<LocalDate>(ld => ld.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

            if (!string.IsNullOrWhiteSpace(DependencyContext.Configuration.LogFolderPath))
            {
                if (!Directory.Exists(DependencyContext.Configuration.LogFolderPath))
                    Directory.CreateDirectory(DependencyContext.Configuration.LogFolderPath);
                
                config = config.WriteTo.File(
                    Path.Combine(DependencyContext.Configuration.LogFolderPath, "info-.log"),
                    LogEventLevel.Information, 
                    rollingInterval: RollingInterval.Day);

                if (DependencyContext.Configuration.EnableDebugFileLog)
                {
                    config = config.WriteTo.File(
                        Path.Combine(DependencyContext.Configuration.LogFolderPath, "debug-.log"),
                        LogEventLevel.Debug, 
                        rollingInterval: RollingInterval.Day);
                }
            }
            
            var logger = config.CreateLogger();
            Log.Logger = logger;
        }

        private static void SetupMonitor()
        {
            DependencyContext.Monitor = new JobMonitor();
        }

        private static void CoerceConfiguration(RootConfiguration config)
        {
            if (config.Subscriptions.Count > 0)
                config.Subscriptions.ForEach(CoerceSubscription);
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
        }
        
        private static void ValidateConfiguration(RootConfiguration config)
        {
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

            if (!_configurationIdRegex.IsMatch(config.Id))
                throw new ArgumentException($"Subscription '{config.Id}' - Invalid characters in Subscription ID. Allowed characters are alpha numeric, dash, underscore and dot.");

            if (string.IsNullOrWhiteSpace(config.TargetUrl))
                throw new ArgumentException($"Subscription '{config.Id}' - targetUrl is required to be specified");

            if (string.IsNullOrWhiteSpace(config.LogGroupName))
                throw new ArgumentException($"Subscription '{config.Id}' - logGroupName is required to be specified");

            if (string.IsNullOrWhiteSpace(config.AbsoluteStartTimestamp)
                && string.IsNullOrWhiteSpace(config.RelativeStartTimestamp))
            {
                throw new ArgumentException($"Subscription '{config.Id}' - absoluteStartTimestamp and relativeStartTimestamp cannot be specified at the same time. Only one of them allowed to be present in the configuration.");
            }

            if (string.IsNullOrWhiteSpace(config.AbsoluteEndTimestamp)
                && string.IsNullOrWhiteSpace(config.RelativeEndTimestamp))
            {
                throw new ArgumentException($"Subscription '{config.Id}' - absoluteEndTimestamp and relativeEndTimestamp cannot be specified at the same time. Only one of them allowed to be present in the configuration.");
            }
        }

        private static async Task SetupProgressDb()
        {
            DependencyContext.ProgressDb = new ProgressDb(DependencyContext.Configuration.ProgressDbPath);
            await DependencyContext.ProgressDb.LoadAll(DependencyContext.Configuration.Subscriptions.Select(s => s.Id));
        }
    }
}
