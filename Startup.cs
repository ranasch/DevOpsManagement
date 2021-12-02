using Microsoft.Azure.Functions.Extensions.DependencyInjection;
[assembly: FunctionsStartup(typeof(DevOpsManagement.Startup))]

namespace DevOpsManagement
{
    using Microsoft.Azure.Storage;
    using Microsoft.Azure.Storage.Queue;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Configuration;
    using System;
    using DevOpsManagement.DevOpsAPI;
    using System.Collections.Generic;
    using Microsoft.Extensions.Logging;

    public class Startup: FunctionsStartup
    {
        private ILoggerFactory _loggerFactory;
        public override void Configure(IFunctionsHostBuilder builder)
        {
            var config = new ConfigurationBuilder()
               .SetBasePath(Environment.CurrentDirectory)
               .AddJsonFile("local.settings.json", optional: true, reloadOnChange: true)
               .AddEnvironmentVariables()
               .AddUserSecrets<Startup>(true, true)
               .Build();

            builder.Services.AddLogging();

            ConfigureServices(builder);

            var appSettings = new Appsettings()
            {
                PAT = config["PAT"],
                VSTSApiVersion = config["VSTSApiVersion"],
                VSTSOrganization = config["VSTSOrganization"],
                ManagementProjectName = config["MANAGEMENT_PROJECT_NAME"],
                ManagementProjectTeam = config["MANAGEMENT_PROJECT_TEAM_NAME"]
            };
            // Initialisze ManagementProjectId
            var managementProjectTask = Project.GetProjectAsync(appSettings.VSTSOrganizationUrl, appSettings.ManagementProjectName, appSettings.PAT);
            var managementProjectId = managementProjectTask.GetAwaiter().GetResult();
            appSettings.ManagementProjectId= managementProjectId.RootElement.GetProperty("id").GetString();

            //var appSettings = config.GetSection("AppSettings").Get<Appsettings>();
            builder.Services.AddSingleton(appSettings);

            // Create queue if not exists
            var storage = config.GetValue<string>("AzureWebJobsStorage");
            var storageAccount = CloudStorageAccount.Parse(storage);
            var qc = storageAccount.CreateCloudQueueClient();
            var queue = qc.GetQueueReference(Constants.StorageQueueName);
            queue.CreateIfNotExistsAsync(null, null).Wait();

            // Seed AZID
            var azidTask = Project.GetMaxAzIdForEnvironment(appSettings.VSTSOrganization, appSettings.ManagementProjectName, appSettings.ManagementProjectTeam, appSettings.PAT);
            var azid = azidTask.GetAwaiter().GetResult();
            var azidinstance = Tools.AzIdCreator.Instance;
            azidinstance.EnvironmentSeed=azid;
        }

        private void ConfigureServices(IFunctionsHostBuilder builder)
        {
            _loggerFactory = new LoggerFactory();
            var logger = _loggerFactory.CreateLogger("DevOps Function");
            logger.LogInformation("*** Enter Startup ***");
        }
    }
}
