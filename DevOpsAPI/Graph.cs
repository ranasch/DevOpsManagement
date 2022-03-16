namespace DevOpsManagement.DevOpsAPI
{
    using Flurl;
    using Flurl.Http;
    using System.Text.Json;
    using System.Threading.Tasks;

    public static class Graph
    {
        public static async Task<JsonDocument> CreateAzDevOpsGroupAsync(string orgaName,
            string projectDescriptor,
            string groupName,
            string [] groupsToJoinAsMember,
            string pat,
            string groupDescription = "")
        {
            // POST https://vssps.dev.azure.com/{{organization}}/_apis/graph/groups?scopeDescriptor=scp.ODU4ZTU0OTItYThkMC00OWEyLWI1NzAtNDNmZTY3ODJkYmJl&api-version=6.0-preview.1
            var group = new
            {
                displayName = groupName,
                description= groupDescription
            };
            var memberships = string.Join(",", groupsToJoinAsMember);

            var queryResponse = await $"https://vssps.dev.azure.com/{orgaName}"
                .AppendPathSegment("_apis/graph/groups")
                .SetQueryParam("scopeDescriptor", projectDescriptor)
                .SetQueryParam("groupDescriptors", memberships)
                .SetQueryParam("api-version", Constants.APIVERSION)
                .WithBasicAuth(string.Empty, pat)
                .PostJsonAsync(group);

            if (queryResponse.ResponseMessage.IsSuccessStatusCode)
                return JsonDocument.Parse(await queryResponse.ResponseMessage.Content.ReadAsStringAsync());
            else
                return JsonDocument.Parse("{}");
        }

        public static async Task<JsonDocument> GetAzDevOpsGroupsAsync(string orgaName, string projectDescriptor, string pat)
        {
            // GET https://vssps.dev.azure.com/{{organization}}/_apis/graph/groups?scopeDescriptor=scp.ODU4ZTU0OTItYThkMC00OWEyLWI1NzAtNDNmZTY3ODJkYmJl&api-version={{api-version-preview}}

            var queryResponse = await $"https://vssps.dev.azure.com/{orgaName}"
                .AppendPathSegment("_apis/graph/groups")
                .SetQueryParam("scopeDescriptor", projectDescriptor)
                .SetQueryParam("api-version", Constants.APIVERSION)
                .WithBasicAuth(string.Empty, pat)
                .GetAsync();

            if (queryResponse.ResponseMessage.IsSuccessStatusCode)
                return JsonDocument.Parse(await queryResponse.ResponseMessage.Content.ReadAsStringAsync());
            else
                return JsonDocument.Parse("{}");
        }

    }
}
