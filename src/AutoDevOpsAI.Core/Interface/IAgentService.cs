using System.Threading.Tasks;

namespace AutoDevOpsAI.Core
{
    public interface IAgentService
    {
        Task<List<FileChange>> ProporAlteracoesAsync(string historiaUsuario, List<string> estruturaArquivos);

         Task<List<FileChange>> CorrigirFalhaBuildAsync(int historiaId, List<FileChange> arquivosAnteriores, string errosBuild);
    }
}
