with open("/mnt/data/README.md", "w", encoding="utf-8") as f:
    f.write("""# AutoDevOpsAI

## 🚀 Visão Geral

**AutoDevOpsAI** é uma solução inovadora de automação que integra inteligência artificial ao ciclo de desenvolvimento de software em projetos .NET, utilizando Azure DevOps como plataforma central.  
A aplicação permite que histórias de usuário criadas no Azure Boards sejam automaticamente implementadas, testadas, corrigidas e entregues por uma IA, integrando-se aos repositórios Git, pipelines de CI/CD, Docker e plataformas de publicação como Render.com.

---

## ✨ Funcionalidades

- **Integração Total com Azure DevOps:**  
  Consome histórias de usuário e gerencia repositórios, branches, pull requests e pipelines de CI/CD automaticamente.

- **Geração de Código por IA:**  
  Uma IA analisa cada história de usuário, propõe e implementa alterações diretamente no código-fonte, seguindo boas práticas de arquitetura .NET.

- **Criação e Correção de Builds:**  
  O sistema roda pipelines, monitora falhas de build, aciona a IA para corrigir problemas e realiza pushs corretivos de forma autônoma.

- **Entrega Automatizada:**  
  Ao final do fluxo, é criada uma Pull Request e a aplicação pode ser publicada automaticamente em ambientes de teste/homologação (ex: Render.com).

- **Observabilidade e Logs:**  
  Todo o ciclo é registrado via logs detalhados, facilitando o rastreio e debugging do processo automatizado.

---

## 🏗️ Arquitetura da Solução

- **Worker Service:**  
  Serviço background que faz polling nas histórias do Azure DevOps e coordena todo o fluxo.

- **Agent Service:**  
  Responsável por conversar com a IA (OpenAI API) para gerar/corrigir código e interpretar prompts.

- **DevOps Client:**  
  Abstração de todas as integrações com Azure Boards, Repos, Pipelines e Build APIs.

- **Estrutura de Projetos:**  
    src/
    AutoDevOpsAI.Worker/ # Serviço principal
    AutoDevOpsAI.Agent/ # Serviço de integração com IA
    AutoDevOpsAI.DevOps/ # Cliente Azure DevOps
    AutoDevOpsAI.Core/ # Contratos, modelos, utilitários
    AutoDevOpsAI.Api/ # (Opcional) API para orquestração manual


## ⚙️ Pré-requisitos

- Conta e Projeto no **Azure DevOps**
- **Personal Access Token (PAT)** do Azure DevOps com permissões em Boards, Repos e Pipelines
- Conta no **OpenAI** (API Key)
- Conta no **Docker Hub** (para publicação de imagens)
- Conta no **Render.com** (ou outra plataforma de publicação)
- .NET 8 SDK (para desenvolvimento local)


## 🚦 Como Funciona (Visão Geral do Fluxo)

1. **PO cria história de usuário** no Azure Boards (com tag `autocode` e identificando o repositório alvo).
2. **AutoDevOpsAI Worker** identifica novas histórias e aciona a IA via AgentService.
3. **IA propõe e gera alterações no código**, que são commitadas em branch própria.
4. **Pipeline de CI/CD** é executada automaticamente.
5. **Falha no build?** IA recebe o log de erro e propõe correções, até três tentativas.
6. **Build OK?** É aberta uma Pull Request automaticamente.
7. **Deploy Automatizado**: Imagem Docker é publicada no Docker Hub e, via Render.com, a aplicação é disponibilizada online.



## 🛠️ Como Rodar Localmente

1. **Clone o repositório**
 ```bash
 git clone https://dev.azure.com/SUA_ORGANIZACAO/SEU_PROJETO/_git/AutoDevOpsAI
 cd AutoDevOpsAI
 ```

2. **Configure os arquivos de ambiente**
No diretório src/AutoDevOpsAI.Worker, crie um arquivo appsettings.Development.json:
 ```bash
{
  "AzureDevOps": {
    "OrganizationUrl": "https://dev.azure.com/SEU_ORG/",
    "ProjectName": "SEU_PROJETO",
    "PatToken": "SUA_PAT_TOKEN"
  },
  "OpenAI": {
    "ApiKey": "SUA_OPENAI_API_KEY"
  }
}
 ```

 3. **Rode o Worker**
 ```bash
 cd src/AutoDevOpsAI.Worker
dotnet run
 ```

 ## 💡 Exemplo de História de Usuário
 Como usuário, quero receber um e-mail de confirmação após me cadastrar,
para garantir que meu e-mail foi digitado corretamente e ativar minha conta.

Cenário:
- Um novo usuário se registra com nome, e-mail e senha
- O sistema deve enviar um e-mail com link de confirmação
- O token do link deve expirar após 24h

Critérios de aceite:
- O e-mail deve conter um link seguro para confirmar a conta
- O usuário deve ser redirecionado para a página de login após clicar no link
- Nenhum e-mail deve ser enviado se o usuário já estiver confirmado

@repo: MinhaApiBackend



 ## 🐳  CI/CD com Docker e Publicação Automática

- Dockerfile já incluso na raiz do projeto, pronto para build.
- Pipeline Azure DevOps:
  - Faz build/test/push da imagem para o Docker Hub.
  - O Render.com consome a imagem automaticamente.

Exemplo de build local:
docker build -t lbelof/minhaapibackend:latest .
docker run -p 8080:80 lbelof/minhaapibackend:latest

 ## 🚧  Observações Importantes

- **Prompt Engineering:**
  Os prompts para a IA foram otimizados para separar cenários de projeto novo e funcionalidades incrementais, garantindo que a IA só altere o essencial e evite sobrescrever código antigo.

- **Segurança:**
  Nunca exponha seu PAT ou API Key em repositórios públicos. Use Azure Key Vault, secrets ou variáveis de ambiente protegidas.

- **Limites:**
  Uso intensivo da OpenAI pode gerar custos. Para ambientes de produção, avalie limites de requisição e custo.

## 🤝 Contribuição

Contribuições são bem-vindas!
Crie issues, faça pull requests e ajude a evoluir esse projeto.