using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace VR.HDRFix
{
    internal class Program
    {
        public static void Main(string[] args)
        {
            CreateHostBuilder(args).Build().Run();
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .UseWindowsService(options =>
                {
                    options.ServiceName = "HDR to SDR Converter Service";
                })
                .ConfigureServices((hostContext, services) =>
                {
                    // Реєструємо конфігурацію (це дасть змогу юзати IOptionsMonitor)
                    services.Configure<HdrFixOptions>(hostContext.Configuration.GetSection("HdrFix"));

                    // Реєструємо наш Worker
                    services.AddHostedService<HdrWorker>();
                });
    }
}