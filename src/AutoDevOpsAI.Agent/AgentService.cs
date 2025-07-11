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
        private const string model = "gpt-4.1";
        private readonly ILogger<AgentService> _logger;
        public AgentService(HttpClient httpClient, IConfiguration config, ILogger<AgentService> logger)
        {
            _httpClient = httpClient;
            _apiKey = config["OpenAI:ApiKey"];
            _logger = logger;
        }

        public async Task<List<FileChange>> ProporAlteracoesAsync(string historiaUsuario, List<FileChange> estruturaArquivos, bool projetoExiste = false)
        {
            string prompt;

            if (projetoExiste)
            {
                _logger.LogInformation("Propondo alterações no código ...");
                prompt = PromptFuncionalidade(historiaUsuario, estruturaArquivos);
            }
            else
            {
                _logger.LogInformation("Nenhum projeto .net válido não encontrado, criando estrutura base...");
                prompt = PromptCriarEstruturaBase(historiaUsuario);
            }

            var requestBody = new
            {
                model,
                messages = new[]
                {
                    new { role = "system", content = "Você é um desenvolvedor backend C# especialista que responde sempre em JSON." },
                    new { role = "user", content = prompt }
                },
                temperature = 0
            };

            var json = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
            _logger.LogInformation(">> PROGRAMANDO... <<");
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
                O repositório está vazio ou não possui nenhum projeto .net válido configurado.

                Siga as instruções abaixo para criar toda a estrutura mínima de um projeto .NET 8 moderno para uma API REST, pronta para rodar em container Docker, usando boas práticas de arquitetura onion e DDD:
                
                ETAPAS:                
                1. Siga o modelo de arquitetura onion, criando as pastas: Domain, Application, Infrastructure,  API e Testes. 
                2. Crie os arquivos de configuração essenciais, como: appsettings.json, launchSettings.json.
                3. Crie Program.cs (padrão .NET 8 minimal API) e configure o Swagger para estar em todos os ambientes em /swagger.
                4. Crie um Dockerfile funcional na raiz, pronto para build e deploy em Ubuntu, e com entrada configurada para rodar o projeto principal.                
                5. Garanta que o build (`dotnet build`) e os testes (`dotnet test`) rodem sem erros.
                6. Garanta que todos os 'using'(referencias) necessários necessários em cada classe  estejam presentes nas respectivas classes.
                7. Evite comentários ou blocos de código inúteis. Inclua apenas o código mínimo necessário, sem métodos vazios, nem exemplos genéricos.
                8. Só utilize pacotes NuGet que sejam compatíveis com .NET 8 
                9. Crie a solution (.sln) na raiz do repositório devidamente preenchida, referenciando todos os projetos criados . 
                10. O projeto de teste deve estar funcional e referenciar corretamente os demais projetos, com testes básicos de integração para a API
                11. Utilize a lib Microsoft.NET.Test.Sdk no projeot de testes, e garanta que os testes sejam executados corretamente.

                Todos os nomes devem seguir a convenção de nomenclatura C# (PascalCase/CamelCase) e a indentação deve usar 4 espaços por nível.
                Importante: Abaixo há uma história de usuário que deve ser implementada após a criação da estrutura base. Utilize a história de usuário para definir a linguagem ubíqua e o domínio dessa API.

                **História de usuário:**
                {historiaUsuario}

                **Formato de saída:**
                Retorne apenas um array JSON de objetos, cada um no formato:
                [
                {{ ""filePath"": ""/caminho/do/arquivo.cs"", ""content"": ""código aqui..."" }},
                ...
                ]
                Não inclua nenhum texto, markdown, ou objeto adicional. Apenas o JSON puro.
                ";

        }

        private string PromptFuncionalidade(string historiaUsuario, List<FileChange> estrutura)
        {
            var estruturaTexto = string.Join("\n", estrutura.Select(a => $"Arquivo: {a.FilePath}\n Conteúdo do arquivo:{a.Content}\nFIM do arquivo {a.FilePath}\n\n "));

            return $@"
                Você é um engenheiro de software .NET especialista e recebeu uma demanda para alterar um sistema já em uso. Abaixo estão alguns dados importantes
                dos arquivos que já estão no repositorio e instruções de como você deve proceder.  

                Abaixo está a estrutura atual do projeto:
                {estruturaTexto}

                Com base nessa estrutura e na história de usuário a seguir, implemente apenas o necessário para entregar a funcionalidade descrita.

                Regras:
                
                - **Não reescreva arquivos inteiros desnecessariamente**
                - Modifique arquivos existentes apenas se for essencial para a funcionalidade
                - AS controllers são críticas por serem usadas pelos consumidores, então evite modificá-las a menos que seja necessário de acordo com a história de usuário
                - Crie arquivos novos onde necessário, mantendo a organização atual
                - Implemente testes unitários apenas para os novos comportamentos adicionados ou ajustes os testes existentes se necessário
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
        public async Task<List<FileChange>> CorrigirFalhaBuildAsync(int historiaId, List<FileChange> arquivosAnteriores, string errosBuild, List<FileChange> arquivosAtuaisNaBranch)
        {
            var arquivosTexto = string.Join("\n\n", arquivosAnteriores.Select(a => $"Arquivo: {a.FilePath}\n Conteúdo do arquivo:{a.Content} \nFIM do arquivo {a.FilePath}\n\n "));
            var arquivosAtuaisTexto = string.Join("\n\n", arquivosAtuaisNaBranch.Select(a => $"Arquivo: {a.FilePath}\n Conteúdo do arquivo:{a.Content}\nFIM do arquivo {a.FilePath}\n\n "));

            var prompt = $@"
                Você é um engenheiro de software .NET responsável por manutenção e correção de aplicação .net 8. O último build de uma aplicação .net 8 falhou. Abaixo estão alguns dados importantes do buil que falho na execução da pipeline,
                dos arquivos que compoem o commit que está rodando no build e instruções de como você deve proceder. 

                ERRO DA BUILD DA APLICAÇÃO(LOG):
                {errosBuild}

                ARQUIVOS ATUAIS NA BRANCH:
                {arquivosAtuaisTexto}

                INSTRUÇÕES:
                - Respeite a arquitetura de pastas do projeto existente. As pastas mostradas no LOG podem não condizer com a estrutura real do projeto, pois fazem parte do build do Azure DevOps. 
                - Corrija apenas o(s) erro(s) presente(s) no log acima.
                
                - Ao alterar um arquivo, faça um MERGE das suas alterações com o conteúdo atual do arquivo(que se encontra nesse prompt), assim como funciona no GIT.
                - A primeira coisa a verificar é a necessidade de adicionar  'using' nas classes citadas nos erros do log. 
                - Apenas corrija os erros de forma eficiente, sem adicionar código desnecessário ou alterar a lógica existente.
                - Evite criar arquivos novos, nem apague arquivos existentes, exceto se o erro explicitamente pedir por isso.
                - Se a correção anterior não funcionou, avalie desfaze-la e tentar outra abordagem.
                - O projeto de testes pode apresentar erros como 'Test Run Aborted'. Nesse caso, avalie os logs do build e identifique o que é preciso corrigir no projeto de testes.
                - Crie testes unitários em caso de novas funcionalidades ou correções que não tenham testes existentes.


                - Para cada erro, explique em até 3 linhas o que estava errado e a alteração realizada, ANTES do JSON.
                - Exemplo de explicação: “Corrigi o namespace da classe UserService, pois estava incorreto.” 
                - Se possível, cite o(s) arquivo(s) corrigido(s) na explicação.

                Retorne SOMENTE a explicação seguida de um array JSON, neste formato (NÃO inclua nenhum outro texto):

                Explicação da(s) alteração(ões):

                [
                {{ ""filePath"": ""/caminho/do/arquivo.cs"", ""content"": ""código aqui..."" }},
                ...
                ]

                NÃO envolva o array em nenhum outro objeto, NEM adicione comentários, markdown, etc.
                ";

            var requestBody = new
            {
                model,
                messages = new[]
                {
                    new { role = "system", content = "Você é um desenvolvedor backend C# experiente que responde sempre em JSON." },
                    new { role = "user", content = prompt }
                },
                temperature = 0
            };

            var json = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
            _logger.LogInformation(">> PROGRAMANDO... <<");
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

                _logger.LogInformation(">> Explicação da IA para falha de build: {explicacaoIA} <<", explicacaoIA);

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
                _logger.LogError("Não foi possível identificar o JSON na resposta da IA:\n{resposta}", contentRaw);
                return new List<FileChange>();
            }

        }



    }
}
