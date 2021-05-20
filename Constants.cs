using System;
using System.Collections.Generic;

namespace DevOpsManagement
{
    internal class Constants
    {
        public const string StorageQueueName = "azprojectsetup";
        public const string APIVERSION = "6.1-preview.1";
        public const string MAINBRANCHNAME = "main";
        public const string SecurityNamespaceGitRepo = "2e9eb7ed-3c0a-47d4-87c1-0ffdd275fd87";
        public const string SETUP_REPO = "SetupRepositoryFct";
        public const string PROJECT_PREFIX = "AZP-{0}_";
        public const string PROCESS_TEMPLATE_ID = "08707d34-379e-45bd-9824-8e7d6b111536";
    }

    public struct GroupScopeDescriptors
    {
        public const string ProjectScope = "scp.NTc0N2FkNjQtMTE3Ni00MzM4LWE1OGMtOTIyZGJiOGVlOTRk";

    }

    public struct GitPermissions
    {
        public const long ADMINISTER = 1;
        public const long GENERIC_READ = 2; //RP: Read
        public const long GENERIC_CONTRIBUTE = 4;   // RP, BP: Contribute
        public const long FORCEPUSH = 8;    // RP, BP: Force push (rewrite history, delete branches and tags)
        public const long CREATEBRANCH = 16; // RP: Create branch
        public const long CREATETAG = 32; // RP: Create tag
        public const long MANAGENOTE = 64; //RP: Manage notes
        public const long POLICYEXEMPT = 128;   // RP,BP: Bypass policies when pushing
        public const long CREATEREPOSITORY = 256;
        public const long DELETEREPOSITORY = 512; // RP: Delete repository
        public const long RENAMEREPOSITORY = 1024; //RP: Rename repository
        public const long EDITPOLICIES = 2048;  // RP, BP: Edit policies
        public const long REMOVEOTHERLOCKS = 4096; // RP, BP: Remove others' locks
        public const long MANAGEPERMISSIONS = 8192; // RP, BP: Manage permissions
        public const long PULLREQUESTCONTRIBUTE = 16384;
        public const long PULLREQUESTBYPASSPOLICY = 32768; // RP,BP: Bypass policies when completing pull requests
    }

    public struct PolicyTypes
    {
        public const string PATH_LENGTH_RESTRICTION = "001a79cf-fda1-4c4e-9e7c-bac40ee5ead8"; // This policy will reject pushes to a repository for paths which exceed the specified length
        public const string RESERVED_NAMES_RESTRICTION = "db2b9b4c-180d-4529-9701-01541d19f36b";    // This policy will reject pushes to a repository for names which aren't valid on all supported client OSes.
        public const string REQUIRE_MERGE_STRATEGY = "fa4e907d-c16b-4a4c-9dfa-4916e5d171ab";    // This policy ensures that pull requests use a consistent merge strategy.
        public const string COMMENT_REQUIREMENTS = "c6a1889d-b943-4856-b76f-9e46bb6b0df2";    // Check if the pull request has any active comments
        public const string STATUS = "cbdc66da-9728-4af8-aada-9a5a32e4a226"; // This policy will require a successfull status to be posted before updating protected refs.
        public const string GIT_REPO_SETTINGS = "7ed39669-655c-494e-b4a0-a08b4da0fcce";   // Git repository settings"
        public const string BUILD = "0609b952-1397-4640-95ec-e00a01b2c241"; // This policy will require a successful build has been performed before updating protected refs.
        public const string FILE_SIZE_RESTRICTION = "2e26e725-8201-4edd-8bf5-978563c34a80"; // This policy will reject pushes to a repository for files which exceed the specified size.
        public const string FILE_NAME_RESTRICTION = "51c78909-e838-41a2-9496-c647091e3c61"; // This policy will reject pushes to a repository which add file paths that match the specified patterns
        public const string COMMIT_AUTHOR_EMAIL = "77ed4bd3-b063-4689-934a-175e4d0a78d7"; // This policy will block pushes from including commits where the author email does not match the specified patterns
        public const string REQUIRED_REVIEWER = "fd2167ab-b0be-447a-8ec8-39368250530e"; // This policy will ensure that required reviewers are added for modified files matching specified patterns.
        public const string MINIMUM_NUMBER_OF_REVIEWERS = "fa4e907d-c16b-4a4c-9dfa-4906e5d171dd"; // This policy will ensure that a minimum number of reviewers have approved a pull request before completion.
        public const string WORK_ITEM_LINKING = "40e92b44-2fe1-4dd6-b3d8-74a9c21d0c6e";   // This policy encourages developers to link commits to work items
        public const string GIT_REPO_SETTINGS_POLICY_NAME = "0517f88d-4ec5-4343-9d26-9930ebd53069";   // GitRepositorySettingsPolicyName",
    }

    internal struct ZfGroupNames
    {
        /// <summary>
        /// member of Build Administrators, Endpoint Administrators, Deployment Group Administrators, Release Administrators
        /// </summary>
        public const string InfraMaint_Administrator = "AZG-{0}_InfraMaint_Administrator";
        /// <summary>
        /// member of Deployment Group Administrators, End Point Creators Groups. Note: Build Pipeline, Repos, Release Pipeline should be Read/Write 
        /// </summary>
        public const string InfraMaint_Developer = "AZG-{0}_InfraMaint_Developer";
        /// <summary>
        /// member of Contributors
        /// </summary>
        public const string ProjMaint_Developer = "AZG-{0}_ProjMaint_Developer";
        /// <summary>
        /// member of Project Administrators
        /// </summary>
        public const string ProjMaint_Adminstrator = "AZG-{0}_ProjMaint_Adminstrator";
        /// <summary>
        /// member of Build Administrators
        /// </summary>
        public const string ProjMaint_Deployer = "AZG-{0}_ProjMaint_Deployer";
        /// <summary>
        /// member of Readers
        /// </summary>
        public const string Proj_Consumer = "AZG-{0}_Proj_Consumer";
    }

    internal struct GroupNames
    {
        public string AzDoName { get; set; }
        public string SecurityDescriptor;        
    }

}
