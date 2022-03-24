namespace DevOpsManagement.Test;
using DevOpsManagement;
using DevOpsManagement.Model;
using DevOpsManagement.Test.Helper;
using Flurl.Http.Testing;
using Newtonsoft.Json;
using Serilog;
using Serilog.Sinks.TestCorrelator;
using System;
using System.Linq;
using System.Net;
using System.Text.Json.Nodes;
using Xunit;

public class FunctionTests
{
    readonly Appsettings _appSettings;
    readonly string _projectPayloadMessage;

    public FunctionTests()
    {
        _appSettings = new Appsettings();
        _appSettings.VSTSOrganization = "mockorga";
        _appSettings.PAT = "mockPAT";
        _appSettings.APPINSIGHTS_INSTRUMENTATIONKEY = Guid.NewGuid().ToString();
        _appSettings.ManagementProjectId = Guid.NewGuid().ToString();
        _appSettings.ManagementProjectName = "MockProjectCreationProject";
        _appSettings.ManagementProjectTeam = $"{_appSettings.ManagementProjectName}%20Team";
        _appSettings.ProcessTemplateId = Guid.NewGuid().ToString();
        _appSettings.VSTSApiVersion = "7.0";
        _projectPayloadMessage = EmbeddedResource.GetResource(typeof(FunctionTests), "Payloads.CreateProject.json");

        Log.Logger = new LoggerConfiguration()
                    .WriteTo.Console()
                    .WriteTo.Sink(new TestCorrelatorSink()).Enrich.FromLogContext().CreateLogger();
    }

    [Fact]
    public async void Create_Project_With_Invalid_Json_Payload_Logs_Error()
    {
        /// Arrange
        var payload = "no json";
        var sut = new DevOpsSetupFct(_appSettings);

        /// Act
        using (TestCorrelator.CreateContext())
        {
            /// Assert
            await sut.SetupRepository(payload);

            Assert.True(TestCorrelator.GetLogEventsFromCurrentContext().Count() > 0);
            Assert.StartsWith("Bad Request - cannot process request message", TestCorrelator.GetLogEventsFromCurrentContext().ToList().FirstOrDefault(_ => _.Level == Serilog.Events.LogEventLevel.Error).RenderMessage());
        }
    }

    [Fact]
    public async void Create_Project_With_Duplicate_Name_Logs_Error()
    {
        /// Arrange
        var apiMock = new HttpTest();
        var azdoProjects = EmbeddedResource.GetResource(typeof(FunctionTests), "AzDoResponses.Projects.json");
        var projectPayload = JsonConvert.DeserializeObject<ProjectPayload>(_projectPayloadMessage);
        apiMock
            .ForCallsTo("https://dev.azure.com/mockorga/_apis/projects")
            .WithVerb("GET")
            .RespondWith(azdoProjects, (int)HttpStatusCode.Accepted);

        apiMock
            .ForCallsTo("https://dev.azure.com/mockorga/*")
            .WithVerb("POST")
            .RespondWith("{}", (int)HttpStatusCode.OK);

        apiMock
            .ForCallsTo("https://dev.azure.com/mockorga/_apis/wit/workitems/*")
            .WithVerb("PATCH")
            .RespondWith("{}", (int)HttpStatusCode.OK);

        apiMock
            .ForCallsTo("https://dev.azure.com/mockorga/MockProjectCreationProject/_apis/wit/workitems/*")
            .WithVerb("POST")
            .RespondWith("{}", (int)HttpStatusCode.OK);

        var sut = new DevOpsSetupFct(_appSettings);

        using (TestCorrelator.CreateContext())
        {
            /// Assert
            await sut.SetupRepository(_projectPayloadMessage);

            Assert.True(TestCorrelator.GetLogEventsFromCurrentContext().Count() > 0);
            Assert.StartsWith("*** Duplicate project name - A project with n", TestCorrelator.GetLogEventsFromCurrentContext().ToList().FirstOrDefault(_ => _.Level == Serilog.Events.LogEventLevel.Error).RenderMessage());
        }
    }

    [Fact]
    public async void Create_Project_With_Invalid_Name_Logs_Error()
    {
        /// Arrange
        var apiMock = new HttpTest();
        var azdoProjects = EmbeddedResource.GetResource(typeof(FunctionTests), "AzDoResponses.Projects.json");
        var projectPayload = JsonConvert.DeserializeObject<ProjectPayload>(_projectPayloadMessage);
        apiMock
            .ForCallsTo("https://dev.azure.com/mockorga/_apis/projects")
            .WithVerb("GET")
            .RespondWith(azdoProjects, (int)HttpStatusCode.Accepted);

        apiMock
            .ForCallsTo("https://dev.azure.com/mockorga/*")
            .WithVerb("POST")
            .RespondWith("{}", (int)HttpStatusCode.OK);

        apiMock
            .ForCallsTo("https://dev.azure.com/mockorga/_apis/wit/workitems/*")
            .WithVerb("PATCH")
            .RespondWith("{}", (int)HttpStatusCode.OK);

        apiMock
            .ForCallsTo("https://dev.azure.com/mockorga/MockProjectCreationProject/_apis/wit/workitems/*")
            .WithVerb("POST")
            .RespondWith("{}", (int)HttpStatusCode.OK);


        projectPayload.ProjectName = "Project with spaces in name";
        var provMessageSpaces = JsonConvert.SerializeObject(projectPayload);

        var sut = new DevOpsSetupFct(_appSettings);

        using (TestCorrelator.CreateContext())
        {
            /// Act
            await sut.SetupRepository(provMessageSpaces);

            /// Assert
            Assert.True(TestCorrelator.GetLogEventsFromCurrentContext().Count() > 0);
            Assert.StartsWith("*** Invalid characters in project name - ch", TestCorrelator.GetLogEventsFromCurrentContext().ToList().FirstOrDefault(_ => _.Level == Serilog.Events.LogEventLevel.Error).RenderMessage());
        }

        projectPayload.ProjectName = "Projectwith+or#inname";
        var provMessageSpecialChar = JsonConvert.SerializeObject(projectPayload);

        using (TestCorrelator.CreateContext())
        {
            /// Act
            await sut.SetupRepository(provMessageSpaces);

            /// Assert
            Assert.True(TestCorrelator.GetLogEventsFromCurrentContext().Count() > 0);
            Assert.StartsWith("*** Invalid characters in project name - ch", TestCorrelator.GetLogEventsFromCurrentContext().ToList().FirstOrDefault(_ => _.Level == Serilog.Events.LogEventLevel.Error).RenderMessage());
        }

    }

}
