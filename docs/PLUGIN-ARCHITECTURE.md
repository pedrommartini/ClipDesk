# Arquitetura de plugins v2

O sistema v2 separa comportamento, apresentação e serviços de plataforma. O objetivo é usar o mesmo módulo, os mesmos comandos e o mesmo estado no WPF atual, no futuro host MAUI para Windows e no Android.

```text
                        ClipDesk.PluginSdk (net8.0)
              manifesto · estado · comandos · permissões
                                  │
                    módulo portável do plugin (net8.0)
                   regras · validação · migração de estado
                         │                    │
           renderizador WPF              renderizador MAUI
       ClipDesk.PluginSdk.Windows       contrato futuro de UI
                         │                    │
                  host WPF atual       MAUI Windows / Android
                         └──────── serviços do host ────────┘
                           rede · clipboard · arquivos · URI
```

## Componentes e responsabilidades

- `ClipDesk.PluginSdk` não referencia WPF nem MAUI. Ele contém `PluginManifest`, `PluginState`, `PluginCommand`, `IClipDeskPluginModule`, opções, resultados e os contratos de serviços.
- Cada pasta `PluginPackages/Modules/<Plugin>/Core` é um projeto `net8.0`. Ali ficam regras, validação, migração e integrações expressas através dos serviços do host.
- O projeto Windows ao lado é apenas um renderizador. Ele traduz eventos WPF em comandos do módulo por `WindowsPluginViewContext.ExecuteAsync`.
- O host controla moldura, persistência, desfazer, sincronização, permissões e recursos do sistema. O plugin não recebe um objeto interno do aplicativo.
- O manifesto v2 declara uma entrada portável e zero ou mais renderizadores. Um pacote só é compatível com uma plataforma quando contém um renderizador para ela.
- Aparência por instância, como a cor de destaque, fica em `BoardObject.Style`; o renderizador recebe a cor resolvida do host e a aplica a ações primárias, estados ativos, indicadores e resultados relevantes. O host aplica o mesmo valor ao cabeçalho e invalida a view quando ele muda. A paleta é uma ferramenta do modo de seleção/movimentação do host, não parte do plugin nem do menu de contexto. O estado do plugin continua reservado para dados funcionais.
- Outline e alças de seleção pertencem à presença colaborativa, não à aparência do plugin. A cor é derivada da identidade do usuário; movimentos ativos trafegam no payload efêmero `PresenceMessage.Drags`, são interpolados nos demais clientes e desaparecem ao encerrar o arraste ou expirar a presença. Hosts WPF e MAUI devem preservar essa separação.

## Manifesto v2

O runtime raiz é `portable-v2`, a API é `2` e cada apresentação declara seu runtime (`wpf-v2`; futuramente `maui-v1`). `stateVersion` é a versão do formato persistido, diferente da versão do pacote.

```json
{
  "manifestVersion": 2,
  "id": "clipdesk.example",
  "name": "Exemplo",
  "version": "2.0.0",
  "runtime": "portable-v2",
  "pluginApiVersion": 2,
  "stateVersion": 1,
  "module": {
    "assembly": "ClipDesk.Plugin.Example.Core.dll",
    "type": "ClipDesk.Plugin.Example.ExampleModule"
  },
  "renderers": [
    {
      "platform": "windows",
      "runtime": "wpf-v2",
      "assembly": "ClipDesk.Plugin.Example.dll",
      "type": "ClipDesk.Plugin.Example.ExamplePlugin"
    }
  ],
  "platforms": ["windows"],
  "permissions": [],
  "capabilities": ["board-widget"],
  "networkHosts": []
}
```

Os nomes de montagem devem ser nomes simples, sem diretórios. `platforms` anuncia apenas plataformas realmente presentes no pacote. Adicionar `android` antes de existir um renderizador MAUI fará o pacote ser rejeitado nesse host.

## Estado e migração

`PluginState` é uma coleção imutável de strings. A chave reservada `$schema` registra a versão. O módulo deve:

1. criar um estado inicial completo em `CreateDefaultState`;
2. aceitar dados ausentes, antigos ou parcialmente sincronizados em `NormalizeState`;
3. produzir um novo estado, sem mutar o anterior;
4. preservar dados desconhecidos sempre que possível, para tolerar versões diferentes durante sincronização;
5. manter migrações determinísticas e idempotentes.

O host persiste somente estado e identidade. DLLs não são sincronizadas. A checklist v2 demonstra a migração de índices frágeis para itens com IDs estáveis e mantém espelhos legados durante a janela de compatibilidade.

## Execução e efeitos externos

Renderizadores enviam comandos nomeados e argumentos simples. O módulo valida o comando e retorna `PluginCommandResult`, incluindo estado, status, mensagem e dados transitórios. Cancelamento é obrigatório em operações assíncronas.

Efeitos externos passam pelo host:

