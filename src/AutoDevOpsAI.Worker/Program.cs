using AutoDevOpsAI.Worker;
using AutoDevOpsAI.Core;
using AutoDevOpsAI.Agent;
using AutoDevOpsAI.DevOps;

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(options =>
{
    options.IncludeScopes = false;
    options.SingleLine = true;
    options.TimestampFormat = "yyyy-MM-dd HH:mm:ss ";
});


builder.Services.AddHostedService<Worker>();
builder.Services.AddHttpClient<IAgentService, AgentService>();
builder.Services.AddSingleton<IAgentService, AgentService>();

builder.Services.AddSingleton(provider =>
{
    var config = provider.GetRequiredService<IConfiguration>();
    var logger = provider.GetRequiredService<ILogger<DevOpsClient>>();
    var agentService = provider.GetRequiredService<IAgentService>();

    return new DevOpsClient(
        config["AzureDevOps:OrganizationUrl"],
        config["AzureDevOps:ProjectName"],
        config["AzureDevOps:PatToken"],
        logger,
        agentService);
});

var host = builder.Build();
host.Run();
