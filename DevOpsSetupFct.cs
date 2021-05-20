namespace DevOpsManagement
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json;
    using System.Threading.Tasks;
    using Microsoft.Azure.WebJobs;
    using Microsoft.Extensions.Logging;
    using DevOpsAPI;
    using DevOpsManagement.Tools;
    using System.Threading;
    using System.Text.RegularExpressions;
    using System.Net.Mail;

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

            string projectId="pending";

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
                        if (!await ValidateProjectName(workItemId, createType, projectName, requestor))
                        {
                            return;
                        }
                        if (!await ValidateCostCenterManagerName(workItemId, createType, costCenterManager, requestor))
                        {
                            return;
                        }

                        // create new project
                        var nextId = AzIdCreator.Instance.NextAzId();
                        var projectDescription = queueItem.RootElement.GetProperty("projectDescription").GetString().Trim().Replace("<div>", "").Replace("</div>", "");
                        projectDescription = String.Concat(projectDescription,
                            $"\n\nRequest: {_organizationUrl}/{_managementProjectId}/_workitems/edit/{workItemId}",
                            $"\nDataOwner: {dataOwner1}, {dataOwner2}",
                            $"\nRequestor: {requestor}",
                            $"\nCost Center: {costCenter}",
                            $"\nCost Center Manager: {costCenterManager}");
                        var zfProjectName = string.Format(Constants.PROJECT_PREFIX, nextId.ToString("D3")) + projectName;
                        var operationsId = await Project.CreateProjectsAsync(_organizationUrl, zfProjectName, projectDescription, Constants.PROCESS_TEMPLATE_ID, _pat);

                        var pending = true;
                        var resultStatus = "";
                        do
                        { // wait for project creation
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
                                    Thread.Sleep(5000);
                                    break;
                            }
                        } while (pending);

                        // Create Project Groups
                        var endpoint = await Project.TriggerEndpointAdminGroupCreationAsync(_organizationUrl, projectId, _pat);
                        var projectDescriptors = await Project.GetProjectDescriptorAsync(_organizationName, projectId, _pat);
                        var projectDescriptor = projectDescriptors.RootElement.GetProperty("value").GetString();

                        var defaultProjectGroups = await Graph.GetAzDevOpsGroupsAsync(_organizationName, projectDescriptor, _pat);

                        var defaultGroups = new Dictionary<string, string>();
                        
                        foreach(var group in defaultProjectGroups.RootElement.GetProperty("value").EnumerateArray())
                        {
                            defaultGroups.Add(group.GetProperty("displayName").GetString(), group.GetProperty("descriptor").GetString());
                        }

                        var membershipGroups = new Dictionary<string, string>();
                        var buildAdminsGroup = defaultGroups.First(d => d.Key == "Build Administrators");
                        var endpointAdminsGroup = defaultGroups.First(d => d.Key == "Endpoint Administrators");
                        membershipGroups.Add(buildAdminsGroup.Key, buildAdminsGroup.Value);
                        membershipGroups.Add(endpointAdminsGroup.Key, endpointAdminsGroup.Value);
                        var joinedGroups = membershipGroups.Select(g=>g.Value).ToArray();
                        //membershipGroups.Add("")
                        var infraMaintAdminGroup = await Graph.CreateAzDevOpsGroupAsync(_organizationName, projectDescriptor, string.Format(ZfGroupNames.InfraMaint_Administrator, nextId), joinedGroups, _pat);
                        var groupDescriptor = infraMaintAdminGroup.RootElement.GetProperty("descriptor").GetString();


                        // Finish up
                        UpdateWorkItemStatus(wiType.project, workItemId, nextId, zfProjectName);
                        var result = await Project.AddWorkItemCommentAsync(_organizationUrl, _managementProjectId, workItemId, $"Project <a href=\"{_organizationUrl}/{zfProjectName}\"{zfProjectName}</a> is provisioned and ready to use.", requestor, _pat);
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

                        UpdateWorkItemStatus(wiType.repo, workItemId);
                        break;
                    }
            };

        }
        private enum wiType { project = 1, repo = 2 }
        private void UpdateWorkItemStatus(wiType type, int workItemId, int nextId=-1, string zfProjectName="")
        {
            var patchOperation = new object();
            if (type == wiType.project)
            {
                // update workitem status
                patchOperation = new[]
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
            }
            else if (type == wiType.repo)
            {
                patchOperation = new[]
                        {
                    new
                    {
                        op = "add",
                        path = "/fields/System.WorkItemType",
                        value = "Repository"
                    },
                    new
                    {
                        op = "add",
                        path = "/fields/System.State",
                        value = "Provisioned"
                    }
                };
            }
            var updatedWorkItem = Project.UpdateWorkItemByIdAsync(_organizationUrl, workItemId, patchOperation, _pat);
        }
        private async Task<bool> ValidateProjectName(int workItemId, string createType, string projectName, string requestor)
        {
            bool isValid = true;
            if (String.IsNullOrEmpty(projectName))
            {
                await ReportError("Missing projectname - abort", _managementProjectId, requestor, createType, workItemId);
            }

            Regex validChars = new Regex(@"[^a-zA-Z0-9_]", RegexOptions.Compiled);
            var matches = validChars.Matches(projectName);

            if (matches.Count > 0)
            {
                // found invalid characters
                await ReportError("Invalid characters in project name - change name and set to approved again for retry", _managementProjectId, requestor, createType, workItemId);
                isValid = false;
            }

            return isValid;
        }
        private async Task<bool> ValidateCostCenterManagerName(int workItemId, string createType, string costCenterManagerEmail, string requestor)
        {
            bool isValid = true;
            if (String.IsNullOrEmpty(costCenterManagerEmail))
            {
                await ReportError("Missing CostCenter Manager - abort", _managementProjectId, requestor, createType, workItemId);
            }

            try
            {
                MailAddress m = new MailAddress(costCenterManagerEmail);
                isValid = true;
            }
            catch (FormatException)
            {
                isValid = false;
                await ReportError("CostCenterManager is no valid email - verify email and set to approved again for retry", _managementProjectId, requestor, createType, workItemId);
            }

            return isValid;
        }
        private async Task ReportError(string errorMessage, string projectId, string requestor, string workItemType, int workItemId)
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
            var result = await Project.AddWorkItemCommentAsync(_organizationUrl, projectId, workItemId, errorMessage, requestor, _pat);
        }
    }
}
