namespace DevOpsManagement.DevOpsAPI
{
    using Flurl;
    using Flurl.Http;
    using System.IO;
    using System.Linq;
    using System.Text.Json;
    using System.Threading.Tasks;

    public static class Graph
    {
        public static async Task<string> CreateAzDevOpsGroupAsync(Url _organization,
            string projectName,
            string groupName,
            string pat)
        {
            // POST https://vssps.dev.azure.com/{organization}/_apis/graph/groups?api-version=6.1-preview.1
            var group = new
            {
                name = projectName,
                capabilities = new
                {
                    versioncontrol = new
                    {
                        sourceControlType = "Git"
                    }
                }
            };

            var queryResponse = await $"{_organization}"
                .AppendPathSegment("_apis/graph/groups")
                .SetQueryParam("api-version", "6.0-preview.1")
                .WithBasicAuth(string.Empty, pat)
                .AllowAnyHttpStatus()
                .PostJsonAsync(group);

            if (queryResponse.ResponseMessage.IsSuccessStatusCode)
                return JsonDocument.Parse(await queryResponse.ResponseMessage.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetString();
            else
                return string.Empty;
        }

    }
}
