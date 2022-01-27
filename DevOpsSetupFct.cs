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
    using Serilog;

    public class DevOpsSetupFct
    {
        private static Appsettings _config;
        private readonly string _organizationName;
        private readonly Uri _organizationUrl;
        private readonly string _pat;
        private readonly string _apiVersion;
        private readonly string _managementProjectId;

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
        public async Task SetupRepository([QueueTrigger(Constants.StorageQueueName)] string setupDevOpsMessage)
        {
            Log.Information($"*** Function {Constants.SETUP_REPO} triggered with message {setupDevOpsMessage} ***");

            string projectId="pending";
            Log.Debug("*** Get Properties ***");
            var queueItem = JsonDocument.Parse(setupDevOpsMessage);
            var workItemId = queueItem.RootElement.GetProperty("workItemId").GetInt32();
            var createType = queueItem.RootElement.GetProperty("createType").GetString();
            var projectName = queueItem.RootElement.GetProperty("projectName").GetString();
            var dataOwner1 = queueItem.RootElement.GetProperty("dataOwner1").GetString();
            var dataOwner2 = queueItem.RootElement.GetProperty("dataOwner2").GetString();
            var requestor = queueItem.RootElement.GetProperty("requestor").GetString();
            var costCenter = queueItem.RootElement.GetProperty("costCenter").GetString();
            var costCenterManager = queueItem.RootElement.GetProperty("costCenterManager").GetString();

            // Ensure, project name is not already used
            Log.Information("*** Validate Project Parameter ***");
            var allProjects = await Project.GetProjectsAsync(_organizationUrl, _pat);
            var projectNames = new List<string>();
            foreach (var projectNode in allProjects.RootElement.GetProperty("value").EnumerateArray())
            {
                var existingProjectName = projectNode.GetProperty("name").GetString();
                if (existingProjectName.StartsWith("AZP-"))
                {
                    projectNames.Add(projectNode.GetProperty("name").GetString().Substring(8));
                }
            }
            Log.Information($"*** Found {projectNames.Count} AZP-projects ***");

            switch (createType)
            {
                case "Project":
                    {
                        if (projectNames.Contains(projectName))
                        {
                            await ReportError($"Duplicate project name - A project with name {projectName} already exists. Please rename and try again.", _managementProjectId, requestor, createType, workItemId);
                            return;
                        }

                        // validate input
                        if (!await ValidateProjectName(workItemId, createType, projectName, requestor))
                        {
                            Log.Error($"*** Invalid project name {projectName} (Workitem {workItemId}) ***");
                            return;
                        }
                        if (!await ValidateCostCenterManagerName(workItemId, createType, costCenterManager, requestor))
                        {
                            Log.Error($"*** Missing CostCenter Manager for {projectName} (Workitem {workItemId})  ***");
                            return;
                        }

                        // create new project
                        Log.Information($"*** Create new project {projectName} ***");
                        var nextId = AzIdCreator.Instance.NextAzId();
                        var projectDescription = queueItem.RootElement.GetProperty("projectDescription").GetString().Trim().Replace("<div>", "").Replace("</div>", "");
                        projectDescription = String.Concat(projectDescription,
                            $"\n\nRequest: {_organizationUrl}/{_managementProjectId}/_workitems/edit/{workItemId}",
                            $"\nDataOwner: {dataOwner1}, {dataOwner2}",
                            $"\nRequestor: {requestor}",
                            $"\nCost Center: {costCenter}",
                            $"\nCost Center Manager: {costCenterManager}");
                        var zfProjectName = string.Format(Constants.PROJECT_PREFIX, nextId.ToString("D3")) + projectName;
                        Log.Information($"*** Creating project {zfProjectName} ***");
                        var operationsId = await Project.CreateProjectsAsync(_organizationUrl, zfProjectName, projectDescription, _config.ProcessTemplateId, _pat);

                        var pending = true;
                        var resultStatus = "";
                        do
                        { // wait for project creation
                            Log.Information($"*** Checking status for {operationsId} ***");
                            var status = await Project.GetProjectStatusAsync(_organizationUrl, operationsId, _pat);                            
                            resultStatus = status.RootElement.GetProperty("status").GetString();
                            Log.Information($"*** {zfProjectName} status is {resultStatus} ***");
                            switch (resultStatus)
                            {
                                case "cancelled":
                                    pending = false;
                                    Log.Information("*** Project creation cancelled ***");
                                    break;
                                case "failed":
                                    pending = false;
                                    Log.Error("*** Project creation failed ***");
                                    break;
                                case "succeeded":
                                    pending = false;
                                    Log.Information("*** Project creation succeeded ***");
                                    var project = await Project.GetProjectAsync(_organizationUrl, zfProjectName, _pat);
                                    projectId = project.RootElement.GetProperty("id").GetString();
                                    break;
                                default:
                                    Log.Information("*** Project creation pending ***");
                                    Thread.Sleep(5000);
                                    break;
                            }
                        } while (pending);

                        // Create Project Groups
                        Log.Information($"*** Trigger special group creations for {zfProjectName} ***");
                        await Project.TriggerEndpointAdminGroupCreationAsync(_organizationUrl, projectId, _pat);
                        await Project.TriggerDeploymentGroupAdminGroupCreationAsync(_organizationUrl, projectId, _pat);
                        await Project.TriggerReleaseAdminGroupCreationAsync(_organizationName, zfProjectName, _pat);                        
                        var projectDescriptors = await Project.GetProjectDescriptorAsync(_organizationName, projectId, _pat);

                        var projectDescriptor = projectDescriptors.RootElement.GetProperty("value").GetString();
                        var defaultProjectGroups = await Graph.GetAzDevOpsGroupsAsync(_organizationName, projectDescriptor, _pat);

                        Log.Information($"*** Get Group Descriptors for {zfProjectName} ***");
                        var defaultGroups = new Dictionary<string, string>();                        
                        foreach(var group in defaultProjectGroups.RootElement.GetProperty("value").EnumerateArray())
                        {
                            defaultGroups.Add(group.GetProperty("displayName").GetString(), group.GetProperty("descriptor").GetString());
                        }

                        var endpointAdminsGroup = defaultGroups.First(d => d.Key == "Endpoint Administrators");
                        var deploymentGroupAdminsGroup = defaultGroups.First(d => d.Key == "Deployment Group Administrators");
                        var buildAdminsGroup = defaultGroups.First(d => d.Key == "Build Administrators");
                        var releaseAdminsGroup = defaultGroups.First(d => d.Key == "Release Administrators");
                        var contributors = defaultGroups.First(d => d.Key == "Contributors");
                        var readers = defaultGroups.First(d => d.Key == "Readers");
                        var projectAdmins = defaultGroups.First(d => d.Key == "Project Administrators");

                        Log.Information($"*** Create Groups for {zfProjectName} ***");
                        var projectConsumerGroup = await Graph.CreateAzDevOpsGroupAsync(_organizationName, projectDescriptor, string.Format(ZfGroupNames.Proj_Consumer, nextId), new[] { readers.Value }, _pat);
                        var projectMaintDeveloperGroup = await Graph.CreateAzDevOpsGroupAsync(_organizationName, projectDescriptor, string.Format(ZfGroupNames.ProjMaint_Developer, nextId), new[] { contributors.Value }, _pat);
                        var projectMaintAdminsGroup = await Graph.CreateAzDevOpsGroupAsync(_organizationName, projectDescriptor, string.Format(ZfGroupNames.ProjMaint_Adminstrator, nextId), new[] { projectAdmins.Value }, _pat);
                        var projectMaintDeployerGroup = await Graph.CreateAzDevOpsGroupAsync(_organizationName, projectDescriptor, string.Format(ZfGroupNames.ProjMaint_Deployer, nextId), new[] { buildAdminsGroup.Value }, _pat);

                        var membershipGroups = new Dictionary<string, string>();
                        membershipGroups.Add(endpointAdminsGroup.Key, endpointAdminsGroup.Value);
                        membershipGroups.Add(deploymentGroupAdminsGroup.Key, deploymentGroupAdminsGroup.Value);

                        var joinedGroups = membershipGroups.Select(g => g.Value).ToArray();

                        var infraMaintDeveloperGroup = await Graph.CreateAzDevOpsGroupAsync(_organizationName, projectDescriptor, string.Format(ZfGroupNames.InfraMaint_Developer, nextId), joinedGroups, _pat);

                        membershipGroups.Add(buildAdminsGroup.Key, buildAdminsGroup.Value);
                        membershipGroups.Add(releaseAdminsGroup.Key, releaseAdminsGroup.Value);

                        joinedGroups = membershipGroups.Select(g => g.Value).ToArray();

                        var infraMaintAdminGroup = await Graph.CreateAzDevOpsGroupAsync(_organizationName, projectDescriptor, string.Format(ZfGroupNames.InfraMaint_Administrator, nextId), joinedGroups, _pat);

                        // Finish up
                        Log.Information($"*** Update Workitem Status {zfProjectName} #{workItemId} ***");
                        await UpdateWorkItemStatus(wiType.project, workItemId, nextId, zfProjectName);
                        var result = await Project.AddWorkItemCommentAsync(_organizationUrl, _managementProjectId, workItemId, $"Project <a href=\"{_organizationUrl}/{zfProjectName}\">{zfProjectName}</a> is provisioned and ready to use.", requestor, _pat);
                        break;
                    }
                case "Repository":
                    {
                        Log.Information($"*** Create new repository in {projectName} ***");
                        var repoName = queueItem.RootElement.GetProperty("repositoryName").GetString();
                        var azp_id = queueItem.RootElement.GetProperty("azp_Id").GetInt32();
                        if (String.IsNullOrEmpty(repoName))
                        {
                            throw new ApplicationException("Missing repository name - abort");
                        }
                        var currentProject = await Project.GetProjectByNameAsync(_organizationUrl, projectName, _pat);
                        try
                        {
                            projectId = currentProject.RootElement.GetProperty("id").GetString();
                            Log.Information($"*** Create repo in project {currentProject.RootElement.GetProperty("name").GetString()}, id: {projectId} ***");
                        }
                        catch
                        {
                            await ReportError($"Project {projectName} not found in {_organizationName} - cannot provision repository", _managementProjectId, requestor, createType, workItemId);
                            break;
                        }

                        await Repository.CreateCompliantRepositoryAsync(_organizationName, _organizationUrl, projectName, repoName, projectId, _pat);
                        Log.Information($"*** Repository {repoName} created in {projectName} ***");
#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
                        UpdateWorkItemStatus(wiType.repo, workItemId);
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
                        var result = await Project.AddWorkItemCommentAsync(_organizationUrl, _managementProjectId, workItemId, $"Repository <a href=\"{_organizationUrl}/{projectName}/_git/{repoName}\">{repoName}</a> is provisioned and ready to use.", requestor, _pat);
                        break;
                    }
            };
            Log.Information($"*** Processing #{workItemId} completed ***");
        }
        private enum wiType { project = 1, repo = 2 }
        private async Task UpdateWorkItemStatus(wiType type, int workItemId, int nextId=-1, string zfProjectName="")
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
            var updatedWorkItem = await Project.UpdateWorkItemByIdAsync(_organizationUrl, workItemId, patchOperation, _pat);
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
            Log.Error($"*** {errorMessage}, Project {projectId}, {workItemType} #{workItemId} ***");
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
