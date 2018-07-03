# Cluster setup (Kubernetes and AWS)

## Introduction
This document describes what Kubernetes is and how it can be set up on AWS. We will try to use a simple imaginary inventory application as a usecase to run using Kubernetes to simplify the learning process. Our application is a microservice based, containerized system, consists of two services, Product Management and Stock Reporting. We assume that the Project Management service will not be used a lot but the Stock Reporting (which is a heavy process) is used frequently. 

## What is Kubernetes
For running a containerized application you need to run all the containers for the system to be fully functional. Generally speaking each container has a main process and if that process stops the whole container will stop which means the service that was running in the container will not be accessible. The only way to have that service back up and running is to run its container by running "docker run". However, this approach is not practical at all as you constantly have to monitor your containers and keep running different commands to make sure your application remains running. This process can be automated using a container orchestration. [Kubernetes](https://kubernetes.io/docs/concepts/overview/what-is-kubernetes/) is a very popular open source container orchastrator developed by google. 

## Kubernetes Concepts
Below is the list of elements we need to understand to be able to run and manage an application on Kubernetes.

#### Cluster
For an application to run on Kubernetes we need to create a cluster which wraps all the Kubernetes elements required to keep all the application containers up and running. 

#### Node
Nodes are the actual servers (bare metal or VM) that host containers. Each cluster can have one or more nodes.

#### Master
Just like nodes, masters are bare metal or VM machines. In order to manage the state of a cluster there should be [some processes](https://kubernetes.io/docs/concepts/overview/components/#master-components), with different responsibilities and taks, running all the time, which monitor the cluster and take actions (remove, add or update elements) accordingly to controll the overal state of the cluster. These process run on masters. For high availability you can set up more than one master in your cluster and Kubernetes will distribute its jobs among the running masters. When connecting to the cluster using the Kubernetes cli you actually connect to a master.

#### States
Let's say for our inventory management application, because we know that the product management service will not be used a lot, one running container will be enough for it but for the service management that has higher demand we need have to run 3 containers at the same time. We just planned a state that we desire our application to have when running on Kubernetes. In Kubernetes that state is called the Desired state. Now let's say we configure Kubernetes and our application is running. For one reason or another, we may lose some containers. For example let's assume we lose the product management container and one of the stock reporting container. Now our cluster only has two stock reporting containers running on it which is the describtion of the current state of our cluster, which obviously doesn't match the desired state we defined earlier. As mentioned earlier, Kubernetes constantly monitors the cluster and trys to manage the resources to bring the current state to the desired state. In our example, once Kubernetes notices that we are missing the production management container and one of the stock reporting container it creates them for us which then brings the current state to the desired state.

#### Pod
If you are familiar with containers you should know what Docker is. For containers to work there should be a container runtime. Docker is a container runtime. Kubernetes supports different container runtimes. By default it uses Docker but you can use the other options if neccessary. Pod is the Kubernetes abstraction for a container alongside some more kubernetes related specifications i.e. labels (described later), name, etc. For example for a container image you often want to specify how much resource that container is allowed to use which, can be specified in the pod definition. All the environment elements (i.e. environment variables) that an application running in a pod works with come from the hosting pod. Like a normal virtual machine, when running, each pod will be allocated a cluster level IP address which can be used to connect to it. For the desired state of the Inventory applciation described in the previous section we would have a pod running the product management container and three pods running the stock reporting contianers.

#### Deployments
Based on the desire state of the inventory application we have to configure Kubernetes to make sure one reporting service and three stock reporting service pods run all the time. This configuration is defined in the form of deployments. Earlier we managed to define the runtime specification of our containers in the form of pod definitions. Now we have to define how many of those pods we want running. Because we have two different pod definitions (one for product management and one for stock reporting) we need to have to different deployments. Deployments encapsulate pod definitions as well as the replication specification of that pod. Based on the replication specification, each deployment can create zero or more pods. An example of a deployment is shown below :

```yaml
apiVersion: extensions/v1beta1
 kind: Deployment
 metadata:
   name: StockReporting
 spec:
   replicas: 3
   template:
     metadata:
       labels:
         microservice: stock-reporting
     spec:
       containers:
         - name: cnt-stock-reporting
           image: The url of the container image.           
           ports:
           - containerPort: 9376
             
```

As you can see in the example we have create a deployment called StockReporting (read from metadata.name), specified that we need 3 running pod (read from spec.replicas) and defined the pod spec in spec.spec section. When added to Kubernetes, 3 pods will be created from the container image specified in spec.spec.image. It also assigns a label to the pods, read from spec.metadata.labels, which will be described later.

#### Services
Containers run in pods, deployments make sure pods run as planned, but how do we access the containers? Yes you can use pod ip addresses but they change everytime a new pod is created. The stock reporting service in the inventory application has to talk to the product management service to get the product information available in stock. We wouldn't want to use the ip address of the pod which runs the product management service. For some reason if the product management pod stops and a new pod is created for it the new pod will have a new virtual ip address and the existing stock management services will not be able to connect to the new pod as they have the old ip address of the product management service pod. To solve that problem, Kubernetes has introduced a concept called service. You can define a service, give it a name, a type and a pod selector, which is used by kubernetes to search among the running pods and map the matching ones to the service. Once mapped, you can use the service name, as opposed to a hard-coded ip address, to connect to pods. 
Below is the definition of the StockManagement service in our inventory application on Kubernetes:

```yaml
kind: Service
apiVersion: v1
metadata:
  name: StockReporting
spec:
  selector:
    microservice: stock-reporting
  ports:
  - protocol: TCP
    port: 80
    targetPort: 9376

```
The template above creates a service named StockReporting (read form metadata.name) for the deployment we created in the previous section. Notice the spec.selector, it matches spec.template.metadata.labels. We will describe labels and selectors in more details in the next section. The service specification also tells Kubernetes to redirect the coming TCP traffic to StockReporting:80 to the 9376 port of its bound pods (defined in spec.ports).


#### Labels and Selctors
Like any other software system Kubernetes resources get allocated a unique id when added to the cluster. Howerver, using long ids are difficult to remember and use. Also, normally when there are more than one instance of a reqource (i.e. pods) it is hard to refere to them by their ids when managing them (i.e. imagine you want to kill all the stock reporting pods). Kubernetes uses labels as a way to identify and query resources. We specified a label called microservice with the "stock-reporting" value to the deployment template in the Deployment section. When Kubernetes creates new pods using that deployment template it assigns that label to them. Later if you want to find all the pods created for the stock reporting service you can query the microservice label and pass "stock-reporting". Labels enable Kubernetes resources to find other resources too. For example in the Service section, the spec.selector part of the template specifies "microservice: stock-reporting". That configuration tells kubernetes to search for all the pods with that label/value and bind them to the service.

## AWS account setup
Earlier in this article we mentioned that Kubernetes needs some servers, to run as masters and nodes, to be able to function. There are different ways to create and manage servers for Kubernetes but for our environment we chose to use AWS. AWS is a well known IaaS provided in the market which provides some cloud computing features such as scailability, availability, security, good logging and monitoring systems that our production environment can benefit from. Compared to the other could providers we have more experience with AWS and that's another reason why we chose it.

## Installation

## Kops

## Kubectl

TODO: https://docs.google.com/document/d/1CRvhWy5uN3dIw-agmqTjhdl8aC4bkWYsFPS45XLWick/edit

## Application Node role
The role of the Node which is created by Kubernetes. The EC2 servers natively have this role. This role should have the permission to assume other roles in general. Based on the following policy:
```json
{
    "Version": "2012-10-17",
    "Statement": [
        {
            "Sid": "VisualEditor0",
            "Effect": "Allow",
            "Action": "sts:AssumeRole",
            "Resource": "*"
        }
    ]
}
```