- Rede: `IPluginNetworkClient`; exige permissão `network` e host em `networkHosts`. O WPF aceita apenas HTTPS, bloqueia redirecionamentos e limita a resposta a 1 MiB.
- Clipboard, abertura de URI e mensagens: ações tipadas de `PluginHostAction` no renderizador.
- Arquivos na mesa: `PluginHostAction.AddFiles` recebe `PluginBoardFileRequest`. `Content` exige `file.write` e é materializado no armazenamento gerenciado; `Source` exige `file.read` e, no Windows, deve ser um caminho absoluto existente. O manifesto também precisa anunciar `board-files`.
- Segundo plano: módulos não criam serviço residente. Atualizações visuais são canceladas quando a view é descarregada. No Android, trabalho prolongado terá de passar por WorkManager/foreground service e pelas políticas do sistema.

O host limita cada ação a 16 arquivos, 25 MiB por conteúdo gerado e 64 MiB no total. A solicitação é validada antes de alterar a mesa. Depois, cada arquivo vira um cartão normal, posicionado prioritariamente à direita da instância solicitante, com alternativas à esquerda, abaixo ou acima quando houver borda ou colisão. A operação participa do desfazer, da persistência e da sincronização existentes. Plugins não recebem acesso ao `WorkspaceBoard`, ao canvas ou a tipos WPF.

Permissões reduzem a superfície acidental, mas plugins Windows ainda executam no processo e não são sandbox de código hostil. Pacotes de terceiros exigirão assinatura, revisão e, idealmente, isolamento fora do processo.

## Compatibilidade v1

O host continua lendo `manifestVersion: 1`, `legacy-wpf` e `wpf-v1`. `IWindowsPlugin` e `PluginViewContext` permanecem no SDK Windows. O carregador escolhe v2 primeiro e tenta v1 em seguida. Isso permite atualizar o ClipDesk sem invalidar pacotes existentes.

Novos plugins devem usar v2. O suporte v1 é uma ponte de migração e não receberá novas capacidades. Os quatro plugins padrão são v2; as implementações antigas dentro de `BoardObjectView` ficam temporariamente como fallback para mesas antigas ou pacotes ausentes.

## Carregamento, entrega e atualização

Cada versão ocupa um diretório imutável. O catálogo valida manifesto, compatibilidade, nomes de arquivos, entradas portável/Windows e hosts. O carregador usa um contexto de montagem por pacote e compartilha apenas os SDKs do host.

O ZIP remoto mantém os limites atuais: 25 MiB compactado, 80 MiB extraído e 256 entradas. O SHA-256 é validado antes da instalação e caminhos que escapam do pacote são rejeitados. O feed v1 aceita metadados de manifesto v1 ou v2; para v2 ele inclui `module`, `renderers`, `stateVersion`, permissões, capacidades e hosts.

Existem canais independentes. Produção consulta o feed de `main`; builds DEV e ClipDesk New consultam o feed de `codex/clipdesk-dev`. Dessa forma, testar ou atualizar plugins durante a migração não os publica para o ClipDesk principal. `CLIPDESK_PLUGIN_FEED_URL` permite um feed local apenas para desenvolvimento.

Plugins incluídos ficam em `Plugins/Bundled`. O build compila e copia os dois binários de cada plugin padrão para sua pasta. Plugins opcionais ficam em Documentos; atualizações dos incluídos ficam no cache privado, preservando a versão embarcada como fallback.

A loja usa uma ação dividida: **Adicionar** executa a ação principal e a seta abre **Reparar** e **Desinstalar**. Reparar copia novamente um pacote validado para um diretório imutável novo e só troca o ponteiro ativo depois de a cópia estar completa; estados e objetos já presentes nas mesas não são apagados. Desinstalar grava uma desativação local e interrompe o uso do plugin, mas preserva o pacote recuperável e todos os estados das mesas. Por isso, ativá-lo novamente restaura as instâncias sem perda de conteúdo.

Atualizações de plugins instalados são automáticas e independentes da atualização do aplicativo. O host consulta o feed ao iniciar, ao abrir a loja e a cada 30 minutos enquanto estiver aberto; há proteção contra verificações concorrentes e um intervalo mínimo de 15 minutos. Somente versões maiores, compatíveis e com ZIP íntegro são ativadas. Plugins desinstalados não são reativados por uma atualização automática.

## Estratégia MAUI e Android

Os módulos Core já são reutilizáveis. A próxima fase deve criar um contrato de renderização MAUI e renderizadores por plugin, sem mudar comandos ou estado. No Android, o registro dos módulos deve ser estático durante o build: carregamento arbitrário de DLL baixada é incompatível com AOT, distribuição nas lojas e um modelo de segurança aceitável. O mesmo formato de pacote pode continuar sendo usado no Windows; no Android, o manifesto funciona como catálogo de capacidades de módulos compilados no aplicativo.

Pontos que continuam específicos da plataforma:

- clipboard em Android depende de ciclo de vida, visibilidade e restrições da versão do sistema;
- tarefas em segundo plano não podem depender de timers de UI;
- arquivos devem usar Storage Access Framework e URIs, não caminhos Windows;
- atalhos globais, inicialização com Windows, janela flutuante e APIs Win32 não pertencem ao módulo compartilhado;
- rede e sincronização devem respeitar conectividade intermitente e retomada.

Essa separação evita que essas diferenças contaminem as regras dos plugins e deixa o WPF operacional durante toda a migração.
