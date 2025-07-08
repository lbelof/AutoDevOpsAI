using Microsoft.TeamFoundation.SourceControl.WebApi;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi.Models;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.WebApi;
using Microsoft.Extensions.Logging;
using System.Text;
using Microsoft.VisualStudio.Services.WebApi.Patch;
using Microsoft.VisualStudio.Services.WebApi.Patch.Json;
using System.Threading.Tasks;
using AutoDevOpsAI.Core;
using Microsoft.TeamFoundation.Build.WebApi;

namespace AutoDevOpsAI.DevOps
{
    public class DevOpsClient
    {
        private readonly VssConnection _connection;
        private readonly WorkItemTrackingHttpClient _witClient;
        private readonly GitHttpClient _gitClient;
        private readonly string _projectName;
        private readonly ILogger<DevOpsClient> _logger;
        private readonly IAgentService _agentService;
        private const int MaxTentativasCorrecao = 3;



        public DevOpsClient(
            string organizationUrl,
            string projectName,
            string patToken,
            ILogger<DevOpsClient> logger,
            IAgentService agentService)
        {
            _logger = logger;
            _agentService = agentService;

            _projectName = projectName;
            var credentials = new VssBasicCredential(string.Empty, patToken);
            _connection = new VssConnection(new Uri(organizationUrl), credentials);

            _witClient = _connection.GetClient<WorkItemTrackingHttpClient>();
            _gitClient = _connection.GetClient<GitHttpClient>();
        }

        public async Task<IReadOnlyList<WorkItem>> GetUserStoriesWithTagAsync(string tag)
        {
            string wiqlQuery = $@"
                SELECT [System.Id], [System.Title]
                FROM WorkItems
                WHERE [System.TeamProject] = '{_projectName}'
                  AND [System.WorkItemType] = 'User Story'
                  AND [System.Tags] CONTAINS '{tag}'
                  AND [System.State] <> 'Closed'
                  AND [System.State] <> 'Active'
                ORDER BY [System.ChangedDate] DESC
            ";

            var wiql = new Wiql { Query = wiqlQuery };
            var result = await _witClient.QueryByWiqlAsync(wiql);

            if (!result.WorkItems.Any())
                return Array.Empty<WorkItem>();

            var ids = result.WorkItems.Select(wi => wi.Id).ToArray();
            var items = await _witClient.GetWorkItemsAsync(ids);

            return items;
        }

        public async Task<string> GetFileContentAsync(string repoName, string branchName, string filePath)
        {
            var repo = await _gitClient.GetRepositoryAsync(_projectName, repoName);

            using var stream = await _gitClient.GetItemContentAsync(
                repo.Id,
                path: filePath,
                versionDescriptor: new GitVersionDescriptor
                {
                    Version = branchName,
                    VersionType = GitVersionType.Branch
                });

            using var reader = new StreamReader(stream, Encoding.UTF8);
            return await reader.ReadToEndAsync();
        }

        public async Task CreateBranchAsync(string repoName, string sourceBranch, string newBranch)
        {
            var repo = await _gitClient.GetRepositoryAsync(_projectName, repoName);

            var branch = await _gitClient.GetRefsAsync(
                            repositoryId: repo.Id,
                            filter: $"heads/{sourceBranch}"
                        );
            var sourceObjectId = branch.First().ObjectId;

            var newRef = new GitRefUpdate
            {
                Name = $"refs/heads/{newBranch}",
                OldObjectId = "0000000000000000000000000000000000000000",
                NewObjectId = sourceObjectId
            };

            await _gitClient.UpdateRefsAsync(new GitRefUpdate[] { newRef }, repo.Id);
        }


