# Azure DevOps Management Automation
This solution creates new Azure DevOps projects and repositories based on workitem creation in Azure DevOps. A LogicApp monitors the board for new workitems of type Project or Repository and then queues a message in Azure Storage. This triggers an Azure Function, which does the actual provisioning of the project or repository.

## Deployment

