using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;


namespace MyAPI
{
    public class Startup
    {
        private State state = State.Running;
        private static int total = 0;

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
                Interlocked.Increment(ref total);
                Log($"Incoming request {total} at {context.Request.Path}, {message}");

                await DoSomeWork(context);

                await context.Response.WriteAsync(message);
            });
        }

        private async Task DoSomeWork(HttpContext context)
        {
            if (context.Request.Path.Value.Contains("slow"))
            {
                await SleepAndPrintForSeconds(2);
            }
            else
            {
                await Task.Delay(500);
            }
        }

        private void ApplicationRequestsIncomingAfterStopRequest()
        {
            state = State.AfterSigterm;

            //did we receive more incoming requests after TERM?
            Log("# this app is stopping. wating 20 seconds for in-flight requests");
            // while endpoints in kubernetes loadbalancer may take around 10 seconds to get updated
            // we can sleep the app for 20 seconds to respond requests
            Thread.Sleep(20000);
            // the default grace period is 30 seconds for termination of app process

        }

        private void ApplicationRequestsAreCompleted()
        {
            // in a graceful shutdown this is shown. 
            // otherwise operation cancelled triggers in Main, 
            // and all requests may have completed even after host is cancelled
            Log($"# this app has stopped. all requests completed. latest {total}");
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
