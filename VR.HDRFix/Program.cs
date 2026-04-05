using System.IO;
using System.Reflection;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Serilog;

using VR.HDRFix.Configs;

namespace VR.HDRFix
{
    public class Program
    {
        private const string FullTemplate = "{Timestamp:yyyy-MM-dd HH:mm:ss} [{Level:u3}] {Message:lj}{NewLine}{Exception}";

        public static void Main(string[] args)
        {
            var appName = Assembly.GetExecutingAssembly().GetName().Name;

            var configuration = new ConfigurationBuilder()
                .SetBasePath(AppContext.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .Build();

            bool enableLogging = configuration.GetValue<bool>("Settings:EnableLogging");

            var loggerConfig = new LoggerConfiguration()
                .ReadFrom.Configuration(configuration);

            if (enableLogging)
                loggerConfig.WriteTo.File(
                    path: Path.Combine(AppContext.BaseDirectory, $"logs/{appName}_log.log"),
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 7,
                    outputTemplate: FullTemplate
                );

            Log.Logger = loggerConfig.CreateLogger();

            try
            {
                if (enableLogging)
                    Log.Information("Service is starting up...");

                CreateHostBuilder(args).Build().Run();
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "Service crashed unexpectedly.");
            }
            finally
            {
                Log.CloseAndFlush();
            }
        }

        public static IHostBuilder CreateHostBuilder(string[] args)
        {
            var builder = Host.CreateDefaultBuilder(args)
                .UseWindowsService(options =>
                {
                    options.ServiceName = "HDR to SDR Converter Service";
                })
                .UseSerilog()
                .ConfigureServices((hostContext, services) =>
                {
                    services.Configure<Settings>(hostContext.Configuration.GetSection("Settings"));
                    services.AddHostedService<HdrWorker>();
                });

            return builder;
        }
    }
}