        public async Task CommitFileAsync(string repoName, string branchName, string filePath, string newContent, string commitMessage)
        {
            var repo = await _gitClient.GetRepositoryAsync(_projectName, repoName);

            // Recupera o último commit da branch
            var refs = await _gitClient.GetRefsAsync(
                            repositoryId: repo.Id,
                            filter: $"heads/{branchName}"
                        );

            var branchRef = refs.FirstOrDefault();
            if (branchRef == null)
                throw new InvalidOperationException($"Branch '{branchName}' não encontrada.");

            var latestCommitId = branchRef.ObjectId;

            //  Verifica se o arquivo já existe
            bool fileExists = false;
            try
            {
                var item = await _gitClient.GetItemAsync(
                    repositoryId: repo.Id,
                    path: filePath,
                    versionDescriptor: new GitVersionDescriptor
                    {
                        Version = branchName,
                        VersionType = GitVersionType.Branch
                    }
                );

                fileExists = item != null;
            }
            catch
            {
                // ignora erro se o arquivo não existir
                fileExists = false;
            }

            var change = new GitChange
            {
                ChangeType = fileExists ? VersionControlChangeType.Edit : VersionControlChangeType.Add,
                Item = new GitItem { Path = filePath },
                NewContent = new ItemContent
                {
                    Content = newContent,
                    ContentType = ItemContentType.RawText
                }
            };

            var commit = new GitCommitRef
            {
                Comment = commitMessage,
                Changes = new List<GitChange> { change }
            };

            var refUpdate = new GitRefUpdate
            {
                Name = $"refs/heads/{branchName}",
                OldObjectId = latestCommitId
            };

            var push = new GitPush
            {
                RefUpdates = new List<GitRefUpdate> { refUpdate },
                Commits = new List<GitCommitRef> { commit }
            };

            await _gitClient.CreatePushAsync(push, repo.Id);
        }



        public async Task<GitPullRequest> CreatePullRequestAsync(string repoName, string sourceBranch, string targetBranch, string title, string description)
        {
            var repo = await _gitClient.GetRepositoryAsync(_projectName, repoName);

            var pr = new GitPullRequest
            {
                Title = title,
                Description = description,
                SourceRefName = $"refs/heads/{sourceBranch}",
                TargetRefName = $"refs/heads/{targetBranch}"
            };

            return await _gitClient.CreatePullRequestAsync(pr, repo.Id);
        }

        public async Task<List<string>> ListAllFilesAsync(string repoName, string branchName)
        {
            var repo = await _gitClient.GetRepositoryAsync(_projectName, repoName);


            var items = await _gitClient.GetItemsAsync(
                repositoryId: repo.Id,
                scopePath: "/",
                recursionLevel: VersionControlRecursionType.Full,
                includeContentMetadata: false,
                versionDescriptor: new GitVersionDescriptor
                {
                    Version = branchName,
                    VersionType = GitVersionType.Branch
                }
            );

            return items
                .Where(item => item.IsFolder == false)
                .Select(item => item.Path)
                .ToList();
        }


        public async Task AtualizarHistoriaComoProcessada(int idHistoria, string prUrl)
        {
            // 1. Comentário
            var comment = new CommentCreate
            {
                Text = $"✅ Esta história foi processada pela AutoDevOpsAI. [Ver Pull Request]({prUrl})"
            };

            await _witClient.AddCommentAsync(comment, _projectName, idHistoria);

            // 2. Atualiza estado para 'In Review' (ajuste conforme o fluxo)
            var patch = new JsonPatchDocument
            {
                new JsonPatchOperation
                {
                    Operation = Operation.Replace,
                    Path = "/fields/System.State",
                    Value = "Active"
                }
            };

            await _witClient.UpdateWorkItemAsync(patch, idHistoria);
        }

