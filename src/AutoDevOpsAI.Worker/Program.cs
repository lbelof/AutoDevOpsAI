using AutoDevOpsAI.Worker;
using AutoDevOpsAI.Core;
using AutoDevOpsAI.Agent;
using Microsoft.Extensions.DependencyInjection;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddHostedService<Worker>();

builder.Services.AddHttpClient<IAgentService, AgentService>();



var host = builder.Build();
host.Run();
