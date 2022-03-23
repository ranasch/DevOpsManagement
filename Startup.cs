using Microsoft.Azure.Functions.Extensions.DependencyInjection;
[assembly: FunctionsStartup(typeof(DevOpsManagement.Startup))]

namespace DevOpsManagement
{
    using DevOpsManagement.DevOpsAPI;
    using Flurl.Http.Configuration;
    using Flurl.Http;
    using Microsoft.ApplicationInsights.Extensibility.Implementation;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Logging;
    using Microsoft.WindowsAzure.Storage;
    using Newtonsoft.Json.Converters;
    using Newtonsoft.Json.Serialization;
    using Newtonsoft.Json;
    using Serilog;
    using System;

    public class Startup : FunctionsStartup
    {
        private ILoggerFactory _loggerFactory;
        public override void Configure(IFunctionsHostBuilder builder)
        {
            var config = new ConfigurationBuilder()
               .SetBasePath(Environment.CurrentDirectory)
               .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
               .AddJsonFile("local.settings.json", optional: true, reloadOnChange: true)
               .AddEnvironmentVariables()
               .AddUserSecrets<Startup>(true, true)
               .Build();

            ConfigureServices(builder);

            var appSettings = new Appsettings()
            {
                PAT = config["PAT"],
                VSTSApiVersion = config["VSTSApiVersion"],
                VSTSOrganization = config["VSTSOrganization"],
                ManagementProjectName = config["MANAGEMENT_PROJECT_NAME"],
                ManagementProjectTeam = config["MANAGEMENT_PROJECT_TEAM_NAME"],
                APPINSIGHTS_INSTRUMENTATIONKEY = config["APPINSIGHTS_INSTRUMENTATIONKEY"],
                ProcessTemplateId = config["ProcessTemplateId"]
            };

            TelemetryDebugWriter.IsTracingDisabled = true;

            // provide static logger instance as soon as possible
            Log.Logger = new LoggerConfiguration()
                .ReadFrom.Configuration(config)
                .Enrich.FromLogContext()
                .WriteTo.Console()
                .WriteTo.ApplicationInsights(appSettings.APPINSIGHTS_INSTRUMENTATIONKEY, TelemetryConverter.Traces)
                .CreateLogger();

            var services = builder.Services;
            services.AddMvcCore()
                .AddNewtonsoftJson(o =>
                {
                    o.SerializerSettings.Converters.Add(new StringEnumConverter());
                    o.SerializerSettings.ContractResolver = new DefaultContractResolver
                    {
                        NamingStrategy = new CamelCaseNamingStrategy
                        {
                            OverrideSpecifiedNames = false
                        }
                    };
                });

            FlurlHttp
                .Configure(settings =>
                {
                    var jsonSettings = new JsonSerializerSettings
                    {
                        ContractResolver = new DefaultContractResolver
                        {
                            NamingStrategy = new CamelCaseNamingStrategy
                            {
                                OverrideSpecifiedNames = false
                            }
                        },
                        NullValueHandling = NullValueHandling.Ignore
                    };
                    jsonSettings.Converters.Add(new StringEnumConverter());
                    settings.JsonSerializer = new NewtonsoftJsonSerializer(jsonSettings);
                });

            Log.Information("*** Starting DevOps Management ***");

            // Initialisze ManagementProjectId
            Log.Information($"Using these settings:\nOrganizationUrl: {appSettings.VSTSOrganizationUrl}\nManagementProject: {appSettings.ManagementProjectName}\nPAT (starting with {appSettings.PAT.Substring(0, 4)}***\nTeam: {appSettings.ManagementProjectTeam}");
            var managementProjectTask = Project.GetProjectAsync(appSettings.VSTSOrganizationUrl, appSettings.ManagementProjectName, appSettings.PAT);
            var managementProjectId = managementProjectTask.GetAwaiter().GetResult();
            appSettings.ManagementProjectId = managementProjectId.RootElement.GetProperty("id").GetString();
            Log.Information($"Management project id: {appSettings.ManagementProjectId}");

            //var appSettings = config.GetSection("AppSettings").Get<Appsettings>();
            builder.Services.AddSingleton(appSettings);

            // Create queue if not exists
            Log.Information("Get storage account for queues");
            var storage = config.GetValue<string>("AzureWebJobsStorage");
            var storageAccount = CloudStorageAccount.Parse(storage);
            var qc = storageAccount.CreateCloudQueueClient();
            Log.Information($"Using storage account {storageAccount.QueueEndpoint.Host}");
            var queue = qc.GetQueueReference(Constants.StorageQueueName);
            queue.CreateIfNotExistsAsync(null, null).Wait();
            Log.Information($"Queue {queue.Name} now available");

            // Seed AZID
            var azidTask = Project.GetMaxAzIdForEnvironment(appSettings.VSTSOrganization, appSettings.ManagementProjectName, appSettings.ManagementProjectTeam, appSettings.PAT);
            var azid = azidTask.GetAwaiter().GetResult();
            Log.Information($"Found current AZP_ID = {azid}");
            var azidinstance = Tools.AzIdCreator.Instance;
            azidinstance.EnvironmentSeed = azid;

            Log.Information("*** DevOps Management running ***");
        }

        private void ConfigureServices(IFunctionsHostBuilder builder)
        {
            _loggerFactory = new LoggerFactory();
            var logger = _loggerFactory.CreateLogger("DevOps Function");
            logger.LogInformation("*** Enter Startup ***");
        }
    }
}
