
# Kubernetes pod shutdown evaluation
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

### Test the application shutdown

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


## References
- https://kubernetes.io/docs/reference/kubectl/cheatsheet/
- https://github.com/juniormayhe/Scripts/tree/master/docker
- https://github.com/juniormayhe/Scripts/tree/master/kubernetes
- https://docs.microsoft.com/en-us/aspnet/core/fundamentals/middleware/?view=aspnetcore-3.1
- https://linux.die.net/Bash-Beginners-Guide/sect_12_01.html
- https://docs.microsoft.com/en-us/dotnet/architecture/containerized-lifecycle/design-develop-containerized-apps/build-aspnet-core-applications-linux-containers-aks-kubernetes