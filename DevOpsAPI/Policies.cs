namespace DevOpsManagement.DevOpsAPI
{
    using Flurl;
    using Flurl.Http;
    using System.IO;
    using System.Linq;
    using System.Text.Json;
    using System.Threading.Tasks;

    public static class Policies
    {

        public static async Task<JsonElement> CheckBranchPolicyExistAsync(Url organization, string projectName, string repositoryId, string branchName, string policy, string pat)
        {
            var refName = $"refs/heads/{branchName}";
            //int policyId = 0;
            JsonElement currentPolicy = new JsonElement();

            var branchPolicies = await GetBranchPolicyAsync(organization, projectName, policy, pat);
            try
            {
                foreach (var val in branchPolicies.RootElement.GetProperty("value").EnumerateArray())
                {
                    if (val.GetProperty("settings")
                        .GetProperty("scope")
                        .EnumerateArray()
                        .Any(p => p.GetProperty("repositoryId").GetString() == repositoryId))
                    {
                        currentPolicy = val;
                        break;
                    }
                }
            }
            catch { }

            //return policyId;
            return currentPolicy;
        }

        public static async Task<JsonDocument> GetBranchPolicyAsync(Url organization, string projectName, string policy, string pat)
        {
            // GET  https://dev.azure.com/{organization}/{project}/_apis/policy/configurations?scope={scope}&$top={$top}&continuationToken={continuationToken}&policyType={policyType}&api-version=6.0

            var queryResponse = await $"{organization}"
                .AppendPathSegment(projectName)
                .AppendPathSegment("_apis/policy/configurations")
                .SetQueryParam("policyType", policy)
                .SetQueryParam("api-version", Constants.APIVERSION)
                .WithBasicAuth(string.Empty, pat)
                .AllowAnyHttpStatus()
                .GetAsync();

            if (queryResponse.ResponseMessage.IsSuccessStatusCode)
                return JsonDocument.Parse(await queryResponse.ResponseMessage.Content.ReadAsStringAsync());

            return JsonDocument.Parse("{}");
        }

        public static async Task<JsonDocument> CreateOrUpdateBranchPolicy(Url organization, string projectName, string repositoryId, string branchName, string policy, string pat)
        {
            var refName = $"refs/heads/{branchName}";
            //var policyId = await CheckBranchPolicyExistAsync(organization, projectName, repositoryId, branchName, policy, pat);
            var existingPolicy = await CheckBranchPolicyExistAsync(organization, projectName, repositoryId, branchName, policy, pat);
            int policyId = 0;
            try
            {
                policyId = existingPolicy.GetProperty("id").GetInt32();
            }
            catch { }

            if (policyId > 0)
            {
                // UPDATE existing policy
                // PUT https://dev.azure.com/{organization}/{project}/_apis/policy/configurations/{configurationId}?api-version=6.0                
                Configuration configPolicy = null;
                switch (policy)
                {
                    case PolicyTypes.MINIMUM_NUMBER_OF_REVIEWERS:
                        configPolicy = new BranchUpdateConfiguration()
                        {
                            isBlocking = true,
                            isEnabled = true,
                            type = new Type() { id = policy },
                            settings = new BranchSettings()
                            {
                                allowDownvotes = false,
                                blockLastPusherVote = false,
                                creatorVoteCounts = false,
                                minimumApproverCount = 1,
                                requireVoteOnLastIteration = false,
                                resetOnSourcePush = false,
                                resetRejectionsOnSourcePush = false,
                                scope = new BranchScope[]
                                {
                            new BranchScope() {
                                repositoryId = repositoryId,
                                matchKind = "Prefix",
                                refName = refName
                            }
                                }
                            }
                        };
                        break;
                    case PolicyTypes.FILE_SIZE_RESTRICTION:
                        configPolicy = new RepoCreateConfiguration()
                        {
                            type = new Type() { id = policy },
                            isBlocking = true,
                            isEnabled = true,
                            settings = new RepoSettings()
                            {
                                maximumGitBlobSizeInBytes = 104857600, // as defined by zf
                                scope = new RepositoryScope[]
                                {
                                        new RepositoryScope() {
                                            repositoryId = repositoryId
                                        }
                                },
                                useUncompressedSize = false
                            }
                        };
                        break;
                    case PolicyTypes.PATH_LENGTH_RESTRICTION:
                        configPolicy = new RepoCreateConfiguration()
                        {
                            type = new Type() { id = policy },
                            isBlocking = true,
                            isEnabled = true,
                            settings = new RepoSettings()
                            {
                                maxPathLength = 254,    // as defined by zf
                                scope = new RepositoryScope[]
                                {
                                            new RepositoryScope() {
                                                repositoryId = repositoryId
                                            }
                                },
                                useUncompressedSize = false
                            }
                        };
                        break;
                    case PolicyTypes.WORK_ITEM_LINKING:
                        configPolicy = new BranchCreateConfiguration()
                        {
                            type = new Type() { id = policy },
                            isBlocking = true,
                            isEnabled = true,
                            settings = new BranchSettings()
                            {
                                scope = new BranchScope[]
                                {
                                        new BranchScope() {
                                            repositoryId = repositoryId,
                                            matchKind = "Prefix",
                                            refName = refName
                                        }
                                }
                            }
                        };
                        break;

                    default:
                        // unknow = ignore
                        return JsonDocument.Parse("{}");
                }

                return await UpdateBranchPolicy(organization, projectName, repositoryId, policyId, configPolicy, pat);
            }
            else
            {
                // POST https://dev.azure.com/{organization}/{project}/_apis/policy/configurations?api-version=6.0
                Configuration configPolicy = null;
                switch (policy)
                {
                    case PolicyTypes.MINIMUM_NUMBER_OF_REVIEWERS:
                        configPolicy = new BranchCreateConfiguration()
                        {
                            type = new Type() { id = policy },
                            revision = 1,
                            isDeleted = false,
                            isBlocking = true,
                            isEnabled = true,
                            settings = new BranchSettings()
                            {
                                allowDownvotes = false,
                                blockLastPusherVote = false,
                                creatorVoteCounts = false,
                                minimumApproverCount = 1,
                                requireVoteOnLastIteration = false,
                                resetOnSourcePush = false,
                                resetRejectionsOnSourcePush = false,
                                scope = new BranchScope[]
                                {
                            new BranchScope() {
                                repositoryId = repositoryId,
                                matchKind = "Prefix",
                                refName = refName
                            }
                                }
                            }
                        };
                        break;
                    case PolicyTypes.WORK_ITEM_LINKING:
                        configPolicy = new BranchCreateConfiguration()
                        {
                            type = new Type() { id = policy },
                            revision = 1,
                            isDeleted = false,
                            isBlocking = true,
                            isEnabled = true,
                            settings = new BranchSettings()
                            {
                                scope = new BranchScope[]
                                {
                                        new BranchScope() {
                                            repositoryId = repositoryId,
                                            matchKind = "Prefix",
                                            refName = refName
                                        }
                                }
                            }
                        };
                        break;
                    case PolicyTypes.GIT_REPO_SETTINGS:
                        configPolicy = new RepoCreateConfiguration()
                        {
                            type = new Type() { id = policy },
                            revision = 1,
                            isDeleted = false,
                            isBlocking = true,
                            isEnabled = true,
                            settings = new RepoSettings()
                            {
                                enforceConsistentCase = true,
                                scope = new RepositoryScope[]
                                {
                                        new RepositoryScope() {
                                            repositoryId = repositoryId
                                        }
                                },
                            }
                        };
                        break;
                    case PolicyTypes.FILE_SIZE_RESTRICTION:
                        configPolicy = new RepoCreateConfiguration()
                        {
                            type = new Type() { id = policy },
                            revision = 1,
                            isDeleted = false,
                            isBlocking = true,
                            isEnabled = true,
                            settings = new RepoSettings()
                            {
                                maximumGitBlobSizeInBytes = 104857600, // as defined by zf
                                scope = new RepositoryScope[]
                                {
                                        new RepositoryScope() {
                                            repositoryId = repositoryId
                                        }
                                },
                                useUncompressedSize = false
                            }
                        };
                        break;
                    case PolicyTypes.PATH_LENGTH_RESTRICTION:
                        configPolicy = new RepoCreateConfiguration()
                        {
                            type = new Type() { id = policy },
                            revision = 1,
                            isDeleted = false,
                            isBlocking = true,
                            isEnabled = true,
                            settings = new RepoSettings()
                            {
                                maxPathLength = 254, // as defined by zf
                                scope = new RepositoryScope[]
                                {
                                            new RepositoryScope() {
                                                repositoryId = repositoryId
                                            }
                                },
                                useUncompressedSize = false
                            }
                        };
                        break;
                }
                return await CreateBranchPolicy(organization, projectName, repositoryId, configPolicy, pat);
            }

            return JsonDocument.Parse("{}");
        }

        private static async Task<JsonDocument> CreateBranchPolicy(Url organization, string projectName, string repositoryId, Configuration policy, string pat)
        {
            var queryResponse = await $"{organization}"
               .AppendPathSegment(projectName)
               .AppendPathSegment($"_apis/policy/configurations")
               .SetQueryParam("api-version", Constants.APIVERSION)
               .WithBasicAuth(string.Empty, pat)
               .AllowAnyHttpStatus()
               .PostJsonAsync(policy);

            if (queryResponse.ResponseMessage.IsSuccessStatusCode)
                return JsonDocument.Parse(await queryResponse.ResponseMessage.Content.ReadAsStringAsync());
            else
                return JsonDocument.Parse("{}");
        }

        private static async Task<JsonDocument> UpdateBranchPolicy(Url organization, string projectName, string repositoryId, int configId, Configuration configuration, string pat)
        {
            var queryResponse = await $"{organization}"
               .AppendPathSegment(projectName)
               .AppendPathSegment($"_apis/policy/configurations")
               .AppendPathSegment(configId)
               .SetQueryParam("api-version", Constants.APIVERSION)
               .WithBasicAuth(string.Empty, pat)
               .AllowAnyHttpStatus()
               .PutJsonAsync(configuration);

            if (queryResponse.ResponseMessage.IsSuccessStatusCode)
                return JsonDocument.Parse(await queryResponse.ResponseMessage.Content.ReadAsStringAsync());
            else
                return JsonDocument.Parse("{}");
        }

        #region Azure DevOps API model

        abstract class Configuration
        {
            public Type type { get; set; }
            public bool isBlocking { get; set; }
            public bool isEnabled { get; set; }
        }

        class BranchUpdateConfiguration : Configuration
        {
            public BranchSettings settings { get; set; }
        }

        class BranchCreateConfiguration : BranchUpdateConfiguration
        {
            public int revision { get; set; }
            public bool isDeleted { get; set; }
        }


        class RepoUpdateConfiguration : Configuration
        {
            public RepoSettings settings { get; set; }
        }

        class RepoCreateConfiguration : RepoUpdateConfiguration
        {
            public Type type { get; set; }
            public int revision { get; set; }
            public bool isDeleted { get; set; }
        }

        class Type
        {
            public string id { get; set; }
        }

        class BranchSettings
        {
            public bool allowDownvotes { get; set; }
            public bool blockLastPusherVote { get; set; }
            public bool creatorVoteCounts { get; set; }
            public bool requireVoteOnLastIteration { get; set; }
            public bool resetOnSourcePush { get; set; }
            public bool resetRejectionsOnSourcePush { get; set; }
            public int minimumApproverCount { get; set; }
            public BranchScope[] scope { get; set; }
        }

        class RepoSettings
        {
            public bool enforceConsistentCase { get; set; }
            public int maxPathLength { get; set; }
            public long maximumGitBlobSizeInBytes { get; set; }
            public bool useUncompressedSize { get; set; }
            public RepositoryScope[] scope { get; set; }
        }

        class BranchScope
        {
            public string repositoryId { get; set; }
            public string refName { get; set; }
            public string matchKind { get; set; }
        }

        class RepositoryScope
        {
            public string repositoryId { get; set; }
        }
        #endregion
    }

}
