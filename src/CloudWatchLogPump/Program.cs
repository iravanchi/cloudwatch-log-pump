using System;
using System.Globalization;
using System.IO;
using System.Linq;
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
        static void Main(string[] args)
        {
            var configFilePath = "appsettings.json";
            if (args.Length > 0)
                configFilePath = args[0];
            
            SetupConfiguration(configFilePath);
            SetupLogging();
            
            Log.Logger.Information("Application started.");
            Log.Logger.Debug($"Configuration: {DependencyContext.Configuration.Dump()}");
            ValidateConfiguration();

            SetupProgressDb();
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
        
        private static void ValidateConfiguration()
        {
            // Check minimum one iteration
            // Check iteration start/stop specified in one format only
            // Check iteration ID for invalid characters
            
            throw new NotImplementedException();
        }

        private static void SetupProgressDb()
        {
            DependencyContext.ProgressDb = new ProgressDb();
            DependencyContext.ProgressDb.LoadAll(DependencyContext.Configuration.Subscriptions.Select(s => s.Id));
        }
    }
}
