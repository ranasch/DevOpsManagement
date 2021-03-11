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
    public class SetupRepositoryFct
    {
        private static Appsettings _config;
        private readonly string _organizationName;
        private readonly Uri _organizationUrl;
        private readonly string _pat;
        private readonly string _apiVersion;

        public SetupRepositoryFct(Appsettings settings)
        {
            _config = settings;
            _organizationName = settings.VSTSOrganization;
            _pat = settings.PAT;
            _apiVersion = settings.VSTSApiVersion;
            _organizationUrl = new Uri($"https://dev.azure.com/{_organizationName}");
        }

        [FunctionName(Constants.SETUP_REPO)]
        public async Task SetupRepository([QueueTrigger(Constants.StorageQueueName)]string createProjectMessage, ILogger log)
        {
            log.LogInformation($"*** Function {Constants.SETUP_REPO} triggered with message {createProjectMessage} ***");

            string projectId;
            string repoId;
            string contributorDescriptor;
            string readerDescriptor;
            string adminDescriptor;
            string mainBranchRefId;
            string projectCollectionAdminDescriptor;

            var project=JsonDocument.Parse(createProjectMessage);

            var projectName = project.RootElement.GetProperty("project").GetString();
            var repoName = project.RootElement.GetProperty("repo").GetString();

            if (String.IsNullOrEmpty(projectName))
            {
                throw new ApplicationException("Missing projectname - abort");
            }
            if (String.IsNullOrEmpty(repoName))
            {
                throw new ApplicationException("Missing repository name - abort");
            }


            // Get current Project
            var currentProject = await Project.GetProjectByNameAsync(_organizationUrl, projectName, _pat);
            try
            {
                projectId = currentProject.RootElement.GetProperty("id").GetString();
                log.LogDebug($"Current project is {currentProject.RootElement.GetProperty("name").GetString()}, id: {projectId}");
            }
            catch
            {
                throw new ApplicationException($"Project {projectName} not found in {_organizationName} - abort");
            }

            // Get Identity for Contributors
            var contributor = await Project.GetIdentityForGroupAsync(_organizationName, projectName, "Contributors", _pat);
            contributorDescriptor = contributor.RootElement.GetProperty("value").EnumerateArray().First().GetProperty("descriptor").GetString();
            log.LogDebug($"Contributor descriptor = {contributorDescriptor}");

            // Get Identity for Readers
            var reader = await Project.GetIdentityForGroupAsync(_organizationName, projectName, "Readers", _pat);
            readerDescriptor = reader.RootElement.GetProperty("value").EnumerateArray().First().GetProperty("descriptor").GetString();
            log.LogDebug($"Reader descriptor = {readerDescriptor}");

            // Get Identity for Project Admins
            var admins = await Project.GetIdentityForGroupAsync(_organizationName, projectName, "Project Administrators", _pat);
            adminDescriptor = admins.RootElement.GetProperty("value").EnumerateArray().First().GetProperty("descriptor").GetString();
            log.LogDebug($"Project Administrators descriptor = {adminDescriptor}");

            // Get Identity for Project Collection Admins
            var collectionAdmins = await Project.GetIdentityForOrganizationAsync(_organizationName, "Project Collection Administrators", _pat);
            projectCollectionAdminDescriptor = collectionAdmins.RootElement.GetProperty("value").EnumerateArray().First().GetProperty("descriptor").GetString();
            log.LogDebug($"Project Collection Administrators descriptor = {projectCollectionAdminDescriptor}");

            // Get or create Repository
            JsonDocument currentRepository = await Repository.GetRepositoryByNameAsync(_organizationUrl, projectName, repoName, _pat);
            try
            {
                repoId = currentRepository.RootElement.GetProperty("id").GetString();
                log.LogDebug($"Found existing Repo {currentRepository.RootElement.GetProperty("name").GetString()}, id: {repoId}");
            }
            catch (KeyNotFoundException)
            {
                currentRepository = await Repository.CreateRepositoryAsync(_organizationUrl, projectId, repoName, _pat);
                repoId = currentRepository.RootElement.GetProperty("id").GetString();
                log.LogDebug($"Created new Repo {currentRepository.RootElement.GetProperty("name").GetString()}, id: {repoId}");
            }
            // do initial commit
            var commit = await Repository.InitialCommitAsync(_organizationUrl, repoId, _pat);
            log.LogDebug("Initial commit");

            // Create top branch
            var refIds = await Repository.GetGitRefs(_organizationUrl, projectId, repoId, _pat);
            var mainBranch = refIds.RootElement.GetProperty("value").EnumerateArray().First(r => r.GetProperty("name").GetString() == $"refs/heads/{Constants.MAINBRANCHNAME}");
            mainBranchRefId = mainBranch.GetProperty("objectId").GetString();

            var integInitBranch = await Repository.CreateBranchAsync(_organizationUrl, projectId, repoId, "integ/init", mainBranchRefId, _pat);
            var maintInitBranch = await Repository.CreateBranchAsync(_organizationUrl, projectId, repoId, "maint/init", mainBranchRefId, _pat);
            var taskInitBranch = await Repository.CreateBranchAsync(_organizationUrl, projectId, repoId, "task/init", mainBranchRefId, _pat);

            var acls = await Repository.GetACLforRepoAsync(_organizationUrl, projectId, repoId, contributorDescriptor, _pat);
            // Deny CreateBranch on Repo for Contributors
            var resultACERepo = await Repository.SetACEforRepoAsync(_organizationUrl, projectId, repoId, null, contributorDescriptor, 0, GitPermissions.CREATEBRANCH, _pat);

            // grant create branch on integ/*
            var resultInteg = await Repository.SetACEforRepoAsync(_organizationUrl, projectId, repoId, "integ", contributorDescriptor, GitPermissions.CREATEBRANCH, 0, _pat);
            // grant create branch on maint/*
            var resultMaint = await Repository.SetACEforRepoAsync(_organizationUrl, projectId, repoId, "maint", contributorDescriptor, GitPermissions.CREATEBRANCH, 0, _pat);
            // grant create branch on maint/*
            var resultTask = await Repository.SetACEforRepoAsync(_organizationUrl, projectId, repoId, "task", contributorDescriptor, GitPermissions.CREATEBRANCH, 0, _pat);

            var minReviewer = await Policies.CreateOrUpdateBranchPolicy(_organizationUrl, projectName, repoId, "integ", PolicyTypes.MINIMUM_NUMBER_OF_REVIEWERS, _pat);
            var linkedWI = await Policies.CreateOrUpdateBranchPolicy(_organizationUrl, projectName, repoId, "integ", PolicyTypes.WORK_ITEM_LINKING, _pat);
            var gitCaseEnforcementPolicy = await Policies.CreateOrUpdateBranchPolicy(_organizationUrl, projectName, repoId, "", PolicyTypes.GIT_REPO_SETTINGS, _pat);
            var gitMaxPathLengthPolicy = await Policies.CreateOrUpdateBranchPolicy(_organizationUrl, projectName, repoId, "", PolicyTypes.PATH_LENGTH_RESTRICTION, _pat);
            var gitFileSizePolicy = await Policies.CreateOrUpdateBranchPolicy(_organizationUrl, projectName, repoId, "", PolicyTypes.FILE_SIZE_RESTRICTION, _pat);

            // Delete Contribute to PullRequests for Reader
            var deletePermission = await Repository.DeletePermissionAsync(_organizationUrl, $"repoV2/{projectId}", readerDescriptor, GitPermissions.PULLREQUESTCONTRIBUTE, _pat);

            // grant Bypass Policy on PR for Admins
            var resultPRBypass = await Repository.SetACEforRepoAsync(_organizationUrl, $"repoV2/{projectId}/", adminDescriptor, GitPermissions.PULLREQUESTBYPASSPOLICY, 0, _pat);
            // grant bypass policy when pushing for admins
            var resultPushBypass = await Repository.SetACEforRepoAsync(_organizationUrl, $"repoV2/{projectId}/", adminDescriptor, GitPermissions.POLICYEXEMPT, 0, _pat);
            // grant force push policy when pushing for admins
            var resultforcePush = await Repository.SetACEforRepoAsync(_organizationUrl, $"repoV2/{projectId}/", projectCollectionAdminDescriptor, GitPermissions.FORCEPUSH, 0, _pat);

            // grant force push policy when pushing for contributors
            var resultforcePushContrib = await Repository.SetACEforRepoAsync(_organizationUrl, projectId, repoId, "task", contributorDescriptor, GitPermissions.FORCEPUSH, 0, _pat);
            // grant force push policy when pushing for project admins
            var resultforcePushAdmins = await Repository.SetACEforRepoAsync(_organizationUrl, projectId, repoId, "task", adminDescriptor, GitPermissions.FORCEPUSH, 0, _pat);

            log.LogInformation($"*** Function {Constants.SETUP_REPO} completed successfully ***");
        }
    }
}
