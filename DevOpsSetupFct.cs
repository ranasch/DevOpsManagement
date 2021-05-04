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
    using System.Text.RegularExpressions;

    public class DevOpsSetupFct
    {
        private static Appsettings _config;
        private readonly string _organizationName;
        private readonly Uri _organizationUrl;
        private readonly string _pat;
        private readonly string _apiVersion;
        private readonly string _managementProjectId;
        private ILogger _log;

        public DevOpsSetupFct(Appsettings settings)
        {
            _config = settings;
            _organizationName = settings.VSTSOrganization;
            _pat = settings.PAT;
            _apiVersion = settings.VSTSApiVersion;
            _organizationUrl = new Uri($"https://dev.azure.com/{_organizationName}");
            _managementProjectId = settings.ManagementProjectId;
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
            var projectName = queueItem.RootElement.GetProperty("projectName").GetString();
            var dataOwner1 = queueItem.RootElement.GetProperty("dataOwner1").GetString();
            var dataOwner2 = queueItem.RootElement.GetProperty("dataOwner2").GetString();
            var requestor = queueItem.RootElement.GetProperty("requestor").GetString();
            var costCenter = queueItem.RootElement.GetProperty("costCenter").GetString();
            var costCenterManager = queueItem.RootElement.GetProperty("costCenterManager").GetString();

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
                        // validate input
                        if (!await ValidateProjectName(workItemId, createType, projectName))
                        {
                            return;
                        }
                        if (!await ValidateCostCenterManagerName(workItemId, createType, costCenterManager))
                        {
                            return;
                        }

                        // create new project

                        var nextId = AzIdCreator.Instance.NextAzId();
                        var projectDescription = queueItem.RootElement.GetProperty("projectDescription").GetString().Trim().Replace("<div>","").Replace("</div>","");
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
                            path="/fields/System.Title",
                            value=$"{zfProjectName}"
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

        private async Task<bool> ValidateProjectName(int workItemId, string createType, string projectName)
        {
            bool isValid = true;
            if (String.IsNullOrEmpty(projectName))
            {
                await ReportError("Missing projectname - abort", _managementProjectId, createType, workItemId);                
            }

            Regex validChars = new Regex(@"[^a-zA-Z0-9_]", RegexOptions.Compiled);
            var matches = validChars.Matches(projectName);

            if(matches.Count>0)
            {
                // found invalid characters
                await ReportError("Invalid characters in project name - change name and set to approved again for retry", _managementProjectId, createType, workItemId);
                isValid = false;
            }

            return isValid;
        }

        private async Task<bool> ValidateCostCenterManagerName(int workItemId, string createType, string costCenterManagerEmail)
        {
            bool isValid = true;
            if (String.IsNullOrEmpty(costCenterManagerEmail))
            {
                await ReportError("Missing CostCenter Manager - abort", _managementProjectId, createType, workItemId);
            }

            Regex validChars = new Regex(@"^[a-zA-Z\.\-_]+@([a-zA-Z\.\-_]+\.)+[a-zA-Z]{2,4}$", RegexOptions.Compiled);
            var matches = validChars.Matches(costCenterManagerEmail);

            if (matches.Count > 0)
            {
                // found invalid characters
                await ReportError("CostCenterManager is no valid email - verify email and set to approved again for retry", _managementProjectId, createType, workItemId);
                isValid = false;
            }

            return isValid;
        }

        private async Task ReportError(string errorMessage, string projectId, string workItemType, int workItemId)
        {
            var patchOperation = new[]
{
                        new
                        {
                            op="add",
                            path="/fields/System.WorkItemType",
                            value=$"{workItemType}"
                        },
                        new
                        {
                            op="add",
                            path="/fields/System.State",
                            value="Error"
                        }
                    };
            
            var updatedWorkItem = await Project.UpdateWorkItemByIdAsync(_organizationUrl, workItemId, patchOperation, _pat);
            var result = await Project.AddWorkItemCommentAsync(_organizationUrl, projectId, workItemId, errorMessage, _pat);
        }
    }
}
