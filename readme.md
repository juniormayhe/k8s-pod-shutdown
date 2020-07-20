

build
# Kubernetes pod shutdown evaluation
What happens with a netcore application when Kubernetes sends a signal to terminate the pod?

How termination works

- A SIGTERM signal is sent to the main process (PID 1) in each container, and a “grace period” countdown starts (defaults to 30 seconds - see below to change it).
- Upon the receival of the SIGTERM, each container should start a graceful shutdown of the running application and exit.
- If a container doesn’t terminate within the grace period, a SIGKILL signal will be sent and the container violently terminated.

Ref: https://pracucci.com/graceful-shutdown-of-kubernetes-pods.html

## Run the image with Visual Studio

- Select "Docker" in dropdown menu and run the application with Start Debugging (F5) 
- browse the application with postman or your browser http://localhost:<dynamic port>

## Create an image for Kubernetes

- Build an image with Dockerfile to be used in your pods
```
docker build -t juniormayhe/myapi:1 .
```

- Check if image was created
```
docker images
```

- Test your image http://localhost:32725
```
docker run -d -p 32725:80 --name teste juniormayhe/myapi:1
```

Delete the test container
```
docker stop teste
docker rm teste
```

## Available kill signal commands to terminate process within container
You can inspect logs in docker while you trigger a graceful shutdown with

```
kill -s 15 $(pidof dotnet)
```

Other signals:

```
Signal name			Signal value	Effect
SIGHUP				1				Hangup
SIGINT				2				Interrupt from keyboard
SIGKILL				9				Kill signal
SIGTERM				15				Termination signal
SIGSTOP				17,19,23		Stop the process
```
Signal 9 kills the process and will not wait for it to end.

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
ApplicationStopped should have been processed to ensure that all requests were fulfilled, but it did not happen.

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


## Evaluating app shutdown on a scale down of deployment

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

## Is signint being passed to app?

Is docker entrypoint receiving the signint signal to gracefully terminate the app?

Our process has PID 1, after sigterm literally nothing would happen until Docker reaches timeout 
and sends a SIGKILL to the entrypoint.

```
winpty kubectl exec -it mydeployment-7587654d6c-5qm84 sh
# ps aux
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

From sigterm to app shut down, again we have 25 seconds. 1 second later after shutdown, we still received requests.
The app seems to be stopped before reaching the maxiumum of 60 seconds grace period.
The shutdown again is not graceful and the application still had unfinished requests to handle.
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
Unhandled exception. System.OperationCanceledException: The operation was canceled.
   at System.Threading.CancellationToken.ThrowOperationCanceledException()
   at Microsoft.Extensions.Hosting.Internal.Host.StopAsync(CancellationToken cancellationToken)
   at Microsoft.Extensions.Hosting.HostingAbstractionsHostExtensions.WaitForShutdownAsync(IHost host, CancellationToken token)
   at Microsoft.Extensions.Hosting.HostingAbstractionsHostExtensions.RunAsync(IHost host, CancellationToken token)
   at Microsoft.Extensions.Hosting.HostingAbstractionsHostExtensions.RunAsync(IHost host, CancellationToken token)
   at Microsoft.Extensions.Hosting.HostingAbstractionsHostExtensions.Run(IHost host)
   at MyAPI.Program.Main(String[] args) in /src/Program.cs:line 17
rpc error: code = Unknown desc = Error: No such container: 14bc6403a62980cc93991e12668f63421636f942315f00c60a80384c04a409dc
```

### Checking if all requests are being responded
Let´s count the requests to check if all requests were satisfied.

While running both bombardier and postman all requests were fulfilled:

- 200 requested by postman, status 200 ok
- 1000 requested by bombardier, status 200 ok

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

## References
- https://kubernetes.io/docs/reference/kubectl/cheatsheet/
- https://github.com/juniormayhe/Scripts/tree/master/docker
- https://github.com/juniormayhe/Scripts/tree/master/kubernetes
- https://docs.microsoft.com/en-us/aspnet/core/fundamentals/middleware/?view=aspnetcore-3.1
- https://linux.die.net/Bash-Beginners-Guide/sect_12_01.html
- https://docs.microsoft.com/en-us/dotnet/architecture/containerized-lifecycle/design-develop-containerized-apps/build-aspnet-core-applications-linux-containers-aks-kubernetes