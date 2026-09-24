# Google, nuvem e colaboração — ClipDesk DEV

> Registro histórico de 15/09/2026. DEV e Production agora usam Supabase. Os comandos de servidor, túnel e OAuth Desktop abaixo documentam a implementação anterior e não fazem parte do aplicativo ou dos pacotes atuais. Use `.\scripts\Start-ClipDeskDev.ps1 -OpenApp` para o DEV e `.\Installer\build-installer.ps1 -Development` para seu pacote. Consulte [a arquitetura atual](SUPABASE-PRODUCTION.md).

Atualizado em 23/09/2026. Esta entrega prepara e testa a estrutura local. O projeto Google Cloud **ClipDesk Development** (`alien-iterator-508518-h6`) está configurado com a Google Drive API, consentimento OAuth externo disponível para todas as contas Google, cliente Desktop e os escopos de identidade e `drive.file`. O login real, a validação da identidade pelo servidor e a criação da pasta particular **ClipDesk DEV** no Drive foram concluídos. O status “Em produção” refere-se somente ao consentimento OAuth do Google; nenhuma versão diária do ClipDesk foi publicada.

Revisão de desempenho e instaladores em 14/09/2026: [mudanças, testes e limites atuais](PERFORMANCE-INSTALLER.md). Os relatos de validação Google real abaixo se referem à rodada anterior; os testes adicionais desta revisão usam contas sintéticas isoladas.

## Abrir a versão de desenvolvimento

O executável atualizado é `bin\Release\net8.0-windows\ClipDesk.exe`, usado pelo atalho `Executar ClipDesk.lnk`. Ele se identifica como **ClipDesk DEV** e guarda seus dados em `%LocalAppData%\ClipDesk-Dev`. A instalação de uso diário e o atualizador dela permanecem separados.

Pré-requisitos: Windows, SDK .NET 10 para a solução/servidor e runtime Desktop .NET 8 para o aplicativo. Nesta máquina, a solução já foi compilada.

Na pasta do projeto, execute:

```powershell
.\scripts\Start-ClipDeskDev.ps1 -OpenApp
```

O script compila a solução em Release/Development e inicia o servidor local, com banco criado automaticamente em `Server\data\development.db`. Para testes somente neste computador, ele atende em `http://127.0.0.1:5278`. Para testar colaboração pela internet, use `-Public`; o servidor continua preso ao computador e recebe conexões externas por um endereço HTTPS do Cloudflare Tunnel. Para reutilizar a compilação, use `-SkipBuild -OpenApp`.

Para encerrar apenas o servidor iniciado pelo script:

```powershell
.\scripts\Stop-ClipDeskDev.ps1
```

Feche o ClipDesk DEV antes de recompilá-lo. Os scripts não encerram a instalação de uso diário e não apagam dados. Os logs do servidor ficam em `Server\data`.

## Configurar o Google pela primeira vez

