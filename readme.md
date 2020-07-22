# Kubernetes pod shutdown evaluation

What happens with a netcore application when Kubernetes sends a signal to terminate the pod?

Here some findings related to scaling down containerized netcore applications in a Kubernetes cluster. 

## Evaluated termination signals

First thing first, let’s review how termination [signals](https://pracucci.com/graceful-shutdown-of-kubernetes-pods.html) work:

How termination works

- A SIGTERM signal is sent to the main process (PID 1) in each container, and a “grace period” countdown starts defaults to 30 seconds, but you can extend this time by adding a setting it in Kubernetes deployment file.
- Upon the reception of the SIGTERM, each container should start a graceful shutdown of the running application and exit.
- If a container doesn’t terminate within the grace period, a SIGKILL signal will be sent and the container violently terminated.

The net core application can gradually shutdown when SIGTERM or SIGINT are received from its container.

The signal codes evaluated in this investigation are:

| Signal name  | Signal value  | Effect |
|---|---|---|
| SIGINT  | 2  | Kubernetes says “could you please stop what you are doing?” and sends an interrupt from keyboard message (aka CTRL + C). The netcore application has a chance to do some cleanup and graceful shutdown.  |
| SIGKILL | 9  | Kubernetes lost its patience and shut down the container and interrupts whatever its netcore app is doing.  |
| SIGTERM | 15 | Kubernetes asks for termination of the process but gives a chance for a cleanup and a graceful netcore application shutdown.   |

## Preparing the evaluation

To evaluate the netcore application shutdown we used the following tools:

- [bombardier](https://github.com/codesenberg/bombardier) - for sending requests and testing load balancer
- [postman](https://www.postman.com/downloads/) - for sending requests
- [docker for windows](https://docs.docker.com/docker-for-windows/install/) - for enabling a local Kubernetes environment with kubectl
- containerized net core 3.1 app
- Kubernetes [declarative management files](https://kubernetes.io/docs/tasks/manage-kubernetes-objects/declarative-config/) - for creating cluster

## Scenarios
| Scenario | Result |
|---|---|
| Scale down on default cluster and netcore app with default configurations | The netcore app responds to all requests |
| Scale down on default cluster and netcore app with thread sleep on application stopping event | The netcore app responds to all requests |
| Scale down on default cluster and netcore app with increased timeout| The netcore app responds to all requests |
| Scale down on cluster with extended termination grace and default netcore app | The netcore app responds to all requests |


## Run the image with Visual Studio

To run the solution in local docker

- Select "Docker" in dropdown menu and run the application with Start Debugging (F5) 
- browse the application with postman or your browser http://localhost:<dynamic port>

## Evidences

### Boilerplates
Here are the sample files used in the evaluation

Default cluster: k8s-deployment-myapi.yaml
```yaml
apiVersion: apps/v1
kind: Deployment
metadata:
  name: mydeployment
spec:
  replicas: 2
  selector:
      matchLabels:
        name: mykubapp
  template:
    metadata:
      labels:
        name: mykubapp
    spec:
      containers:
        - name: myapi
          image: juniormayhe/myapi:1
          ports:
            - containerPort: 80
---
apiVersion: v1
kind: Service
metadata:
    name: mykubapp
spec:
  ports:  
    - protocol: TCP
      port: 8080
      targetPort: 80
  selector:
    name: mykubapp
  type: LoadBalancer
```

Cluster with extended grace period for shutting down container: k8s-deployment-myapi-grace.yaml
```yaml
apiVersion: apps/v1
kind: Deployment
metadata:
  name: mydeployment
spec:
  replicas: 2
  selector:
      matchLabels:
        name: mykubapp
  template:
    metadata:
      labels:
        name: mykubapp
    spec:
      terminationGracePeriodSeconds: 60
      containers:
        - name: myapi
          # image with UseShutdownTimeout 30s 
          image: juniormayhe/myapi:3
          ports:
            - containerPort: 80          
---
apiVersion: v1
kind: Service
metadata:
    name: mykubapp
spec:
  ports:  
    - protocol: TCP
      port: 8080
      targetPort: 80
  selector:
    name: mykubapp
  type: LoadBalancer
```

Containerized netcore application
```csharp
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
 
            app.UseHttpsRedirection();
 
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
 
            // we enter here when SIGTERM or SIGINT has been sent by Kubernetes
            Log("# this app is stopping. wating 20 seconds for in-flight requests");

            // the default shutdown timeout is 5 seconds for netcore app
            // the default termination grace period is 30 seconds for kubernetes
            // kubernetes loadbalancer may take around 10 seconds to get updated
            // we can sleep the app for 20 seconds to respond requests giving loadbalancer enough time
            Thread.Sleep(20000);
        }
 
        private void ApplicationRequestsAreCompleted()
        {
            // in a graceful shutdown this is shown. 
            // otherwise operation canceled triggers in Main, 
            // and all requests may have completed even after host is canceled
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
```

Dockerfile
```
FROM mcr.microsoft.com/dotnet/core/aspnet:3.1-buster-slim AS base
WORKDIR /app
EXPOSE 80
EXPOSE 443
 
FROM mcr.microsoft.com/dotnet/core/sdk:3.1-buster AS build
WORKDIR /src
COPY ["MyAPI.csproj", ""]
RUN dotnet restore "./MyAPI.csproj"
COPY . .
WORKDIR "/src/."
RUN dotnet build "MyAPI.csproj" -c Release -o /app/build
 
FROM build AS publish
RUN dotnet publish "MyAPI.csproj" -c Release -o /app/publish
 
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
# MANDATORY if node app or other PID 1 app cannot be interrupted by SIGINT
# STOPSIGNAL SIGINT 
ENTRYPOINT ["dotnet", "MyAPI.dll"]
```

### Create an image for netcore app

To build an image with Dockerfile to be used in your pods
```
docker build -t juniormayhe/myapi:1 .
```

to check if image was created
```
docker images
```

to test your app startup from image http://localhost:32725
```
docker run -d -p 32725:80 --name teste juniormayhe/myapi:1
```

to delete the test container
```
docker stop teste
docker rm teste
```

### Available kill signal commands to terminate process within container
You can inspect logs in docker while you trigger a SIGTERM

```
docker exec -it <container id> sh
```

install procps
```
apt-get update && apt-get install -y procps
```

to make a graceful shutdown on the app
```
kill -s 15 $(pidof dotnet)
```

## Evaluating app shutdown on an independent pod deletion

For the case where there are two pods created without a deployment, 
behind the same load balancer, the pods will keep different names and 
shutdown is done manually with kubectl delete. 

We can only scale up or down at a top level object like a deployment 
to affect the replicaset and cascade to the pod.

But for the sake of testing of kubectl delete, we have created a cluster
with two independent pods to evaluate how app behaves after the pod deletion.

### Create the K8S cluster

- Create a kubernetes cluster with two pods
```
 kubectl apply -f k8s-myapi.yaml
```

if you did something wrong with port or image name within yaml, delete to start over with a new apply
```
kubectl delete -f k8s-myapi.yaml
kubectl apply -f k8s-myapi.yaml
```

- Check if loadbalancer and pods were created 
```
kubectl get all
```

### Test the application shutdown
- open the logs of both container pods in separate windows
terminal window 1
```
kubectl logs -f pod/myapp-pod1
```

terminal window 2
```
kubectl logs -f pod/myapp-pod2
```

- open a terminal for deleting the pod to evaluate the application shutdown in logs
```
kubectl delete pod/myapp-pod1
```

to readd the deleted pod, apply the yaml
```
 kubectl apply -f k8s-myapi.yaml
```

- open postman
in postman open two runners with 1000 tries and trigger the execution, 
then observe the logs and fire the kubectl delete to evaluate app shutdown behaviour

### First impressions

Requests continue to arrive to the pod even after a deletion has been requested by kubernetes.
In host lifecycle we can wait 25 seconds and the requests keep coming until the pod is completely terminated.
ApplicationStopped only shows up if the application has exited normally.

```
07/16/2020 16:42:18: Incoming request at /, Host: myapp-pod1, State: Running
07/16/2020 16:42:18: Incoming request at /, Host: myapp-pod1, State: Running
07/16/2020 16:42:18: # this app is stopping. there may be incoming requests left
07/16/2020 16:42:18: Incoming request at /, Host: myapp-pod1, State: AfterSigterm
07/16/2020 16:42:18: Incoming request at /, Host: myapp-pod1, State: AfterSigterm
07/16/2020 16:42:19: Incoming request at /, Host: myapp-pod1, State: AfterSigterm
...
07/16/2020 16:42:43: Incoming request at /, Host: myapp-pod1, State: AfterSigterm
07/16/2020 16:42:43: Incoming request at /, Host: myapp-pod1, State: AfterSigterm
info: Microsoft.Hosting.Lifetime[0]
      Application is shutting down...

```
In program, the UseShutdownTimeout, (default to wait for 5 seconds) seems to have no effect when we increase to 30 seconds.
The host application has ended without following this wait time?


### Evaluating app shutdown on a scale down of deployment

For the case where there are two pods created with a deployment, 
behind the same load balancer, the deployment will manage the created 
containers in pods. We can perform a scale down from 2 pods to 1
without needing to specify which pod name must be deleted. 

delete the previous cluster with independents pods
```
kubectl delete -f k8s-myapi.yaml
```

start the new cluster with deployment to automatically create 2 pods, as defined in yaml specs section.
```
kubectl apply -f k8s-deployment-myapi.yaml  
```

- Check if loadbalancer and pods were created 
```
kubectl get all
```

### Test the application shutdown with scale down

list the pods to get their names
```
kubectl get pods
```
- open the logs of both container pods in separate windows
terminal window 1
```
kubectl logs -f pod/mydeployment-<random id of first pod>
```

terminal window 2
```
kubectl logs -f pod/mydeployment-<random id of second pod>
```

- open a terminal for scaling down the pods from 2 to 1 to evaluate the application shutdown in logs
```
kubectl scale --replicas=1 deployment mydeployment
```

```
to scale back to 2 pods, apply the yaml
```
kubectl apply -f k8s-deployment-myapi.yaml
```

- open postman
in postman open two runners with 1000 tries and trigger the execution, 
then observe the logs and fire the kubectl scale to evaluate app shutdown behaviour


### First impressions

The same behaviour happens, when we scale down 1 of the pods, the app gives some time to finish but the 
ApplicationStopped message was never shown because the app was terminated suddenly.
```
07/17/2020 16:57:10: Incoming request at /, Host: mydeployment-5957948974-gbpj8, State: Running
07/17/2020 16:57:10: Incoming request at /, Host: mydeployment-5957948974-gbpj8, State: Running
07/17/2020 16:57:10: # this app is stopping. there may be incoming requests left
07/17/2020 16:57:10: Incoming request at /, Host: mydeployment-5957948974-gbpj8, State: AfterSigterm
07/17/2020 16:57:10: Incoming request at /, Host: mydeployment-5957948974-gbpj8, State: AfterSigterm
...
07/17/2020 16:57:35: Incoming request at /, Host: mydeployment-5957948974-gbpj8, State: AfterSigterm
07/17/2020 16:57:35: Incoming request at /, Host: mydeployment-5957948974-gbpj8, State: AfterSigterm
07/17/2020 16:57:35: Incoming request at /, Host: mydeployment-5957948974-gbpj8, State: AfterSigterm
info: Microsoft.Hosting.Lifetime[0]
      Application is shutting down...
```

Instead of postman runner we also did another try with bombardier (125 connections, 1 million requests) 
and get the same results where application has not ended gracefully. 
```
bombardier -c 125 -n 10000000 http://localhost:8080
```

```
07/20/2020 08:42:28: Incoming request at /, Host: mydeployment-5957948974-gff68, State: Running
07/20/2020 08:42:28: Incoming request at /, Host: mydeployment-5957948974-gff68, State: Running
07/20/2020 08:42:28: # this app is stopping. there may be incoming requests left
07/20/2020 08:42:29: Incoming request at /, Host: mydeployment-5957948974-gff68, State: AfterSigterm
...
07/20/2020 08:42:53: Incoming request at /, Host: mydeployment-5957948974-gff68, State: AfterSigterm
info: Microsoft.Hosting.Lifetime[0]
      Application is shutting down...
07/20/2020 08:42:54: Incoming request at /, Host: mydeployment-5957948974-gff68, State: AfterSigterm
07/20/2020 08:42:54: Incoming request at /, Host: mydeployment-5957948974-gff68, State: AfterSigterm
07/20/2020 08:42:54: Incoming request at /, Host: mydeployment-5957948974-gff68, State: AfterSigterm
07/20/2020 08:42:54: Incoming request at /, Host: mydeployment-5957948974-gff68, State: AfterSigterm
07/20/2020 08:42:54: Incoming request at /, Host: mydeployment-5957948974-gff68, State: AfterSigterm
rpc error: code = Unknown desc = Error: No such container: e967a964fe30af6cc90964f8c3e6d245c91cba89c21e04eff17c84ea198a653b
```

### Does UseShutdownTimeout have any effect?
build a new image with UseShutdownTimeout in Program.cs

```
docker build -t juniormayhe/myapi:2 .
```

delete previous deployment and wait 30 seconds
```
kubectl delete -f k8s-deployment-myapi.yaml
```

create a new deployment for new image juniormayhe/myapi:2
```
kubectl apply -f k8s-deployment-myapi-timeout.yaml
```

copy the new pods ids
```
kubectl get all
```

run in different terminals each pod´s app 
terminal window 1
```
kubectl logs -f pod/mydeployment-<random id of first pod>
```

terminal window 2
```
kubectl logs -f pod/mydeployment-<random id of second pod>
```

### First impressions
The UseShutdownTimeout has no effect. 25 seconds later the signal, the app got interrupted.
the 30 seconds for timeout did not have any effect to increase time for app to respond requests.
```
07/20/2020 11:25:43: Incoming request at /, Host: mydeployment-7587654d6c-kpv8l, State: Running
07/20/2020 11:25:43: # this app is stopping. there may be incoming requests left
07/20/2020 11:25:43: Incoming request at /, Host: mydeployment-7587654d6c-kpv8l, State: AfterSigterm
...
07/20/2020 11:26:08: Incoming request at /, Host: mydeployment-7587654d6c-kpv8l, State: AfterSigterm
info: Microsoft.Hosting.Lifetime[0]
      Application is shutting down...
```

### Is signint being passed to app?

Is docker entrypoint receiving the signint signal to gracefully terminate the app?

Our process has PID 1, after sigterm literally nothing would happen until Docker reaches timeout 
and sends a SIGKILL to the entrypoint.

```
winpty kubectl exec -it mydeployment-7587654d6c-5qm84 sh
ps aux
USER       PID %CPU %MEM    VSZ   RSS TTY      STAT START   TIME COMMAND
root         1  0.0  2.3 12007896 48008 ?      Ssl  11:35   0:00 dotnet MyAPI.dll
root       595  0.0  0.0   2388   756 pts/0    Ss   11:53   0:00 sh
root       943  0.0  0.1   7640  2692 pts/0    R+   11:54   0:00 ps aux
```

Let´s avoid sigint being bounced by adding to dockerfile 

```
STOPSIGNAL SIGINT
```
and rebuild a new image:
```
docker build -t juniormayhe/myapi:3 .
```

### First impressions
The app seems to receive sigint signal and gets interrupted after 25 seconds
```
07/20/2020 13:20:17: Incoming request at /, Host: mydeployment-54f9fff75b-27p2q, State: Running
07/20/2020 13:20:17: # this app is stopping. there may be incoming requests left
07/20/2020 13:20:17: Incoming request at /, Host: mydeployment-54f9fff75b-27p2q, State: AfterSigterm
...
07/20/2020 13:20:42: Incoming request at /, Host: mydeployment-54f9fff75b-27p2q, State: AfterSigterm
info: Microsoft.Hosting.Lifetime[0]
      Application is shutting down...
Unhandled exception. System.OperationCanceledException: The operation was canceled.
   at System.Threading.CancellationToken.ThrowOperationCanceledException()
   at Microsoft.Extensions.Hosting.Internal.Host.StopAsync(CancellationToken cancellationToken)
   at Microsoft.Extensions.Hosting.HostingAbstractionsHostExtensions.WaitForShutdownAsync(IHost host, CancellationToken token)
   at Microsoft.Extensions.Hosting.HostingAbstractionsHostExtensions.RunAsync(IHost host, CancellationToken token)
   at Microsoft.Extensions.Hosting.HostingAbstractionsHostExtensions.RunAsync(IHost host, CancellationToken token)
   at Microsoft.Extensions.Hosting.HostingAbstractionsHostExtensions.Run(IHost host)
   at MyAPI.Program.Main(String[] args) in /src/Program.cs:line 17
rpc error: code = Unknown desc = Error: No such container: e82a05372a7546c6f2117eb6528f861eb0c161aedc905ac8b4ff0583481a85f5
```
Must we implement anything to handle Host StopAsync?

### Must we give some extra time for app to finish by changing Kubernetes yaml file? 
In deployment we have added a terminal grace period to extend the wait time before kubernetes shutdown the app.
```
  template:
    metadata:
      labels:
        name: mykubapp
    spec:
      terminationGracePeriodSeconds: 60
```

delete previous deployment
```
kubectl delete -f k8s-deployment-myapi-sigint.yaml
```

Apply the extended grace time to 60 seconds
```
kubectl apply -f k8s-deployment-myapi-grace.yaml
```
Get pods name and tail the logs.

Start bombardier again, scale down and monitor the logs.
```
kubectl scale --replicas=1 deployment mydeployment
```

At this moment, Kubernetes sends a SIGTERM to one of the containers, which passes the signal to the netcore application, which by its turn triggers the ApplicationStopping to wait the number of seconds defined in Thread.Sleep, if implemented. After the SIGTERM, remaining requests were processed. 

In the following evidence, the Thread.Sleep was set to 25 seconds, and an operation canceled shows up indicating the process was ended. 

```
07/20/2020 13:42:15: Incoming request at /, Host: mydeployment-7ccbdff885-mkf54, State: Running
07/20/2020 13:42:15: # this app is stopping. there may be incoming requests left
07/20/2020 13:42:15: Incoming request at /, Host: mydeployment-7ccbdff885-mkf54, State: AfterSigterm
...
07/20/2020 13:42:40: Incoming request at /, Host: mydeployment-7ccbdff885-mkf54, State: AfterSigterm
info: Microsoft.Hosting.Lifetime[0]
      Application is shutting down...
07/20/2020 13:42:40: Incoming request at /, Host: mydeployment-7ccbdff885-mkf54, State: AfterSigterm
...
07/20/2020 13:42:41: Incoming request at /, Host: mydeployment-7ccbdff885-mkf54, State: AfterSigterm
07/20/2020 13:42:41: # app was cancelled
```

### Checking if all requests are being responded
Let´s count the requests to check if all requests were satisfied.

While running both bombardier and postman all requests were fulfilled:

- 200 requested by postman, status 200 ok
- 1000 requested by bombardier, status 200 ok

```
Bombarding http://localhost:8080 with 1000 request(s) using 10 connection(s)
 1000 / 1000 [===================================================================================================================================================] 100.00% 95/s 10s
Done!
Statistics        Avg      Stdev        Max
  Reqs/sec        96.96      58.06     307.48
  Latency      102.91ms     5.13ms   173.69ms
  HTTP codes:
    1xx - 0, 2xx - 1000, 3xx - 0, 4xx - 0, 5xx - 0
    others - 0
  Throughput:    21.19KB/s
```

We also did the same test for the scenario where an image has no Thread.Sleep implemented. All requests were again responded correctly. 

In all scenarios, we never see the message from ApplicationStopped event because the netcore app process gets canceled after all requests have been fulfilled, which seems to be the normal behavior for cancellation.

## Conclusions

The netcore [IWebHostBuilder.UseShutdownTimeout](https://docs.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.hosting.hostingabstractionswebhostbuilderextensions.useshutdowntimeout?view=aspnetcore-3.1) has no effect because the higher object in Kubernetes has priority on shutdown policy, so the app cannot extend its own grace period because it must obey to the Kubernetes deployment specification, defined in Kubernetes declarative yaml.

There would be an edge case where the netcore application receives a SIGTERM (shutdown request) but the load balancer did not get updated yet, so the pod container keeps receiving in-flight requests.

We were unable to reproduce this update slowness in the load balancer, because all requests were responded were all answered within the expected time status code 200 OK after the scale down.

To get around this possible edge case with Kubernetes load balancer update slowness, there is a [suggestion](https://blog.markvincze.com/graceful-termination-in-kubernetes-with-asp-net-core/) to add a Thread.Sleep in ApplicationStopping event to give the load balancer time to update itself while the application responds to requests. The ApplicationStopping is a host lifetime that can be defined on netcore application startup.

We tested the scale down with two images, one with and another without this suspension in the host lifetime thread and the requests were all satisfied. We even create a netcore application image that responds in 500ms response time which is the average response time of Routing Service and all requests were again responded correctly after the scale down.

Kubernetes gives a default of [30 seconds](https://kubernetes.io/docs/concepts/workloads/pods/pod/#termination-of-pods) for the container to shut down. Within 30 seconds, in around 10 seconds it updates the Load Balancer. We could not find if 10 seconds is related to a health check interval done with a [default readiness probe](https://github.com/kubernetes/kubernetes/blob/master/pkg/apis/core/v1/defaults_test.go#L70) interval, set by periodSeconds.

If the application has some final processing to do, we extend the time in ApplicationStopping event to deal with pending tasks. Or we can avoid this implementation and extend the time via Kubernetes declarative yaml with the setting terminationGracePeriodSeconds: 60 so the application has more time to do some final processing within this time limit.

## References
- https://kubernetes.io/docs/reference/kubectl/cheatsheet/
- https://github.com/juniormayhe/Scripts/tree/master/docker
- https://github.com/juniormayhe/Scripts/tree/master/kubernetes
- https://docs.microsoft.com/en-us/aspnet/core/fundamentals/middleware/?view=aspnetcore-3.1
- https://linux.die.net/Bash-Beginners-Guide/sect_12_01.html
- https://docs.microsoft.com/en-us/dotnet/architecture/containerized-lifecycle/design-develop-containerized-apps/build-aspnet-core-applications-linux-containers-aks-kubernetes
