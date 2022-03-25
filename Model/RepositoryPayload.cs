namespace DevOpsManagement.Model;
using Newtonsoft.Json;

public class RepositoryPayload : ProjectPayload
{   
    [JsonRequired]
    [JsonProperty("azp_Id")]
    public int AZPID { get; set; }
    [JsonProperty(Required = Required.DisallowNull)]
    public string RepositoryName { get; set; }
}

