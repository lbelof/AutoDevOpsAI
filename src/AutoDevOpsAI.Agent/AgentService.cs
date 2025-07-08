using AutoDevOpsAI.Core;
using Microsoft.Extensions.Logging;
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
        private const string model = "gpt-4.1"; // ou gpt-3.5-turbo
        private readonly ILogger<AgentService> _logger;
        public AgentService(HttpClient httpClient, IConfiguration config, ILogger<AgentService> logger)
        {
            _httpClient = httpClient;
            _apiKey = config["OpenAI:ApiKey"];
            _logger = logger;
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

                Crie toda a estrutura mínima necessária para iniciar um projeto moderno em .NET 8, seguindo boas práticas de arquitetura, incluindo:

                - Solução (.sln)
                - Projeto principal (.csproj) e projetos auxiliares (.csproj) conforme necessário
                - Estrutura de pastas organizada
                - Arquivos de configuração essenciais (appsettings.json, launchSettings.json, etc.)
                - Crie as pastas e arquivos a partir da raiz do repositório. Não crie uma pasta raiz para o projeto.
                - Arquivo Program.cs ou Startup.cs, conforme a versão do .net
                - Dockerfile funcional na raiz do repositório. O Dockerfile deve ser capaz de construir e rodar o projeto em um SO Ubuntu com respectivas libs para .NET 8
                - README.md explicando o projeto
                - Ao fim desse prompt existe uma história de usuário. Entenda o contexto da historia e crie a estrutura base já pensando em antender a essa história.
                - Garanta que o projeto tenha tudo que precisa para ser executado e testado em container, incluindo:
                - Estrutura funcional de API RESTful
                - Configuração de injeção de dependência
                - Configuração de logging
                - Configuração de banco de dados (pode ser SQLite para simplicidade inicial)
                - Configuração de testes unitários
                - Estrutura organizada de acordo com DDD (Domain-Driven Design) e onion architecture

                Todos os nomes devem seguir a convenção de nomenclatura C# (PascalCase/CamelCase), e a indentação deve usar 4 espaços por nível.

                Retorne os arquivos no seguinte formato JSON:

                    [
                    {{ ""filePath"": ""/caminho/do/arquivo.cs"", ""content"": ""código aqui..."" }},
                    ...
                    ]

                Retorne apenas um array JSON de objetos com filePath e content, sem envolver em objetos adicionais como {{ files: [...] }} ou {{ response: [...] }} ou nenhum outro objeto.
                Certifique-se de que o JSON esteja bem formatado e válido.
                Use [ e ] apenas no início e fim do JSON, sem outros objetos intermediários.

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
                Certifique-se de que o JSON esteja bem formatado e válido.
                Use [ e ] apenas no início e fim do JSON, sem outros objetos intermediários.

                História de usuário:
                ""{historiaUsuario}""
                ";
        }

       

        public async Task<List<FileChange>> CorrigirFalhaBuildAsync(int historiaId, List<FileChange> arquivosAnteriores, string errosBuild)
        {
            var arquivosTexto = string.Join("\n\n", arquivosAnteriores.Select(a => $"Arquivo: {a.FilePath}\n{a.Content}"));

            var prompt = $@"
            Você é um engenheiro de software experiente em .NET. O último build da seguinte automação falhou:

            Erro da build:
            {errosBuild}

            Aqui estão os arquivos modificados anteriormente:

            {arquivosTexto}

            Corrija apenas o que for necessário para que o build seja bem-sucedido novamente, mantendo o restante do código intacto.

            Retorne somente os arquivos que foram alterados, no formato JSON:

            [
            {{ ""filePath"": ""/caminho/do/arquivo.cs"", ""content"": ""código aqui..."" }},
            ...
            ]
            Retorne apenas um array JSON de objetos com filePath e content, sem envolver em objetos adicionais como {{ files: [...] }} ou {{ response: [...] }} ou nenhum outro objeto.
            Use [ e ] apenas no início e fim do JSON, sem outros objetos intermediários.
            ";

            var requestBody = new
            {
                model,
                messages = new[]
                {
                    new { role = "system", content = "Você é um desenvolvedor backend C# experiente que responde sempre em JSON." },
                    new { role = "user", content = prompt }
                },
                temperature = 0.2
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


            if (string.IsNullOrWhiteSpace(contentRaw))
            {
                _logger.LogError("Resposta da IA vazia.");
                return new List<FileChange>();
            }


            var startIndex = contentRaw.IndexOf('[');
            var endIndex = contentRaw.LastIndexOf(']') + 1;

            if (startIndex >= 0 && endIndex > startIndex)
            {
                var explicacaoIA = contentRaw.Substring(0, startIndex).Trim();
                errosBuild += $"\n\nFoi aplicada a solução conforme essa explicação: {explicacaoIA}, mas ainda assim não funcionou:\n";
                var jsonData = contentRaw.Substring(startIndex, endIndex - startIndex).Trim();

                _logger.LogWarning($"📄 Explicação da IA para falha de build:\n{explicacaoIA}");

                try
                {
                    var arquivosCorrigidos = JsonSerializer.Deserialize<List<FileChange>>(jsonData, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    return arquivosCorrigidos ?? new List<FileChange>();
                }
                catch (JsonException ex)
                {
                    _logger.LogError(ex, "Erro ao desserializar a resposta JSON da IA.");
                    return new List<FileChange>();
                }
            }
            else
            {
                _logger.LogError("❌ Não foi possível identificar o JSON na resposta da IA:\n{resposta}", contentRaw);
                return new List<FileChange>();
            }

        }

    }
}
