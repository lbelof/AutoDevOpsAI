using AutoDevOpsAI.Core;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace AutoDevOpsAI.Agent
{
    public class AgentService : IAgentService
    {
        private readonly HttpClient _httpClient;
        private readonly string _apiKey;
        private const string endpoint = "https://api.openai.com/v1/chat/completions";
        private const string model = "gpt-4"; // ou gpt-3.5-turbo

        public AgentService(HttpClient httpClient, IConfiguration config)
        {
            _httpClient = httpClient;
            _apiKey = config["OpenAI:ApiKey"];
        }

        public async Task<List<FileChange>> ProporAlteracoesAsync(string historiaUsuario, List<string> estruturaArquivos)
        {
            var projetoExiste = estruturaArquivos.Any(x =>
                x.EndsWith(".sln") || x.EndsWith(".csproj") || x.EndsWith("Program.cs")
            );

            var prompt = projetoExiste
                ? PromptFuncionalidade(historiaUsuario, estruturaArquivos)
                : PromptCriarEstruturaBase(historiaUsuario);

            var requestBody = new
            {
                model,
                messages = new[]
                {
                    new { role = "system", content = "Você é um desenvolvedor backend C# experiente que responde sempre em JSON." },
                    new { role = "user", content = prompt }
                },
                temperature = 0.3
            };

            var json = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

            var response = await _httpClient.PostAsync(endpoint, content);
            response.EnsureSuccessStatusCode();

            var responseString = await response.Content.ReadAsStringAsync();

            using var doc = JsonDocument.Parse(responseString);

            var contentRaw = doc.RootElement
                 .GetProperty("choices")[0]
                 .GetProperty("message")
                 .GetProperty("content")
                 .GetString();

            // Remove marcações Markdown como ```json
            var cleanJson = contentRaw?
                .Replace("'''json", "")
                .Replace("'''", "")
                .Trim();

            // Validação opcional
            Console.WriteLine("Resposta da IA limpa:");
            Console.WriteLine(cleanJson);

            // Desserializa a lista de arquivos
            return JsonSerializer.Deserialize<List<FileChange>>(cleanJson ?? "[]", new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? new List<FileChange>();
        }

        private string PromptCriarEstruturaBase(string historiaUsuario)
        {
            return $@"
                O repositório está vazio ou não possui projeto configurado.

                Crie toda a estrutura mínima necessária para iniciar um projeto moderno em .NET (última versão LTS(connsultar a web para saber qual é a versão)), seguindo boas práticas de arquitetura, incluindo:

                - Solução (.sln)
                - Projeto principal (.csproj)
                - Arquivo Program.cs ou Startup.cs, conforme a versão
                - Estrutura organizada com Controllers, Services, Models, Repositories
                - Projeto separado para testes unitários
                - Dockerfile funcional na raiz
                - README.md explicando o projeto
                - Ao fim desse prompt existe uma história de usuário. Entenda o contexto da historia e crie a estrutura base já pensando em antender a essa história.

                Todos os nomes devem seguir a convenção de nomenclatura C# (PascalCase/CamelCase), e a indentação deve usar 4 espaços por nível.

                Retorne os arquivos no seguinte formato JSON:

                    [
                    {{ ""filePath"": ""/caminho/do/arquivo.cs"", ""content"": ""código aqui..."" }},
                    ...
                    ]

                Retorne apenas um array JSON de objetos com filePath e content, sem envolver em objetos adicionais como {{ files: [...] }} ou {{ response: [...] }} ou nenhum outro objeto.

                História de usuário:
                ""{historiaUsuario}""
                ";
        }

        private string PromptFuncionalidade(string historiaUsuario, List<string> estrutura)
        {
            var estruturaTexto = string.Join("\n", estrutura);

            return $@"
                Você é um engenheiro de software .NET.

                Abaixo está a estrutura atual do projeto:
                {estruturaTexto}

                Com base nessa estrutura e na história de usuário a seguir, implemente apenas o necessário para entregar a funcionalidade descrita.

                Regras:
                - **Não reescreva arquivos inteiros desnecessariamente**
                - Modifique arquivos existentes apenas se for essencial para a funcionalidade
                - Crie arquivos novos onde necessário, mantendo a organização atual
                - Não crie estrutura de projeto (sln, csproj, Program.cs, Dockerfile, etc) se ela já existir
                - Implemente testes unitários apenas para os novos comportamentos adicionados
                - Use indentação padrão C# (4 espaços), nomes em PascalCase/CamelCase, boas práticas de formatação

                Retorne os arquivos alterados/criados no seguinte formato JSON:
                    [
                    {{ ""filePath"": ""/caminho/do/arquivo.cs"", ""content"": ""código aqui..."" }},
                    ...
                    ]

                Retorne apenas um array JSON de objetos com filePath e content, sem envolver em objetos adicionais como {{ files: [...] }} ou {{ response: [...] }} ou nenhum outro objeto.

                História de usuário:
                ""{historiaUsuario}""
                ";
        }


    }
}
