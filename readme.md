
# Kubernetes pod sthudown evaluation
What happens with a netcore application when Kubernetes sends a signal to terminate the pod?

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


## Create the K8S cluster

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

## Test the application shutdown
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

## First impressions

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
In program, the UseShutdownTimeout (default to wait for 5 seconds) seems to have no effect when we increase to 30 seconds.
The host application has ended without following this wait time?

## References
https://kubernetes.io/docs/reference/kubectl/cheatsheet/
https://github.com/juniormayhe/Scripts/tree/master/docker
https://github.com/juniormayhe/Scripts/tree/master/kubernetes
https://docs.microsoft.com/en-us/aspnet/core/fundamentals/middleware/?view=aspnetcore-3.1
https://linux.die.net/Bash-Beginners-Guide/sect_12_01.html