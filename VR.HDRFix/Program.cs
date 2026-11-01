using System.Diagnostics;
using System.IO;
using System.Reflection;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Serilog;

using VR.HDRFix.Configs;

namespace VR.HDRFix;

public class Program
{
    public const string ServiceName = "HDRFixService";

    private const string FullTemplate = "{Timestamp:yyyy-MM-dd HH:mm:ss} [{Level:u3}] {Message:lj}{NewLine}{Exception}";

    public static void Main(string[] args)
    {
        var appName = Assembly.GetExecutingAssembly().GetName().Name;

        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
            .Build();

        bool enableLogging = configuration.GetValue("Settings:EnableLogging", true);

        var loggerConfig = new LoggerConfiguration()
            .ReadFrom.Configuration(configuration);

        if (enableLogging)
        {
            loggerConfig.WriteTo.File(
                path: Path.Combine(AppContext.BaseDirectory, "logs", $"{appName}_log.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7,
                outputTemplate: FullTemplate);
        }

        Log.Logger = loggerConfig.CreateLogger();

        LowerProcessPriority();

        try
        {
            Log.Information("Service is starting up...");
            CreateHostBuilder(args).Build().Run();
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Service crashed unexpectedly.");
            Environment.ExitCode = 1;
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }

    public static IHostBuilder CreateHostBuilder(string[] args)
    {
        return Host.CreateDefaultBuilder(args)
            .UseContentRoot(AppContext.BaseDirectory)
            .UseWindowsService(options => options.ServiceName = ServiceName)
            .UseSerilog()
            .ConfigureServices((hostContext, services) =>
            {
                services.Configure<Settings>(hostContext.Configuration.GetSection("Settings"));
                services.AddHostedService<HdrWorker>();
            });
    }

    /// <summary>Conversions are CPU heavy; never compete with the game that is being captured.</summary>
    private static void LowerProcessPriority()
    {
        try
        {
            using var process = Process.GetCurrentProcess();
            process.PriorityClass = ProcessPriorityClass.BelowNormal;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not lower process priority.");
        }
    }
}