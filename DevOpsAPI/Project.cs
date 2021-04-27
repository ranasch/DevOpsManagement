namespace DevOpsManagement.DevOpsAPI
{
    using Flurl;
    using Flurl.Http;
    using System;
    using System.Linq;
    using System.Text.Json;
    using System.Threading.Tasks;
   
    public static class Project
    {
        public static async Task<JsonDocument> GetProjectsAsync(Url _organization, string pat)
        {
            var queryResponse = await $"{_organization}"
                .AppendPathSegment("_apis/projects")
                .SetQueryParam("api-version", Constants.APIVERSION)
                .WithBasicAuth(string.Empty, pat)
                .AllowAnyHttpStatus()
                .GetAsync();

            if (queryResponse.ResponseMessage.IsSuccessStatusCode)
                return JsonDocument.Parse(await queryResponse.ResponseMessage.Content.ReadAsStringAsync());
            else
                return JsonDocument.Parse("{}");
        }
        public static async Task<JsonDocument> GetProjectStatusAsync(Url _organization, string operationId, string pat)
        {
            // GET https://dev.azure.com/{organization}/_apis/operations/{operationId}?api-version=6.1-preview.1
            var queryResponse = await $"{_organization}"
                .AppendPathSegment("_apis/operations")
                .AppendPathSegment(operationId)
                .SetQueryParam("api-version", Constants.APIVERSION)
                .WithBasicAuth(string.Empty, pat)
                .AllowAnyHttpStatus()
                .GetAsync();

            if (queryResponse.ResponseMessage.IsSuccessStatusCode)
                return JsonDocument.Parse(await queryResponse.ResponseMessage.Content.ReadAsStringAsync());
            else
                return JsonDocument.Parse("{}");
        }

        public static async Task<JsonDocument> GetProjectByNameAsync(Url _organization, string projectName, string pat)
        {
            var queryResponse = await $"{_organization}"
                .AppendPathSegment($"_apis/projects/{projectName}")
                .SetQueryParam("api-version", Constants.APIVERSION)
                .WithBasicAuth(string.Empty, pat)
                .AllowAnyHttpStatus()
                .GetAsync();

            if (queryResponse.ResponseMessage.IsSuccessStatusCode)
                return JsonDocument.Parse(await queryResponse.ResponseMessage.Content.ReadAsStringAsync());
            else
                return JsonDocument.Parse("{}");

        }

        public static async Task<JsonDocument> GetIdentityForGroupAsync(string organizationName, string projectName, string groupName, string pat)
        {
            //https://vssps.dev.azure.com/{{organization}}/_apis/identities?searchFilter=General&filterValue=[{{project}}]\Contributors&queryMembership=None&api-version={{api-version-preview}}
            var queryResponse = await $"https://vssps.dev.azure.com/{organizationName}"
                .AppendPathSegment($"_apis/identities")
                .SetQueryParam("searchFilter", "General")
                .SetQueryParam("filterValue", $"[{projectName}]\\{groupName}")
                .SetQueryParam("queryMembership", "None")
                .SetQueryParam("api-version", Constants.APIVERSION)
                .WithBasicAuth(string.Empty, pat)
                .AllowAnyHttpStatus()
                .GetAsync();

            if (queryResponse.ResponseMessage.IsSuccessStatusCode)
                return JsonDocument.Parse(await queryResponse.ResponseMessage.Content.ReadAsStringAsync());
            else
                return JsonDocument.Parse("{}");
        }

        public static async Task<JsonDocument> GetIdentityForOrganizationAsync(string organizationName, string groupName, string pat)
        {
            //https://vssps.dev.azure.com/{{organization}}/_apis/identities?searchFilter=General&filterValue=[{{project}}]\Contributors&queryMembership=None&api-version={{api-version-preview}}
            var queryResponse = await $"https://vssps.dev.azure.com/{organizationName}"
                .AppendPathSegment($"_apis/identities")
                .SetQueryParam("searchFilter", "General")
                .SetQueryParam("filterValue", $"[{organizationName}]\\{groupName}")
                .SetQueryParam("queryMembership", "None")
                .SetQueryParam("api-version", Constants.APIVERSION)
                .WithBasicAuth(string.Empty, pat)
                .AllowAnyHttpStatus()
                .GetAsync();

            if (queryResponse.ResponseMessage.IsSuccessStatusCode)
                return JsonDocument.Parse(await queryResponse.ResponseMessage.Content.ReadAsStringAsync());
            else
                return JsonDocument.Parse("{}");


        }

        public static async Task<string> CreateProjectsAsync(Url _organization, 
            string projectName, 
            string projectDescription,
            string processTemplateId,
            string pat)
        {
            var project = new
            {
                name = projectName,
                description = projectDescription,
                capabilities = new
                {
                    versioncontrol = new
                    {
                        sourceControlType="Git"
                    },
                    processTemplate= new
                    {
                        templateTypeId=processTemplateId
                    }
                }
            };

            var queryResponse = await $"{_organization}"
                .AppendPathSegment("_apis/projects")
                .SetQueryParam("api-version", "6.0")
                .WithBasicAuth(string.Empty, pat)
                .AllowAnyHttpStatus()
                .PostJsonAsync(project);

            if (queryResponse.ResponseMessage.IsSuccessStatusCode)
                return JsonDocument.Parse(await queryResponse.ResponseMessage.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetString();
            else
                return string.Empty;
        }


        public static async Task<JsonDocument> GetWorkItemById(string organizationName, string projectName, int workitemId, string pat)
        {
            // GET https://dev.azure.com/{organization}/{project}/_apis/wit/workitems/{id}?fields={fields}&asOf={asOf}&$expand={$expand}&api-version=6.1-preview.3
            var queryResponse = await $"https://dev.azure.com/{organizationName}"
               .AppendPathSegment(projectName)
               .AppendPathSegment($"_apis/wit/workitems/{workitemId}")
               .SetQueryParam("api-version", "6.0")
               .WithBasicAuth(string.Empty, pat)
               .AllowAnyHttpStatus()
               .GetAsync();

            if (queryResponse.ResponseMessage.IsSuccessStatusCode)
                return JsonDocument.Parse(await queryResponse.ResponseMessage.Content.ReadAsStringAsync());
            else
                return JsonDocument.Parse("{}");
        }

        public static async Task<int> GetMaxAzIdForEnvironment(string organizationName, string projectName, string projectTeam, string environment, string pat)
        {
            var wiql = new
            {
                query = "SELECT [Custom.AZP_ID] \n" +
            "FROM workitems \n" +
            "WHERE [System.TeamProject] = @project \n" +
            "       and [System.WorkItemType]=\"Project\" \n" +
            $"       and [Custom.SW4ZFOrga]=\"{environment}\" \n" +
            "ORDER BY [System.Id] DESC"
            };

                // POST https://dev.azure.com/{organization}/{project}/{team}/_apis/wit/wiql?timePrecision={timePrecision}&$top={$top}&api-version=4.1
                var queryResponse = await $"https://dev.azure.com/{organizationName}"
                   .AppendPathSegment(projectName)
                   .AppendPathSegment(projectTeam)
                   .AppendPathSegment($"_apis/wit/wiql")
                   .SetQueryParam("$top", "1")
                   .SetQueryParam("api-version", "6.0")
                   .WithBasicAuth(string.Empty, pat)
                   .AllowAnyHttpStatus()
                   .PostJsonAsync(wiql);

                var azid = 0;
            if (queryResponse.ResponseMessage.IsSuccessStatusCode)
            {
                try
                {
                    var jsonQueryResult = JsonDocument.Parse(await queryResponse.ResponseMessage.Content.ReadAsStringAsync());
                    var workItemWithmaxAzId = jsonQueryResult.RootElement.GetProperty("workItems").EnumerateArray().First().GetProperty("id").GetInt32();
                    var workItem = await GetWorkItemById(organizationName, projectName, workItemWithmaxAzId, pat);
                    azid = workItem.RootElement.GetProperty("fields").GetProperty("Custom.AZP_ID").GetInt32();
                }
                catch (InvalidOperationException ex)
                { azid = 0; }                
            }
            else
                return 0;

            return azid;
        }
    }
}