        public async Task<bool> ValidarBuildAntesDaPRAsync(
            string repoName,
            string branchName,
            int historiaId,
            List<FileChange> arquivos,
            int pipelineId,
            int tentativaAtual = 0)
        {
            _logger.LogInformation($"[Tentativa {tentativaAtual + 1}] Realizando push de arquivos para {branchName}...");


            var pushResult = await RealizarPushAsync(repoName, branchName, arquivos);
            if (!pushResult)
            {
                _logger.LogError($"Push falhou para a branch {branchName}.");
                return false;
            }


            var build = new Build
            {
                Definition = new DefinitionReference { Id = pipelineId },
                SourceBranch = $"{branchName}"
            };

            var buildClient = _connection.GetClient<BuildHttpClient>();
            var buildResult = await buildClient.QueueBuildAsync(build, _projectName);

            _logger.LogInformation($"Build {buildResult.Id} iniciada para a branch {branchName}...");

            Build current;
            do
            {
                await Task.Delay(TimeSpan.FromSeconds(10));
                current = await buildClient.GetBuildAsync(_projectName, buildResult.Id);
            }
            while (current.Status != BuildStatus.Completed);

            if (current.Result == BuildResult.Succeeded)
            {
                _logger.LogInformation("✅ Build sucedida. PR será criada.");
                return true;
            }

            _logger.LogWarning("❌ Build falhou. Recuperando informações...");

            var timeline = await buildClient.GetBuildTimelineAsync(_projectName, current.Id);
            if (timeline == null)
            {
                _logger.LogError($"Não foi possível obter a timeline da build #{buildResult.Id}. Abandonando.");
                return false;
            }

            var falhas = timeline.Records
                .Where(r => r.Result == TaskResult.Failed && r.Log != null)
                .ToList();


            var mensagensErro = new List<string>();

            foreach (var falha in falhas)
            {
                var logLines = await buildClient.GetBuildLogLinesAsync(_projectName, current.Id, falha.Log.Id);
                var linhasRelevantes = logLines
                    .Where(l => l.Contains("error", StringComparison.OrdinalIgnoreCase))
                    .TakeLast(200); // pega as últimas 10 linhas com "error"

                mensagensErro.Add($"[{falha.Name}]\n{string.Join("\n", linhasRelevantes)}");
            }

            var resumoErro = string.Join("\n\n", mensagensErro);


            // var falhas = timeline.Records
            //     .Where(r => r.Result == TaskResult.Failed)
            //     .Select(r => $"{r.Name} (LogId: {r.Log?.Id})")
            //     .ToList();

            // foreach (var erro in falhas)
            // {
            //     _logger.LogError($"Erro na build: {erro}");
            // }

            // var resumoErro = string.Join("\n", falhas);

            if (tentativaAtual >= MaxTentativasCorrecao)
            {
                _logger.LogError($"❌ Número máximo de tentativas de correção atingido para #{historiaId}. Encerrando.");

                // (Opcional) Notifica a história de usuário com a falha
                await AtualizarHistoriaComErro(historiaId, resumoErro);
                return false;
            }

            _logger.LogInformation("Solicitando correção à IA...");
            var arquivosCorrigidos = await _agentService.CorrigirFalhaBuildAsync(historiaId, arquivos, resumoErro);

            _logger.LogInformation("Nova tentativa de push com arquivos corrigidos...");
            return await ValidarBuildAntesDaPRAsync(
                repoName,
                branchName,
                historiaId,
                arquivosCorrigidos,
                pipelineId,
                tentativaAtual + 1
            );
        }

