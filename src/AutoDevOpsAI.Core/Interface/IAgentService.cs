using System.Threading.Tasks;

namespace AutoDevOpsAI.Core
{
    public interface IAgentService
    {
        Task<List<FileChange>> ProporAlteracoesAsync(string historiaUsuario, List<string> estruturaArquivos);

        
    }
}
