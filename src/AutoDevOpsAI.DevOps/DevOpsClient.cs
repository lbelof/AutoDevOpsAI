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

        // Futuro:
        // - CreateBranchAsync
        // - CommitChangesAsync
        // - CreatePullRequestAsync
        // - TriggerPipelineAsync
    }
}
