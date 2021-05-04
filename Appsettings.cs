namespace DevOpsManagement
{
    using System;

    public class Appsettings
    {
        private static Uri _orgaUrl;
        public string VSTSApiVersion { get; set; }
        public string VSTSOrganization { get; set; }
        public string PAT { get; set; }
        public string ManagementProjectName { get; set; }
        public string ManagementProjectTeam { get; set; }
        public string ManagementProjectId { get; set; }
        public Uri VSTSOrganizationUrl { 
            get {
                if (String.IsNullOrEmpty(VSTSOrganization))
                {
                    throw new ApplicationException("VSTSOrganization not initialized - missing AppSettings?");
                }
                if (_orgaUrl==null )
                {
                    _orgaUrl = new Uri($"https://dev.azure.com/{VSTSOrganization}");
                }
                return _orgaUrl;
            } 
        }
    }
}
