namespace DevOpsManagement;

using DevOpsAPI;
using DevOpsManagement.Model;
using DevOpsManagement.Tools;
using Microsoft.Azure.WebJobs;
using Newtonsoft.Json;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Mail;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

public class DevOpsSetupFct
{
    private static Appsettings _config;
    private readonly string _organizationName;
    private readonly Uri _organizationUrl;
    private readonly string _pat;
    private readonly string _managementProjectId;

    public DevOpsSetupFct(Appsettings settings)
    {
        try
        {
            _config = settings;
            _organizationName = settings.VSTSOrganization;
            _pat = settings.PAT;
            _organizationUrl = new Uri($"https://dev.azure.com/{_organizationName}");
            _managementProjectId = settings.ManagementProjectId;
            Log.Information($"*** DevOpsSetupFct: Orga: {_organizationName}, Url: {_organizationUrl}, PAT (starting with {_pat.Substring(0, 4)}, Management Project Id: {_managementProjectId} ***");
        }
        catch(Exception ex)
        {
            Log.Error(ex, "*** Fatal: DevOpsSetupFct Constructor failed to initialize ***");
            throw;
        }
    }

    [FunctionName(Constants.SETUP_REPO)]
    public async Task SetupRepository([QueueTrigger(Constants.StorageQueueName)] string setupDevOpsMessage,
        [Queue($"{Constants.StorageQueueName}-poison")] IAsyncCollector<string> poison)
    {
        Log.Information($"*** Function {Constants.SETUP_REPO} triggered with message {setupDevOpsMessage} ***");

        string projectId = "pending";
        Log.Debug("*** Get Properties ***");
        ProjectPayload provisioningCmd;
        try
        {
            provisioningCmd = JsonConvert.DeserializeObject<ProjectPayload>(setupDevOpsMessage);
            await Project.AddWorkItemCommentAsync(_organizationUrl, _managementProjectId, provisioningCmd.WorkItemId, $"Processing started - getting things ready for you...", provisioningCmd.Requestor, _pat);
        }
        catch (Exception ex)
        {
            Log.Error(ex, $"Bad Request - cannot process request message {setupDevOpsMessage}");
            await poison.AddAsync(setupDevOpsMessage);
            return;
        }
        switch (provisioningCmd.CreateType)
        {
            case "Project":
                try
                {
                    // Ensure, project name is not already used
                    Log.Information("*** Validate Project Parameter ***");

                    var allProjects = await Project.GetProjectsAsync(_organizationUrl, _pat);
                    var existingProjects = new List<string>();
                    foreach (var projectNode in allProjects.RootElement.GetProperty("value").EnumerateArray())
                    {
                        var existingProjectName = projectNode.GetProperty("name").GetString();
                        if (existingProjectName.StartsWith("AZP-"))
                        {
                            existingProjects.Add(projectNode.GetProperty("name").GetString().Substring(8));
                        }
                    }
                    Log.Information($"*** Found {existingProjects.Count} AZP-projects ***");

                    if (existingProjects.Contains(provisioningCmd.ProjectName))
                    {
                        await ReportError($"Duplicate project name - A project with name {provisioningCmd.ProjectName} already exists. Please rename and try again.", _managementProjectId, provisioningCmd.Requestor, provisioningCmd.CreateType, provisioningCmd.WorkItemId);
                        await poison.AddAsync(setupDevOpsMessage);
                        return;
                    }
                    // validate input
                    if (!await ValidateProjectName(provisioningCmd.WorkItemId, provisioningCmd.CreateType, provisioningCmd.ProjectName, provisioningCmd.Requestor))
                    {
                        await ReportError($"*** Invalid project name {provisioningCmd.ProjectName} (Workitem {provisioningCmd.WorkItemId}) ***", _managementProjectId, provisioningCmd.Requestor, provisioningCmd.CreateType, provisioningCmd.WorkItemId);
                        await poison.AddAsync(setupDevOpsMessage);
                        return;
                    }
                    if (!await ValidateCostCenterManagerName(provisioningCmd.WorkItemId, provisioningCmd.CreateType, provisioningCmd.CostCenterManager, provisioningCmd.Requestor))
                    {
                        await ReportError($"*** Missing CostCenter Manager for {provisioningCmd.ProjectName} (Workitem {provisioningCmd.WorkItemId})  ***", _managementProjectId, provisioningCmd.Requestor, provisioningCmd.CreateType, provisioningCmd.WorkItemId);
                        await poison.AddAsync(setupDevOpsMessage);
                        return;
                    }

                    // create new project
                    Log.Information($"*** Create new project {provisioningCmd.ProjectName} ***");
                    var nextId = AzIdCreator.Instance.NextAzId();
                    var projectDescription = provisioningCmd.ProjectDescription.Trim().Replace("<div>", "").Replace("</div>", "");
                    projectDescription = String.Concat(projectDescription,
                        $"\n\nRequest: {_organizationUrl}/{_managementProjectId}/_workitems/edit/{provisioningCmd.WorkItemId}",
                        $"\nDataOwner: {provisioningCmd.DataOwner1}, {provisioningCmd.DataOwner2}",
                        $"\nRequestor: {provisioningCmd.Requestor}",
                        $"\nCost Center: {provisioningCmd.CostCenter}",
                        $"\nCost Center Manager: {provisioningCmd.CostCenterManager}");
                    var zfProjectName = string.Format(Constants.PROJECT_PREFIX, nextId.ToString("D3")) + provisioningCmd.ProjectName;
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
                                await ReportError("*** Project creation caceled ***", _managementProjectId, provisioningCmd.Requestor, provisioningCmd.CreateType, provisioningCmd.WorkItemId);
                                return;
                            case "failed":
                                pending = false;
                                await ReportError("*** Project creation failed ***", _managementProjectId, provisioningCmd.Requestor, provisioningCmd.CreateType, provisioningCmd.WorkItemId);
                                return;
                            case "succeeded":
                                pending = false;
                                Log.Information("*** Project creation succeeded ***");
                                var project = await Project.GetProjectAsync(_organizationUrl, zfProjectName, _pat);
                                projectId = project.RootElement.GetProperty("id").GetString();
                                break;
                            default:
                                Log.Debug("*** Project creation pending ***");
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
                    foreach (var group in defaultProjectGroups.RootElement.GetProperty("value").EnumerateArray())
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
                    Log.Information($"*** Success - Update Workitem Status {zfProjectName} #{provisioningCmd.WorkItemId} ***");
                    await UpdateWorkItemStatus(wiType.project, provisioningCmd.WorkItemId, nextId, zfProjectName);
                    var result = await Project.AddWorkItemCommentAsync(_organizationUrl, _managementProjectId, provisioningCmd.WorkItemId, $"Project <a href=\"{_organizationUrl}/{zfProjectName}\">{zfProjectName}</a> is provisioned and ready to use.", provisioningCmd.Requestor, _pat);
                    break;
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "*** Failed to fully provision new project. Payload: {@Payload}", provisioningCmd);
                    await poison.AddAsync(setupDevOpsMessage);
                    await ReportError("*** Project creation failed - check with helpdesk ***", _managementProjectId, provisioningCmd.Requestor, provisioningCmd.CreateType, provisioningCmd.WorkItemId);
                    return;
                }
            case "Repository":
                try
                {
                    var repoCmd = JsonConvert.DeserializeObject<RepositoryPayload>(setupDevOpsMessage);
                    Log.Information($"*** Create new repository in {provisioningCmd.ProjectName} ***");
                    var currentProject = await Project.GetProjectByNameAsync(_organizationUrl, provisioningCmd.ProjectName, _pat);
                    try
                    {
                        projectId = currentProject.RootElement.GetProperty("id").GetString();
                        Log.Information($"*** Create repo in project {currentProject.RootElement.GetProperty("name").GetString()}, id: {projectId} ***");
                    }
                    catch
                    {
                        await poison.AddAsync(setupDevOpsMessage);
                        await ReportError($"Project {provisioningCmd.ProjectName} not found in {_organizationName} - cannot provision repository", _managementProjectId, provisioningCmd.Requestor, provisioningCmd.CreateType, provisioningCmd.WorkItemId);
                        break;
                    }

                    await Repository.CreateCompliantRepositoryAsync(_organizationName, _organizationUrl, provisioningCmd.ProjectName, repoCmd.RepositoryName, projectId, _pat);
                    Log.Information($"*** Repository {repoCmd.RepositoryName} created in {provisioningCmd.ProjectName} ***");
                    await UpdateWorkItemStatus(wiType.repo, provisioningCmd.WorkItemId);
                    var result = await Project.AddWorkItemCommentAsync(_organizationUrl, _managementProjectId, provisioningCmd.WorkItemId, $"Repository <a href=\"{_organizationUrl}/{provisioningCmd.ProjectName}/_git/{repoCmd.RepositoryName}\">{repoCmd.RepositoryName}</a> is provisioned and ready to use.", provisioningCmd.Requestor, _pat);
                    break;
                }
                catch (Exception ex)
                {
                    await poison.AddAsync(setupDevOpsMessage);
                    Log.Error(ex, "*** Failed to fully provision new repository. Payload: {@Payload}", provisioningCmd);
                    await ReportError("*** Repository creation failed ***", _managementProjectId, provisioningCmd.Requestor, provisioningCmd.CreateType, provisioningCmd.WorkItemId);
                    return;
                }
        };
        Log.Information($"*** Processing #{provisioningCmd.WorkItemId} completed ***");
    }
    private enum wiType { project = 1, repo = 2 }
    private async Task UpdateWorkItemStatus(wiType type, int workItemId, int nextId = -1, string zfProjectName = "")
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
