using Biden.Radar.Common.Telegrams;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Quartz;

namespace Biden.Radar.OKX
{
    internal class Program
    {
        public static void Main(string[] args)
        {
            CreateHostBuilder(args).Build().Run();
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
        Host.CreateDefaultBuilder(args)
            .ConfigureAppConfiguration((context, config) =>
            {
                config.SetBasePath(Directory.GetCurrentDirectory());
                config.AddJsonFile("appsettings.json", optional: true, true);
                config.AddEnvironmentVariables();
                config.AddCommandLine(args);
            })
            .ConfigureServices((hostContext, services) =>
            {
                var builder = new ConfigurationBuilder().SetBasePath(Directory.GetCurrentDirectory())
                                    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);
                IConfiguration configuration = builder.Build();
                
                services.AddSingleton<ITeleMessage, TeleMessage>();
                services.AddHostedService<AutoRunService>();
                services.AddQuartz(q =>
                {
                    var jobKey = new JobKey("TokenChangesJob");
                    q.AddJob<TokenChangesJob>(opts => opts.WithIdentity(jobKey));

                    q.AddTrigger(opts => opts
                        .ForJob(jobKey)
                        .WithIdentity("TokenChangesJob-trigger")
                        .WithCronSchedule("0 0/5 * * * ?"));

                });
                services.AddQuartzHostedService(q => q.WaitForJobsToComplete = true);
            });
    }    
}