        public async Task<bool> RealizarPushAsync(string repoName, string branchName, List<FileChange> arquivos)
        {
            try
            {
                var repo = await _gitClient.GetRepositoryAsync(_projectName, repoName);

                // Obter commit atual da branch de destino
                var refs = await _gitClient.GetRefsAsync(repo.Id, filter: $"heads/{branchName}");
                var targetRef = refs.FirstOrDefault();

                if (targetRef == null)
                {
                    _logger.LogError($"❌ Branch '{branchName}' não encontrada. Push cancelado.");
                    return false;
                }

                if(arquivos == null || !arquivos.Any())
                {
                    _logger.LogWarning($"⚠️ Nenhum arquivo para enviar na branch '{branchName}'. Push cancelado.");
                    return true; 
                }

                var latestCommitId = targetRef.ObjectId;

                if (string.IsNullOrEmpty(latestCommitId))
                {
                    _logger.LogError($"❌ Commit ID inválido para a branch '{branchName}'.");
                    return false;
                }

                // Construir as alterações
                var changes = new List<GitChange>();

                foreach (var arquivo in arquivos)
                {
                    // Verifica se o arquivo já existe no repositório
                    bool existe = false;
                    try
                    {
                        var item = await _gitClient.GetItemAsync(
                            repositoryId: repo.Id,
                            path: arquivo.FilePath,
                            versionDescriptor: new GitVersionDescriptor
                            {
                                Version = branchName,
                                VersionType = GitVersionType.Branch
                            }
                        );
                        existe = item != null;
                        _logger.LogInformation($"Arquivo '{arquivo.FilePath}' já existe no repositório e será alterado");
                    }
                    catch
                    {
                        _logger.LogInformation($"Arquivo '{arquivo.FilePath}' não encontrado no repositório, será criado");
                    }

                    changes.Add(new GitChange
                    {
                        ChangeType = existe ? VersionControlChangeType.Edit : VersionControlChangeType.Add,
                        Item = new GitItem { Path = arquivo.FilePath },
                        NewContent = new ItemContent
                        {
                            Content = arquivo.Content,
                            ContentType = ItemContentType.RawText
                        }
                    });
                }

                var newCommit = new GitCommitRef
                {
                    Comment = $"feat: commit automático gerado pela IA ",
                    Changes = changes
                };

                var push = new GitPush
                {
                    RefUpdates = new List<GitRefUpdate>
            {
                new GitRefUpdate
                {
                    Name = $"refs/heads/{branchName}",
                    OldObjectId = latestCommitId
                }
            },
                    Commits = new List<GitCommitRef> { newCommit }
                };

                _logger.LogInformation($"🔄 Enviando push para '{branchName}' com {arquivos.Count} arquivos...");

                await _gitClient.CreatePushAsync(push, repo.Id);

                _logger.LogInformation($"✅ Push realizado com sucesso para '{branchName}'.");

                return true;
            }
            catch (VssServiceException ex) when (ex.Message.Contains("TF401028"))
            {
                _logger.LogWarning("⚠️ Conflito de concorrência no push para '{branchName}': {erro}", branchName, ex.Message);
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"❌ Erro inesperado ao realizar push para '{branchName}'");
                return false;
            }
        }

        public async Task<int?> ObterPipelineIdPorNomeAsync(string pipelineName)
        {
            var buildClient = _connection.GetClient<BuildHttpClient>();

            var definitions = await buildClient.GetDefinitionsAsync(_projectName, name: pipelineName);

            var definition = definitions.FirstOrDefault();

            return definition?.Id;
        }

        public async Task AtualizarHistoriaComErro(int historiaId, string mensagemErro)
        {
            try
            {
                var comentario = new CommentCreate()
                {
                    Text = $"⚠️ A automação tentou processar a história #{historiaId}, mas a build falhou mesmo após várias tentativas.\n\n**Resumo do erro:**\n{mensagemErro}"
                };

                await _witClient.AddCommentAsync(comentario, _projectName, historiaId);
                _logger.LogInformation($"Comentário de erro adicionado na história #{historiaId}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Erro ao adicionar comentário de erro na história #{historiaId}");
            }
        }

        public async Task<bool> VerificarBranchExistenteAsync(string repoName, string branchName)
        {
            try
            {
                var repo = await _gitClient.GetRepositoryAsync(_projectName, repoName);
                var branches = await _gitClient.GetRefsAsync(repo.Id, filter: $"heads/{branchName}");
                return branches.Any();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao verificar se a branch {branchName} existe no repositório {repoName}", branchName, repoName);
                return false;
            }
        }

    }
}
