# Azure DevOps Management Automation
This solution creates new Azure DevOps projects and repositories based on workitem creation in Azure DevOps. A LogicApp monitors the board for new workitems of type Project or Repository and then queues a message in Azure Storage. This triggers an Azure Function, which does the actual provisioning of the project or repository.

## Architecture

The following resources will be required:
![Architecture](/images/resources.png)

- LogicApps
 will monitor Azure DevOps and create a provisioning message to Azure Queues
- Storage Account 
hosting the Azure Queues
- AppService/ Plan
Host for Azure Function for processing the queue provisioning messages
- Application Insights
Monitoring the Azure Function
- KeyVault
keeping the app configuration secrets (PAT, WebJobStorage connection string)

## Deployment
