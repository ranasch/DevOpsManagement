namespace DevOpsManagement
{
    public class Appsettings
    {
        public string VSTSApiVersion { get; set; }
        public string VSTSOrganization { get; set; }
        public string PAT { get; set; }
        public string ManagementProjectName { get; set; }
        public string ManagementProjectTeam { get; set; }
        public string[] Environments { get; set; }
    }
}
