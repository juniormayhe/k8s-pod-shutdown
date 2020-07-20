using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MyAPI
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            var host = Host.CreateDefaultBuilder(args)
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    Console.WriteLine("# app started");
                    ////webBuilder.UseShutdownTimeout(TimeSpan.FromSeconds(30)); // default is 5

                    webBuilder.UseStartup<Startup>();
                });
            try
            {
                await host.Build().RunAsync();
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine("# app was cancelled");
                // suppress
            }
        }
    }
}
