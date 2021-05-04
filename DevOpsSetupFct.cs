namespace DevOpsManagement
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json;
    using System.Threading.Tasks;
    using Microsoft.Azure.WebJobs;
    using Microsoft.Azure.WebJobs.Host;
    using Microsoft.Extensions.Logging;
    using DevOpsAPI;
    using DevOpsManagement.Tools;
    using System.Threading;

    public class DevOpsSetupFct
    {
        private static Appsettings _config;
        private readonly string _organizationName;
        private readonly Uri _organizationUrl;
        private readonly string _pat;
        private readonly string _apiVersion;
        private ILogger _log;

        public DevOpsSetupFct(Appsettings settings)
        {
            _config = settings;
            _organizationName = settings.VSTSOrganization;
            _pat = settings.PAT;
            _apiVersion = settings.VSTSApiVersion;
            _organizationUrl = new Uri($"https://dev.azure.com/{_organizationName}");
        }

        [FunctionName(Constants.SETUP_REPO)]
        public async Task SetupRepository([QueueTrigger(Constants.StorageQueueName)] string setupDevOpsMessage, ILogger log)
        {
            _log = log;
            _log.LogInformation($"*** Function {Constants.SETUP_REPO} triggered with message {setupDevOpsMessage} ***");

            string projectId;

            var queueItem = JsonDocument.Parse(setupDevOpsMessage);
            var workItemId = queueItem.RootElement.GetProperty("workItemId").GetInt32();
            var createType = queueItem.RootElement.GetProperty("createType").GetString();
            var projectName = queueItem.RootElement.GetProperty("projectName").GetString().Trim().Replace(" ", "_");
            var environment = queueItem.RootElement.GetProperty("environment").GetString().Trim().ToLower();            
            var requestor = queueItem.RootElement.GetProperty("requestor").GetString();

            if (String.IsNullOrEmpty(projectName))
            {
                throw new ApplicationException("Missing projectname - abort");
            }

            var allProjects = await Project.GetProjectsAsync(_organizationUrl, _pat);

            var projectNames = new List<string>();
            foreach (var projectNode in allProjects.RootElement.GetProperty("value").EnumerateArray())
            {
                projectNames.Add(projectNode.GetProperty("name").GetString());
            }            

            switch (createType)
            {
                case "Project":
                    {
                        // create new project

                        var nextId = AzIdCreator.Instance.NextAzId(environment);
                        var projectDescription = queueItem.RootElement.GetProperty("projectDescription").GetString().Trim().ToLower();
                        var zfProjectName = string.Format(Constants.PROJECT_PREFIX, nextId.ToString("D3")) + projectName;
                        var operationsId = await Project.CreateProjectsAsync(_organizationUrl, zfProjectName, projectDescription, Constants.PROCESS_TEMPLATE_ID, _pat);

                        var pending = true;
                        var resultStatus = "";
                        do
                        {
                            var status = await Project.GetProjectStatusAsync(_organizationUrl, operationsId, _pat);
                            resultStatus = status.RootElement.GetProperty("status").GetString();
                            switch (resultStatus)
                            {
                                case "cancelled":
                                    pending = false;
                                    break;
                                case "failed":
                                    pending = false;
                                    break;
                                case "succeeded":
                                    pending = false;
                                    var project = await Project.GetProjectAsync(_organizationUrl, zfProjectName, _pat);
                                    projectId = project.RootElement.GetProperty("id").GetString();
                                    break;
                                default:
                                    Thread.Sleep(5000); // wait for project creation
                                    break;
                            }
                        } while (pending);

                        var patchOperation = new[]
                        {
                        new
                        {
                            op="add",
                            path="/fields/System.WorkItemType",
                            value="Project"
                        },
                        new
                        {
                            op="add",
                            path="/fields/System.State",
                            value="Provisioned"
                        },
                        new
                        {
                            op="add",
                            path="/fields/Custom.AZP_ID",
                            value=$"{nextId}"
                        }
                    };
                        var updatedWorkItem = Project.UpdateWorkItemByIdAsync(_organizationUrl, workItemId, patchOperation, _pat);
                        // ToDO: Create Groups

                        break;
                    }
                case "Repository":
                    {
                        var repoName = queueItem.RootElement.GetProperty("repositoryName").GetString();
                        var azp_id = queueItem.RootElement.GetProperty("azp_Id").GetInt32();
                        var parentProjectName = string.Format(Constants.PROJECT_PREFIX, azp_id.ToString("D3")) + projectName; 
                        var projectNameMatch = projectNames.FirstOrDefault<string>(p => p.Contains(parentProjectName));
                        if (String.IsNullOrEmpty(repoName))
                        {
                            throw new ApplicationException("Missing repository name - abort");
                        }
                        var currentProject = await Project.GetProjectByNameAsync(_organizationUrl, projectNameMatch, _pat);
                        try
                        {
                            projectId = currentProject.RootElement.GetProperty("id").GetString();
                            log.LogDebug($"Current project is {currentProject.RootElement.GetProperty("name").GetString()}, id: {projectId}");
                        }
                        catch
                        {
                            throw new ApplicationException($"Project {projectName} not found in {_organizationName} - abort");
                        }

                        //await CreateRepository(projectNameMatch, repoName, projectId);
                        await Repository.CreateCompliantRepositoryAsync(_organizationName, _organizationUrl, projectNameMatch, repoName, projectId, _pat);

                        var patchOperation = new[]
                        {
                        new
                        {
                            op="add",
                            path="/fields/System.WorkItemType",
                            value="Repository"
                        },
                        new
                        {
                            op="add",
                            path="/fields/System.State",
                            value="Provisioned"
                        }
                    };
                        var updatedWorkItem = Project.UpdateWorkItemByIdAsync(_organizationUrl, workItemId, patchOperation, _pat);
                        break;
                    }
            };

        }        
    }
}
