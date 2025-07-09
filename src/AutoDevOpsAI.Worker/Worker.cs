using Microsoft.TeamFoundation.WorkItemTracking.WebApi;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.WebApi;
using AutoDevOpsAI.DevOps;
using AutoDevOpsAI.Core;
using HtmlAgilityPack;

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
        private readonly IAgentService _agentService;

        public Worker(
            ILogger<Worker> logger,
            IConfiguration config,
            IAgentService agentService,
            ILoggerFactory loggerFactory)
        {
            _logger = logger;
            _config = config;
            _agentService = agentService;

            _devOpsClient = new DevOpsClient(
                OrganizationUrl,
                ProjectName,
                PatToken,
                loggerFactory.CreateLogger<DevOpsClient>(),
                agentService);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Worker iniciado");

            var credentials = new VssBasicCredential(string.Empty, PatToken);
            var connection = new VssConnection(new Uri(OrganizationUrl), credentials);
            var witClient = connection.GetClient<WorkItemTrackingHttpClient>();

            while (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation($">>  Buscando histórias de usuário  <<");
                var historias = await _devOpsClient.GetUserStoriesWithTagAsync("autocode");

                foreach (var historia in historias)
                {
                    var titulo = historia.Fields["System.Title"].ToString();
                    var htmlDescricao = historia.Fields["System.Description"]?.ToString() ?? titulo;
                    var descricao = ExtrairTextoLimpo(htmlDescricao);

                    _logger.LogInformation($">> Processando história #{historia.Id}: {titulo} <<");

                    var repoName = ExtrairRepoHtml(htmlDescricao);

                    if (string.IsNullOrWhiteSpace(repoName))
                    {
                        _logger.LogWarning("História #{id} ignorada: repositório não informado no corpo da descrição.", historia.Id);
                        continue;
                    }

                    var pipelineId = await _devOpsClient.ObterPipelineIdPorNomeAsync(repoName);

                    if (pipelineId is null)
                    {
                        _logger.LogWarning($"Pipeline não encontrada para o repositório {repoName}.", repoName);
                        continue;
                    }

                    var branchName = $"autocode/card-{historia.Id}";

                    //verifica se a branch já existe
                    var branchExists = await _devOpsClient.VerificarBranchExistenteAsync(repoName, branchName);

                    // 1. Obter estrutura do repositório
                    var estruturaArquivos = await _devOpsClient.ListAllFilesAsync(repoName, branchExists ? branchName : "main");

                    // 2. Pedir sugestão de alterações à IA
                    var arquivosGerados = await _agentService.ProporAlteracoesAsync(descricao, estruturaArquivos);

                    if (!arquivosGerados.Any())
                    {
                        _logger.LogWarning("IA não retornou alterações para a história #{id}", historia.Id);
                        continue;
                    }

                    // 3. Criar nova branch
                    if (!branchExists)
                    {
                        _logger.LogInformation(">> Criando nova branch: {branchName} <<", branchName);
                        await _devOpsClient.CreateBranchAsync(repoName, "main", branchName);
                    }
                    else
                        _logger.LogInformation(">> Branch já existe: {branchName} <<", branchName);


                    // 5. Rodar build e validar antes do PR
                    var buildOk = await _devOpsClient.ValidarBuildAntesDaPRAsync(
                        repoName: repoName,
                        branchName: branchName,
                        historiaId: historia.Id ?? 0,
                        arquivos: arquivosGerados,
                        pipelineId: pipelineId.Value
                    );

                    if (!buildOk)
                    {
                        _logger.LogWarning(">> Build falhou para a história #{id}. PR não será criada. <<", historia.Id);
                        continue;
                    }

                    // 6. Criar Pull Request
                    var pr = await _devOpsClient.CreatePullRequestAsync(
                        repoName: repoName,
                        sourceBranch: branchName,
                        targetBranch: "main",
                        title: $"AutoCode: #{historia.Id} - {titulo}",
                        description: descricao
                    );

                    await _devOpsClient.AtualizarHistoriaComoProcessada(historia.Id ?? 0, pr.Url);

                    _logger.LogInformation(">> História #{id} concluída com sucesso. <<", historia.Id);
                }



                await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken);
            }


        }


        private static string? ExtrairRepoHtml(string html)
        {
            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            foreach (var div in doc.DocumentNode.SelectNodes("//div"))
            {
                var texto = HtmlEntity.DeEntitize(div.InnerText.Trim());

                if (texto.StartsWith("@repo:", StringComparison.OrdinalIgnoreCase))
                {
                    var valor = texto.Substring(6).Trim();
                    return string.IsNullOrWhiteSpace(valor) ? null : valor;
                }
            }

            return null;
        }

        private static string ExtrairTextoLimpo(string html)
        {
            var doc = new HtmlDocument();
            doc.LoadHtml(html);
            return HtmlEntity.DeEntitize(doc.DocumentNode.InnerText.Replace("\r", "").Replace("\n", ""));
        }

    }
}
