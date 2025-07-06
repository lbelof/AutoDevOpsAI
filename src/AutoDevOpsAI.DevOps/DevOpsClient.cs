using Microsoft.TeamFoundation.SourceControl.WebApi;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi.Models;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.WebApi;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.Services.WebApi.Patch;
using Microsoft.VisualStudio.Services.WebApi.Patch.Json;
using System.Threading.Tasks;

namespace AutoDevOpsAI.DevOps
{
    public class DevOpsClient
    {
        private readonly VssConnection _connection;
        private readonly WorkItemTrackingHttpClient _witClient;
        private readonly GitHttpClient _gitClient;
        private readonly string _projectName;

        public DevOpsClient(string organizationUrl, string projectName, string patToken)
        {
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
            var refs =  await _gitClient.GetRefsAsync(
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

    }
}
