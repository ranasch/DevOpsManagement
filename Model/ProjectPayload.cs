namespace DevOpsManagement.Model;
using Newtonsoft.Json;

public class ProjectPayload 
{
    [JsonRequired]
    public string CreateType { get; set; }
    [JsonRequired]
    public int WorkItemId { get; set; }
    [JsonRequired]
    public string ProjectName { get; set; }
    public string ProjectDescription { get; set; }
    [JsonRequired]
    public string DataOwner1 { get; set; }
    public string DataOwner2 { get; set; }
    [JsonRequired]
    public string Requestor { get; set; }
    [JsonRequired]
    public string CostCenter { get; set; }
    [JsonRequired]
    public string CostCenterManager { get; set; }
}

