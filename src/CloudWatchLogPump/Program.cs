using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CloudWatchLogPump.Configuration;
using CloudWatchLogPump.Extensions;
using NodaTime;
using NodaTime.Text;
using Serilog;
using Serilog.Events;
using Serilog.Sinks.SystemConsole.Themes;

namespace CloudWatchLogPump
{
    class Program
    {
        static void Main(string[] args)
        {
            var configFilePath = "appsettings.json";
            if (args.Length > 0)
                configFilePath = args[0];
            
            ConfigurationParser.LoadConfiguration(configFilePath);
            ConfigurationParser.FlattenSubscriptions();
            ConfigurationParser.ExpandSubscriptionPatterns();
            
            SetupLogging();
            Log.Logger.Information("Application started.");
            Log.Logger.Debug("Loaded configuration: \n{ConfigurationDump}", DependencyContext.Configuration.Dump());
            
            ConfigurationParser.ValidateConfiguration();
            ConfigurationParser.CoerceConfiguration();

            SetupProgressDb().GetAwaiter().GetResult();
            SetupMonitor();
            DependencyContext.Monitor.StartAll();

            Console.ReadLine();
            DependencyContext.Monitor.StopAll();
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
                    rollingInterval: RollingInterval.Day,
                    outputTemplate: logMessageTemplate);

                if (DependencyContext.Configuration.EnableDebugFileLog)
                {
                    config = config.WriteTo.File(
                        Path.Combine(DependencyContext.Configuration.LogFolderPath, "debug-.log"),
                        LogEventLevel.Debug, 
                        rollingInterval: RollingInterval.Day,
                        outputTemplate: logMessageTemplate);
                }
            }

            config = config
                .Destructure.ByTransforming<LocalDate>(ld => ld.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
                .Destructure.ByTransforming<Instant>(i => InstantPattern.General.Format(i))
                .Destructure.ByTransforming<Duration>(d => DurationPattern.Roundtrip.Format(d));

            var logger = config.CreateLogger();
            Log.Logger = logger;
        }

        private static void SetupMonitor()
        {
            DependencyContext.Monitor = new JobMonitor();
        }

        private static async Task SetupProgressDb()
        {
            DependencyContext.ProgressDb = new ProgressDb(DependencyContext.Configuration.ProgressDbPath);
            await DependencyContext.ProgressDb.LoadAll(DependencyContext.Configuration.Subscriptions.Select(s => s.Id));
        }
    }
}