1. No [Google Cloud Console](https://console.cloud.google.com/), selecione o projeto de testes **ClipDesk Development** (`alien-iterator-508518-h6`), já criado.
2. A **Google Drive API**, o consentimento **ClipDesk DEV** e o cliente OAuth Desktop já estão configurados. O app usa o navegador do sistema e retorno em endereço local com porta temporária, conforme o [fluxo Google para aplicativos nativos](https://developers.google.com/identity/protocols/oauth2/native-app). Esse retorno local agora usa a identidade visual do ClipDesk; a mesma página está em `landing/callback/` para publicação no domínio da landing page, sem transportar código OAuth ou token para o domínio público.
3. Qualquer conta Google pode entrar no ambiente DEV. O OAuth está publicado externamente apenas para remover a lista de usuários de teste.
4. Para reconfigurar uma máquina, baixe o JSON do cliente Desktop pelo Google Cloud Console. Guarde-o fora do repositório e não envie credenciais pela conversa.
5. Configure os dois componentes usando o arquivo baixado:

```powershell
.\scripts\Configure-CloudGoogle.ps1 -CredentialsPath 'C:\caminho\client_secret_desktop.json'
.\scripts\Stop-ClipDeskDev.ps1
.\scripts\Start-ClipDeskDev.ps1 -SkipBuild -OpenApp
```

Se o app já estiver aberto, feche o DEV antes do último comando. A configuração é lida na inicialização. O configurador preserva os demais campos e grava o identificador no servidor e as credenciais Desktop no arquivo local do DEV; não imprime os valores.

6. Clique em **Entrar com Google**, escolha sua conta e autorize o acesso solicitado. A tela de consentimento inclui identificação e `drive.file`. O app cria a pasta particular **ClipDesk DEV** no seu Drive. O escopo limita o acesso aos arquivos criados/usados com o app; não solicita acesso irrestrito a todo o Drive. Veja a [documentação de escopos do Drive](https://developers.google.com/workspace/drive/api/guides/api-specific-auth).
7. Escolha um username único de 3 a 24 letras minúsculas, números ou `_`. Convites usam esse nome.

## Testar colaboração entre dois notebooks

1. No computador que hospedará o teste, execute `./scripts/Start-ClipDeskDev.ps1 -Public`. Nenhuma porta do roteador ou regra de rede local é necessária.
2. Gere o pacote DEV com o endereço HTTPS exibido, por exemplo `./Installer/build-installer.ps1 -Development -ServerUrl 'https://endereco-de-teste.trycloudflare.com'`.
3. Instale `ClipDesk-DEV-Setup.exe` no outro notebook. O pacote encontra o servidor e inicializa o login Google automaticamente; o usuário não vê endereço, IP ou configuração técnica. Esse pacote é exclusivamente para testes e não deve ser publicado fora do grupo autorizado.
4. Cada pessoa pode usar qualquer rede. Entre com contas Google diferentes, escolha usernames diferentes, compartilhe uma mesa por username e aceite o convite no aplicativo. As alterações devem aparecer em tempo real enquanto o computador hospedeiro mantiver o servidor DEV e o túnel abertos.

O endereço do túnel rápido muda quando ele é recriado. Nesse caso, o pacote DEV precisa ser recompilado com o novo endereço. Uma hospedagem permanente substituirá esse túnel antes da distribuição para usuários.

Os arquivos de configuração são `%LocalAppData%\ClipDesk-Dev\cloud-config.local.json` e `Server\appsettings.Development.local.json`. Ambos ficam fora do controle de versão. Tokens da conta são protegidos pelo Windows para o usuário atual.

## Escolher o comportamento de cada mesa

Clique no pequeno estado de salvamento perto do nome da mesa:

| Opção | Comportamento |
| --- | --- |
| Somente local | Mantém a mesa neste computador, mesmo com Google conectado. |
| Nuvem pessoal | Sincroniza esta mesa entre seus dispositivos autenticados. |
| Compartilhar com colaboradores | Ativa a nuvem para a mesa e permite convidar por username. A aceitação cria o ambiente compartilhado. |

O login não envia todas as mesas. As mesas locais existentes são associadas ao primeiro perfil mantendo o modo local. Cada conta tem seu próprio perfil; conectar outra conta não importa mesas de uma conta anterior. Sair retorna ao perfil local sem apagar o cache da conta.

O histórico é privado por conta, tem controle próprio no painel da conta e mantém até 80 entradas visíveis. Compartilhar uma mesa nunca concede acesso ao histórico. Desativar a sincronização do histórico pausa novos envios/recebimentos; não elimina dados já enviados. No DEV, a captura automática do clipboard começa desativada; pode ser ligada no painel da conta. Colar e arrastar itens na mesa continua disponível.

Alterações são salvas localmente antes da confirmação da nuvem. Eventos em tempo real avisam os clientes; reconexão e uma conferência periódica recuperam atualizações após falhas de rede. Alterações simultâneas em campos diferentes são combinadas. Conflitos no mesmo campo ficam preservados para revisão, em vez de sobrescrever silenciosamente uma versão.

Plugins e objetos criativos usam eventos direcionados aos membros autorizados. Digitação, redimensionamento e movimento são agrupados por 180 ms e enviados como uma entidade isolada, sem aguardar a consulta completa. A presença inclui cursor, seleção, centro da visão e zoom. A taxa de envio permanece limitada; suavização do cursor e a viagem de câmera com duplo clique no avatar são animações locais. Um clique simples abre as ações da pessoa, incluindo **Acompanhar visão**, que mantém câmera e zoom ligados às atualizações dela até o usuário interromper, trocar de mesa ou desligar a opção. Durante o acompanhamento, um aviso inferior usa a cor da pessoa e oferece **Parar**. A câmera seguida atualiza seu alvo sem reiniciar a animação a cada mensagem, reduzindo atraso sem aumentar a taxa de presença. O marcador do cursor compensa o zoom para continuar legível em 10%.

A barra criativa possui **Alinhamento magnético**. Quando ligado, cards e objetos encaixam bordas e centros próximos, com guias visuais; `Alt` suspende temporariamente o encaixe e `M` alterna o recurso. A preferência fica salva somente no perfil local.

O painel de conflitos cria uma mesa local de recuperação com o texto e um arquivo contendo o registro completo da alteração. Arquivos e estruturas complexas ainda exigem revisão desse registro. Uma atualização remota limpa a pilha local de desfazer/refazer para evitar restaurar acidentalmente uma versão antiga compartilhada. Sessões do servidor expiram em sete dias; o painel mostra **Reconectar Google** quando uma nova sessão é necessária.

## Arquivos e Drive

- Até **30.000.000 bytes**, inclusive: conteúdo no banco do app; cópia local gerenciada e envio automático quando a mesa/histórico estiver sincronizando.
- Acima desse limite: conteúdo no Drive do proprietário, organizado na pasta particular do ClipDesk e em subpastas por mesa. O banco do app mantém somente metadados e permissões da mesa.
- Arquivo grande pendente: card menos opaco e uma seta para cima sem cor. O dono clica para enviar diretamente à pasta correspondente; não precisa escolher a pasta no navegador.
- A ação de arquivo fica sempre no canto inferior direito do card. Se este computador possui uma cópia local válida, ela mostra uma pasta e abre o Explorador com o arquivo selecionado (ou abre a própria pasta). Se existe apenas a cópia na nuvem, ela mostra a ação de baixar no mesmo lugar.
- Ao concluir e validar um download, inclusive de arquivo pertencente a um colaborador, o card troca imediatamente de **Baixar arquivo** para **Mostrar na pasta**, sem exigir reabertura da mesa. Essa regra vale para anexos no armazenamento do ClipDesk e no Google Drive.
- Arquivos pequenos são recuperados automaticamente para o cache local; arquivos grandes podem ser baixados sob demanda. Em ambos os casos, tamanho e SHA-256 são verificados antes de a cópia ser considerada local.
- O compartilhamento no Drive é concedido aos e-mails dos colaboradores por arquivo individual, como leitor. A pasta não é pública e o app não cria permissão “qualquer pessoa com o link”.

A concessão de acesso aos arquivos de um dono é reconciliada pelo dispositivo autenticado dele. Se ele estiver offline quando um convite for aceito, o acesso a esses arquivos no Drive pode aguardar sua reconexão. A mesa continua sincronizando pelo servidor.

Voltar uma mesa compartilhada para pessoal remove os membros no app e revoga as permissões individuais gerenciadas pelo dono. Se houver arquivos enviados ao Drive de outro colaborador, o DEV bloqueia essa transição e explica que a remoção coordenada ainda está pendente. Retirar da nuvem baixa os anexos necessários antes e mantém uma mesa local com novos identificadores; arquivos do Drive não são apagados. Cópias já baixadas por outras pessoas não podem ser recolhidas.

## Estrutura e confidencialidade

O cliente usa SQLite para documentos, fila de alterações, versões recebidas e conflitos, em um diretório separado por perfil. A primeira leitura importa os JSON locais anteriores sem apagar os originais. O DEV não importa dados da instalação diária.

O servidor mantém usuários, sessões, entidades, membros, convites, operações e anexos. As APIs verificam o usuário e o acesso à mesa em cada operação, inclusive downloads e presença. Sessões são armazenadas por hash; saída encerra também a conexão de tempo real. OAuth usa PKCE, state, nonce e validação do token Google no servidor.

O proprietário pode remover um colaborador pelo painel exibido ao passar sobre o avatar. A exclusão completa aceita todos os tipos atuais de objeto, inclusive checklist, calculadora, tradutor e conversor de moeda, antes de criar as lápides da mesa.

Isso implementa isolamento entre usuários na aplicação. **Não é criptografia de ponta a ponta:** um administrador do banco/servidor pode acessar os dados armazenados. A criptografia dos discos/bancos, backups e acessos administrativos precisa ser configurada na infraestrutura. O acesso remoto de teste usa HTTPS; o servidor de origem permanece acessível somente em `127.0.0.1` e recebe o tráfego externo pelo túnel.

O servidor aceita PostgreSQL via `ConnectionStrings__ClipDesk`; sem conexão configurada, SQLite só é permitido em Development. O caminho PostgreSQL foi compilado, mas ainda não foi exercitado com um servidor PostgreSQL real. O teste atual usa HTTPS público temporário, com endereço já embutido no instalador. Hospedagem permanente, backups e operação com mais de uma instância do servidor ainda precisam ser preparados antes da distribuição para usuários.

## O que foi validado e o que falta

Os testes automatizados usam sessões locais criadas pelo teste e exercitam o servidor real e dois serviços desktop independentes. Não existe endpoint de login fictício no aplicativo. Foram verificados isolamento entre contas, convites, histórico privado, conflitos, sincronização entre clientes, reconexão por consulta, anexos pequenos, hierarquia de pastas, mudanças entre local/nuvem e eventos SignalR. A interface WPF foi renderizada em 900, 1280 e 1920 pixels, com teste de opacidade, ações e lixeira original.

Com uma conta Google real de teste, também foram validados o fluxo OAuth com PKCE, o escopo `drive.file`, o token de identidade e nonce no servidor e a criação/localização da pasta raiz no Drive. A API confirmou que a pasta pertence à conta conectada e não está compartilhada.

```powershell
dotnet build .\ClipDesk.sln -c Release -p:ClipDeskEnvironment=Development
dotnet run --project .\Tests\ClipDesk.Core.Checks.csproj -c Release
dotnet run --project .\Tests\Cloud\ClipDesk.Cloud.Checks.csproj -c Release
dotnet run --project .\Tests\Visual\ClipDesk.Visual.Checks.csproj -c Release
```

Antes de uma entrega aos usuários, ainda será preciso:

1. Validar renovação de tokens, upload interrompido, permissões e downloads com duas contas Google reais. Verificar também a autorização `drive.file` do colaborador para download dentro do app.
2. Configurar e testar PostgreSQL, HTTPS, backups/restauração, retenção, exclusão de conta e quotas de armazenamento. Não há limpeza automática de todos os anexos órfãos nem console de administração nesta versão.
3. Completar a revogação coordenada de arquivos pertencentes a diferentes colaboradores, controles individuais de membros e estados detalhados de concessão de acesso do Drive.
4. Validar carga e grandes históricos: atualmente o cliente consulta o conjunto de entidades autorizado. Log incremental paginado e operação com várias instâncias do servidor não foram implementados.
5. Homologar as jornadas reais na versão DEV e obter autorização explícita para qualquer publicação. O instalador de usuários exige compilação explícita de Production.

O mockup corrigido está em `plans/google-cloud-sync-concept-v2.png` e a captura produzida pela interface WPF implementada está em `plans/google-cloud-sync-implemented-dev.png`. O plano original em `plans/2026-09-11-google-cloud-sync.md` registra a proposta histórica; as correções do usuário e o comportamento descrito neste documento prevalecem.
