using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi.Models;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.WebApi;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AutoDevOpsAI.DevOps;

namespace AutoDevOpsAI.Worker
{
    public class Worker : BackgroundService
    {
        private readonly ILogger<Worker> _logger;
        private readonly IConfiguration _config;
        private readonly DevOpsClient _devOpsClient;
        private string OrganizationUrl => _config["AzureDevOps:OrganizationUrl"];
        private string ProjectName => _config["AzureDevOps:ProjectName"];
        private string PatToken => _config["AzureDevOps:PatToken"]; 

        public Worker(ILogger<Worker> logger, IConfiguration config)
        {
            _logger = logger;
            _config = config;

    

            _devOpsClient = new DevOpsClient(OrganizationUrl, ProjectName, PatToken);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
           

            var credentials = new VssBasicCredential(string.Empty, PatToken);
            var connection = new VssConnection(new Uri(OrganizationUrl), credentials);
            var witClient = connection.GetClient<WorkItemTrackingHttpClient>();

            while (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("Verificando cards com tag 'autocode'...");

                var historias = await _devOpsClient.GetUserStoriesWithTagAsync("autocode");

                if (historias.Any())
                {
                    foreach (var item in historias)
                    {
                        var id = item.Id;
                        var titulo = item.Fields["System.Title"];
                        _logger.LogInformation($"# {id} - {titulo}");
                    }
                }
                else
                {
                    _logger.LogInformation("Nenhuma história encontrada com a tag 'autocode'.");
                }

                await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken);
            }
        }
    }
}
