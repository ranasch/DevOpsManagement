namespace DevOpsManagement.DevOpsAPI
{
    using Flurl;
    using Flurl.Http;
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net.Http;
    using System.Text.Json;
    using System.Threading.Tasks;

    public static class Repository
    {
        public static async Task CreateCompliantRepositoryAsync(string organizationName, Url organizationUrl, string projectName, string repoName, string projectId, string pat)
        {
            string repoId;
            string contributorDescriptor;
            string readerDescriptor;
            string adminDescriptor;
            string mainBranchRefId;
            string projectCollectionAdminDescriptor;

            // Get Identity for Contributors
            var contributor = await Project.GetIdentityForGroupAsync(organizationName, projectName, "Contributors", pat);
            contributorDescriptor = contributor.RootElement.GetProperty("value").EnumerateArray().First().GetProperty("descriptor").GetString();

            // Get Identity for Readers
            var reader = await Project.GetIdentityForGroupAsync(organizationName, projectName, "Readers", pat);
            readerDescriptor = reader.RootElement.GetProperty("value").EnumerateArray().First().GetProperty("descriptor").GetString();

            // Get Identity for Project Admins
            var admins = await Project.GetIdentityForGroupAsync(organizationName, projectName, "Project Administrators", pat);
            adminDescriptor = admins.RootElement.GetProperty("value").EnumerateArray().First().GetProperty("descriptor").GetString();

            // Get Identity for Project Collection Admins
            var collectionAdmins = await Project.GetIdentityForOrganizationAsync(organizationName, "Project Collection Administrators", pat);
            projectCollectionAdminDescriptor = collectionAdmins.RootElement.GetProperty("value").EnumerateArray().First().GetProperty("descriptor").GetString();

            // Get or create Repository
            JsonDocument currentRepository = await Repository.GetRepositoryByNameAsync(organizationUrl, projectName, repoName, pat);
            try
            {
                repoId = currentRepository.RootElement.GetProperty("id").GetString();
            }
            catch (KeyNotFoundException)
            {
                currentRepository = await Repository.CreateRepositoryAsync(organizationUrl, projectId, repoName, pat);
                repoId = currentRepository.RootElement.GetProperty("id").GetString();
            }
            // do initial commit
            var commit = await Repository.InitialCommitAsync(organizationUrl, repoId, pat);

            // Create top branch
            var refIds = await Repository.GetGitRefs(organizationUrl, projectId, repoId, pat);
            var mainBranch = refIds.RootElement.GetProperty("value").EnumerateArray().First(r => r.GetProperty("name").GetString() == $"refs/heads/{Constants.MAINBRANCHNAME}");
            mainBranchRefId = mainBranch.GetProperty("objectId").GetString();

            var integInitBranch = await Repository.CreateBranchAsync(organizationUrl, projectId, repoId, "integ/init", mainBranchRefId, pat);
            var maintInitBranch = await Repository.CreateBranchAsync(organizationUrl, projectId, repoId, "maint/init", mainBranchRefId, pat);
            var taskInitBranch = await Repository.CreateBranchAsync(organizationUrl, projectId, repoId, "task/init", mainBranchRefId, pat);

            var acls = await Repository.GetACLforRepoAsync(organizationUrl, projectId, repoId, contributorDescriptor, pat);
            // Deny CreateBranch on Repo for Contributors
            var resultACERepo = await Repository.SetACEforRepoAsync(organizationUrl, projectId, repoId, null, contributorDescriptor, 0, GitPermissions.CREATEBRANCH, pat);

            // grant create branch on integ/*
            var resultInteg = await Repository.SetACEforRepoAsync(organizationUrl, projectId, repoId, "integ", contributorDescriptor, GitPermissions.CREATEBRANCH, 0, pat);
            // grant create branch on maint/*
            var resultMaint = await Repository.SetACEforRepoAsync(organizationUrl, projectId, repoId, "maint", contributorDescriptor, GitPermissions.CREATEBRANCH, 0, pat);
            // grant create branch on maint/*
            var resultTask = await Repository.SetACEforRepoAsync(organizationUrl, projectId, repoId, "task", contributorDescriptor, GitPermissions.CREATEBRANCH, 0, pat);

            var minReviewer = await Policies.CreateOrUpdateBranchPolicy(organizationUrl, projectName, repoId, "integ", PolicyTypes.MINIMUM_NUMBER_OF_REVIEWERS, pat);
            var linkedWI = await Policies.CreateOrUpdateBranchPolicy(organizationUrl, projectName, repoId, "integ", PolicyTypes.WORK_ITEM_LINKING, pat);
            var gitCaseEnforcementPolicy = await Policies.CreateOrUpdateBranchPolicy(organizationUrl, projectName, repoId, "", PolicyTypes.GIT_REPO_SETTINGS, pat);
            var gitMaxPathLengthPolicy = await Policies.CreateOrUpdateBranchPolicy(organizationUrl, projectName, repoId, "", PolicyTypes.PATH_LENGTH_RESTRICTION, pat);
            var gitFileSizePolicy = await Policies.CreateOrUpdateBranchPolicy(organizationUrl, projectName, repoId, "", PolicyTypes.FILE_SIZE_RESTRICTION, pat);

            // Delete Contribute to PullRequests for Reader
            var deletePermission = await Repository.DeletePermissionAsync(organizationUrl, $"repoV2/{projectId}", readerDescriptor, GitPermissions.PULLREQUESTCONTRIBUTE, pat);

            // grant Bypass Policy on PR for Admins
            var resultPRBypass = await Repository.SetACEforRepoAsync(organizationUrl, $"repoV2/{projectId}/", adminDescriptor, GitPermissions.PULLREQUESTBYPASSPOLICY, 0, pat);
            // grant bypass policy when pushing for admins
            var resultPushBypass = await Repository.SetACEforRepoAsync(organizationUrl, $"repoV2/{projectId}/", adminDescriptor, GitPermissions.POLICYEXEMPT, 0, pat);
            // grant force push policy when pushing for admins
            var resultforcePush = await Repository.SetACEforRepoAsync(organizationUrl, $"repoV2/{projectId}/", projectCollectionAdminDescriptor, GitPermissions.FORCEPUSH, 0, pat);

            // grant force push policy when pushing for contributors
            var resultforcePushContrib = await Repository.SetACEforRepoAsync(organizationUrl, projectId, repoId, "task", contributorDescriptor, GitPermissions.FORCEPUSH, 0, pat);
            // grant force push policy when pushing for project admins
            var resultforcePushAdmins = await Repository.SetACEforRepoAsync(organizationUrl, projectId, repoId, "task", adminDescriptor, GitPermissions.FORCEPUSH, 0, pat);
        }
        public static async Task<JsonDocument> GetRepositoryByNameAsync(Url organization, string projectName, string repoName, string pat)
        {
            // check, if repo already exists
            var queryRepos = await GetRepositoriesAsync(organization, projectName, pat);
            string repoId = null;
            foreach (var repo in queryRepos.RootElement.GetProperty("value").EnumerateArray())
            {
                if (repo.GetProperty("name").GetString() == repoName)
                {
                    repoId = repo.GetProperty("id").GetString();
                }
            }

            if (repoId != null)
            {
                // https://{{coreServer}}/{{organization}}/{{project}}/_apis/git/repositories/{{repositoryId}}?api-version={{api-version}}
                var queryResponse = await $"{organization}"
                    .AppendPathSegment(projectName)
                    .AppendPathSegment($"_apis/git/repositories/{repoId}")
                    .SetQueryParam("api-version", Constants.APIVERSION)
                    .WithBasicAuth(string.Empty, pat)
                    .GetAsync();

                if (queryResponse.ResponseMessage.IsSuccessStatusCode)
                    return JsonDocument.Parse(await queryResponse.ResponseMessage.Content.ReadAsStringAsync());
            }

            return JsonDocument.Parse("{}");

        }

        public static async Task<JsonDocument> GetRepositoriesAsync(Url organization, string projectName, string pat)
        {
            // https://{{coreServer}}/{{organization}}/{{project}}/_apis/git/repositories?api-version={{api-version}}
            var queryResponse = await $"{organization}"
                .AppendPathSegment(projectName)
                .AppendPathSegment($"_apis/git/repositories")
                .SetQueryParam("api-version", Constants.APIVERSION)
                .WithBasicAuth(string.Empty, pat)
                .GetAsync();

            if (queryResponse.ResponseMessage.IsSuccessStatusCode)
                return JsonDocument.Parse(await queryResponse.ResponseMessage.Content.ReadAsStringAsync());
            else
                return JsonDocument.Parse("{}");

        }

        public static async Task<JsonDocument> CreateRepositoryAsync(Url organization, string projectId, string repoName, string pat)
        {
            var payload = new
            {
                name = repoName,
                project = new
                {
                    id = projectId
                }
            };

            // https://{{coreServer}}/{{organization}}/{{projectId}}/_apis/git/repositories?api-version={{api-version}}
            var queryResponse = await $"{organization}"
                .AppendPathSegment(projectId)
                .AppendPathSegment($"_apis/git/repositories")
                .SetQueryParam("api-version", Constants.APIVERSION)
                .WithBasicAuth(string.Empty, pat)
                .PostJsonAsync(payload);

            if (queryResponse.ResponseMessage.IsSuccessStatusCode)
                return JsonDocument.Parse(await queryResponse.ResponseMessage.Content.ReadAsStringAsync());
            else
                return JsonDocument.Parse("{}");

        }

        public static async Task<JsonDocument> GetGitRefs(Url organization, string projectId, string repoId, string pat)
        {
            //GET https://dev.azure.com/{organization}/{project}/_apis/git/repositories/{repositoryId}/refs?api-version=6.1-preview.1
            var queryResponse = await $"{organization}"
                .AppendPathSegment(projectId)
                .AppendPathSegment($"_apis/git/repositories")
                .AppendPathSegment(repoId)
                .AppendPathSegment("refs")
                .SetQueryParam("filter", "heads")
                .SetQueryParam("api-version", Constants.APIVERSION)
                .WithBasicAuth(string.Empty, pat)
                .GetAsync();

            if (queryResponse.ResponseMessage.IsSuccessStatusCode)
                return JsonDocument.Parse(await queryResponse.ResponseMessage.Content.ReadAsStringAsync());
            else
                return JsonDocument.Parse("{}");

        }

        public static async Task<JsonDocument> InitialCommitAsync(Url organization, string repoId, string pat)
        {
            // POST https://dev.azure.com/pocit/zfAspiceReference/_apis/git/repositories/8c8d4a20-b7af-409b-8e34-827bc3cc8e32/pushes
            var payload = new
            {
                refUpdates = new[] {
                  new {
                    name= $"refs/heads/{Constants.MAINBRANCHNAME}",
                    oldObjectId= "0000000000000000000000000000000000000000"
                  }
                },
                commits = new[] {
                new {
                    comment= "Initial commit.",
                    changes=new []
                    {
                        new
                        {
                            changeType="add",
                            item=new
                            {
                                path="/readme.md"
                            },
                            newContent=new
                            {
                                content="initial file",
                                contentType="rawtext"
                            }
                        }
                    }
                }
                }
            };
            var test = JsonSerializer.Serialize(payload).GetStringAsync();

            // POST https://dev.azure.com/fabrikam/_apis/git/repositories/{repositoryId}/pushes?api-version=6.1-preview.2
            var queryResponse = await $"{organization}"
               .AppendPathSegment($"_apis/git/repositories")
               .AppendPathSegment(repoId)
               .AppendPathSegment("pushes")
               .SetQueryParam("api-version", "6.1-preview.2")
               .WithBasicAuth(string.Empty, pat)
               .PostJsonAsync(payload);

            if (queryResponse.ResponseMessage.IsSuccessStatusCode)
                return JsonDocument.Parse(await queryResponse.ResponseMessage.Content.ReadAsStringAsync());
            else
                return JsonDocument.Parse("{}");
        }

        public static async Task<JsonDocument> CreateBranchAsync(Url organization, string projectId, string repoId, string branchName, string parentBranchRefId, string pat)
        {
            var payload = new[]
            {
                new{
                name = $"refs/heads/{branchName}",
                newObjectId = parentBranchRefId,
                oldObjectId = "0000000000000000000000000000000000000000"
                }
            };

            // https://{{coreServer}}/{{organization}}/{{projectId}}/_apis/git/repositories?api-version={{api-version}}
            var queryResponse = await $"{organization}"
               .AppendPathSegment(projectId)
               .AppendPathSegment($"_apis/git/repositories")
               .AppendPathSegment(repoId)
               .AppendPathSegment("refs")
               .SetQueryParam("api-version", Constants.APIVERSION)
               .WithBasicAuth(string.Empty, pat)
               .PostJsonAsync(payload);

            if (queryResponse.ResponseMessage.IsSuccessStatusCode)
                return JsonDocument.Parse(await queryResponse.ResponseMessage.Content.ReadAsStringAsync());
            else
                return JsonDocument.Parse("{}");
        }

        public static async Task<JsonDocument> GetACLforRepoAsync(Url organization, string projectId, string repoId, string groupSecDescriptor, string pat)
        {

            // https://{{coreServer}}/{{organization}}/_apis/accesscontrollists/{{securityNamespaceId}}?token=repoV2/{{projectId}}/{{repositoryId}}&descriptors={{contribSecDescriptor}}&includeExtendedInfo=false&recurse=false&api-version={{api-version}}
            var queryResponse = await $"{organization}"
                .AppendPathSegment($"_apis/accesscontrollists")
                .AppendPathSegment(Constants.SecurityNamespaceGitRepo)
                .SetQueryParam("token", $"repoV2/{projectId}/{repoId}")
                .SetQueryParam("descriptors", groupSecDescriptor)
                .SetQueryParam("includeExtendedInfo", "false")
                .SetQueryParam("recurse", "false")
                .SetQueryParam("api-version", Constants.APIVERSION)
                .WithBasicAuth(string.Empty, pat)
                .GetAsync();

            if (queryResponse.ResponseMessage.IsSuccessStatusCode)
                return JsonDocument.Parse(await queryResponse.ResponseMessage.Content.ReadAsStringAsync());
            else
                return JsonDocument.Parse("{}");

        }

        public static async Task<JsonDocument> SetACEforRepoAsync(Url organization, string projectId, string repoId, string branchName, string groupSecDescriptor, long allowMask, long denyMask, string pat)
        {
            var token = GitUtils.CalculateSecurableFromBranchName(Guid.Parse(projectId), Guid.Parse(repoId), branchName);

            return await SetACEforRepoAsync(organization, token, groupSecDescriptor, allowMask, denyMask, pat);
        }

        public static async Task<JsonDocument> SetACEforRepoAsync(Url organization, string token, string groupSecDescriptor, long allowMask, long denyMask, string pat)
        {
            var ace = new ACE()
            {
                token = token,
                merge = true,
                accessControlEntries = new Accesscontrolentry[] {
                    new Accesscontrolentry() {
                        allow = allowMask, deny = denyMask, descriptor = groupSecDescriptor, extendedInfo = new Extendedinfo()
                            {
                            effectiveAllow=allowMask,
                            effectiveDeny=denyMask,
                            inheritedAllow=allowMask,
                            inheritedDeny=denyMask
                            }
                    }
                }
            };

            // https://{{coreServer}}/{{organization}}/_apis/AccessControlEntries/{{securityNamespaceId}}api-version={{api-version}}
            var queryResponse = await $"{organization}"
               .AppendPathSegment($"_apis/AccessControlEntries")
               .AppendPathSegment(Constants.SecurityNamespaceGitRepo)
               .SetQueryParam("api-version", Constants.APIVERSION)
               .WithBasicAuth(string.Empty, pat)
               .PostJsonAsync(ace);

            if (queryResponse.ResponseMessage.IsSuccessStatusCode)
                return JsonDocument.Parse(await queryResponse.ResponseMessage.Content.ReadAsStringAsync());
            else
                return JsonDocument.Parse("{}");

        }

        public static async Task<JsonDocument> DeletePermissionAsync(Url organization, string token, string groupSecDescriptor, long permission, string pat)
        {
            // DELETE https://dev.azure.com/{organization}/_apis/permissions/{securityNamespaceId}/{permissions}?descriptor={descriptor}&token={token}&api-version=6.0

            var queryResponse = await $"{organization}"
               .AppendPathSegment($"_apis/permissions")
               .AppendPathSegment(Constants.SecurityNamespaceGitRepo)
               .AppendPathSegment(permission)
               .SetQueryParam("descriptor", groupSecDescriptor)
               .SetQueryParam("token", token)
               .SetQueryParam("api-version", Constants.APIVERSION)
               .WithBasicAuth(string.Empty, pat)
               .DeleteAsync();

            if (queryResponse.ResponseMessage.IsSuccessStatusCode)
                return JsonDocument.Parse(await queryResponse.ResponseMessage.Content.ReadAsStringAsync());
            else
                return JsonDocument.Parse("{}");

        }

        class ACE
        {
            public string token { get; set; }
            public bool merge { get; set; }
            public Accesscontrolentry[] accessControlEntries { get; set; }
        }

        class Accesscontrolentry
        {
            public string descriptor { get; set; }
            public long allow { get; set; }
            public long deny { get; set; }
            public Extendedinfo extendedInfo { get; set; }
        }

        public class Extendedinfo
        {
            public long effectiveAllow { get; set; }
            public long effectiveDeny { get; set; }
            public long inheritedAllow { get; set; }
            public long inheritedDeny { get; set; }
        }
    }

}
