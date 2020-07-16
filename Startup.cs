using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpsPolicy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;


namespace MyAPI
{
    public class Startup
    {
        private State state = State.Running;

        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddControllers();
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env, IHostApplicationLifetime hostApplicationLifetime)
        {
            Log($"Application starting. Process ID: {Process.GetCurrentProcess().Id}");

            // Triggered when the application host is performing a graceful shutdown. Requests may still be in flight. Shutdown will block until this event completes.
            hostApplicationLifetime.ApplicationStopping.Register(ApplicationRequestsIncomingAfterStopRequest);

            // Triggered when the application host is performing a graceful shutdown. All requests should be complete at this point. Shutdown will block until this event completes.
            hostApplicationLifetime.ApplicationStopped.Register(ApplicationRequestsAreCompleted);

            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            app.UseHttpsRedirection();

            //app.UseRouting();

            //app.UseAuthorization();

            //app.UseEndpoints(endpoints =>
            //{
            //    endpoints.MapControllers();
            //});


            app.Run(async (context) =>
            {
                var message = $"Host: {Environment.MachineName}, State: {state}";

                //if (!context.Request.Path.Value.Contains("/favicon.ico"))

                Log($"Incoming request at {context.Request.Path}, {message}");


                if (context.Request.Path.Value.Contains("slow"))
                {
                    await SleepAndPrintForSeconds(10);
                }
                else
                {
                    await Task.Delay(100);
                }

                await context.Response.WriteAsync(message);
            });
        }

        private void ApplicationRequestsIncomingAfterStopRequest()
        {
            state = State.AfterSigterm;

            //did we receive more incoming requests after TERM?
            Log("# this app is stopping. there may be incoming requests left");
            Thread.Sleep(25000);

        }

        private void ApplicationRequestsAreCompleted()
        {

            Log("# this app has stopped. all requests completed");
        }

        private void Log(string msg) => Console.WriteLine($"{DateTime.UtcNow}: {msg}");

        private async Task SleepAndPrintForSeconds(int seconds)
        {
            do
            {
                Log($"Sleeping ({seconds} seconds left)");
                await Task.Delay(1000);
            } while (--seconds > 0);
        }
    }
}
