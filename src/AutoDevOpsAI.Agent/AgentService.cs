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
            var estrutura = string.Join("\n", estruturaArquivos);

            var prompt = $@"
Você está atuando como um engenheiro de software .NET. Abaixo está a estrutura atual do repositório:

{estrutura}


Com base nessa estrutura e na história de usuário a seguir, siga as instruções abaixo:

- Caso o projeto já esteja estruturado com arquivos `.sln`, `.csproj`, `Program.cs`, `Startup.cs`, `Dockerfile`, testes e pastas organizadas, **não os modifique**.
- Preserve todos os arquivos existentes, e **evite reescrever ou sobrescrever funcionalidades que não estejam diretamente relacionadas à história**.
- Altere ou crie apenas os arquivos estritamente necessários para implementar a história de usuário abaixo.
- Caso alguma parte essencial esteja ausente para que o sistema funcione (ex: `Program.cs`, `Startup.cs`, `Dockerfile`, `test project`), **crie somente o que for indispensável**.
- Se o projeto ainda não existir, ou estiver completamente vazio, siga as boas práticas de estruturação de um projeto moderno em .NET Core (última versão LTS(faça uma pesquisa para saber qual é)), com separação de responsabilidades, testes e Docker.
- Sempre que possível, **reutilize os arquivos e pastas já presentes** no projeto.
- Garanta que o código final gere um build bem-sucedido.
- Todo o código deve seguir a indentação e estilo padrão da linguagem C#, conforme as convenções da Microsoft (4 espaços por nível de indentação, nomes em PascalCase/CamelCase, uso de chaves em blocos, etc).
- Implemente testes unitários apenas para os novos comportamentos adicionados.
- Retorne a resposta no seguinte formato JSON:


[
  {{ ""filePath"": ""/caminho/do/arquivo.cs"", ""content"": ""código aqui..."" }},
  ...
]

História de usuário:
""{historiaUsuario}""
";

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
    }
}
