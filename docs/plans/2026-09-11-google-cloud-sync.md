# ClipDesk — plano de login Google e sincronização

Data da proposta: 11/09/2026. Registro histórico: a implementação local foi autorizada posteriormente, com correções. Consulte [o estado atual e a configuração DEV](../CLOUD-DEVELOPMENT.md).

Atualização de 12/09/2026: cada mesa escolhe local, nuvem pessoal ou compartilhada; login não sincroniza todas as mesas. A pasta do Drive é particular, com acesso por arquivo aos colaboradores. Upload acima de 30 MB é manual. Ícones e texto de estado são pequenos e sem cor, cards pendentes têm menor opacidade e a lixeira original é preservada. Convites e presença entraram no escopo local autorizado. O mockup corrigido é `google-cloud-sync-concept-v2.png`. As propostas abaixo que divergem dessas decisões foram substituídas; o restante do plano não deve ser interpretado como concluído.

## 1. Direção de produto

O ClipDesk continua funcionando e salvando as mesas localmente, inclusive sem conta e sem internet. Entrar com Google habilita a sincronização das mesas e do histórico pessoal entre dispositivos. Cada conta escolhe um username único. A colaboração entre pessoas será uma etapa posterior, preparada pela arquitetura desta entrega.

A orientação atual do usuário prevalece sobre o fluxo anterior: desenvolver e testar na versão de desenvolvimento; a instalação de uso diário será atualizada pelo próprio usuário através do pop-up. Publicação, commit e envio ao GitHub continuam dependendo de autorização explícita.

Nesta rodada, os entregáveis são este plano e uma imagem gerada para avaliação visual. Nenhum código, executável, instalação ou banco foi alterado/criado.

## 2. Requisitos extraídos da solicitação e das anotações

| Área | Comportamento esperado |
| --- | --- |
| Sem login | Mesas e histórico locais; nada do conteúdo é enviado à nuvem. |
| Login | Botão Entrar com Google no topo; depois do primeiro login, escolher username. |
| Perfil | Foto Google e @username; conta interna vinculada à identidade Google. |
| Mesas | Conteúdo, nomes, organização, agrupamentos, posições e dimensões sincronizados. |
| Histórico | Persistente, sincronizado entre dispositivos da mesma conta e privado. |
| Confidencialidade | Usuários não podem consultar mesas, histórico ou anexos de outras contas. |
| Arquivos até 30 MB | Conteúdo guardado nos bancos do ClipDesk junto dos dados da aplicação. |
| Arquivos maiores que 30 MB | Conteúdo no Google Drive do proprietário, em pasta organizada criada pelo app; metadados no ClipDesk. |
| Upload pendente | Ícone pequeno e discreto no card, com detalhes e ação ao interagir. |
| Estado da nuvem | Indicador ao lado do nome da mesa, atualizado conforme salvamento e envio. |
| Convites futuros | Convidar por username; botão Convidar enquanto a mesa não tem colaboradores. |
| Colaboração futura | Fotos com @username no topo, cor por pessoa, cursores circulares e contorno de seleção na mesma cor. |
| Opções futuras | Ao passar o mouse sobre um avatar, painel com opções, incluindo mostrar/ocultar cursor. Também acessível por clique e teclado. |
| Aparência | Preservar grade escura, identidade roxa, transparência e componentes próprios do ClipDesk. |

As anotações visuais foram interpretadas como referência de experiência e escopo futuro. Elas não autorizam implantação remota nem antecipam a colaboração para a primeira entrega.

## 3. O que existe hoje

- Aplicativo Windows em WPF, com projeto principal em .NET 8 e biblioteca `ClipDesk.Core` independente de WPF.
- `StorageService` salva mesas, itens, configurações e histórico em JSON dentro de `%LocalAppData%\ClipDesk`.
- O histórico já persiste entre sessões e mantém 80 registros. O comentário de `ARCHITECTURE.md` que o descreve como temporário está desatualizado.
- Mesas e cards já possuem identificadores estáveis. Faltam proprietário, versão de sincronização, exclusão lógica e operações pendentes.
- Arquivos e pastas são principalmente referências a caminhos do computador. Salvar a mesa atualmente não equivale a copiar todos os arquivos para um acervo próprio.
- `MainWindow.xaml.cs` concentra salvamento e interação; é necessário extrair responsabilidades gradualmente.
- Desenvolvimento e instalação compartilham atualmente pasta de dados, nome de instância única e evento de ativação. Só compilar em outra pasta não isola os ambientes.
- O atualizador instalado consulta releases do GitHub. A separação deve preservar esse caminho para o teste real do pop-up pelo usuário.

## 4. Proposta de interface

Imagem de referência para aprovação: `google-cloud-sync-concept-v1.png`, nesta pasta. Foi produzida com a ferramenta nativa de geração de imagens; é uma simulação, não uma tela funcional. Fotos e conteúdos são ilustrativos.

O painel superior da imagem representa a entrega pessoal com nuvem. A faixa inferior compara o estado sem login e a colaboração futura. Essa faixa explicativa não fará parte da janela real.

### Barra superior

Preservar ferramentas, seletor de mesa, busca, zoom e lixeira. Inserir estado de salvamento perto do nome da mesa e conta/convites à direita. Em janelas menores, compactar rótulos e avatares em componentes próprios sem sobrepor os controles.

| Estado real | Texto/ação |
| --- | --- |
| Sem login | Salvo neste computador + Entrar com Google. |
| Alterações locais ainda não confirmadas | Salvando… ou Sincronizando… conforme a etapa. |
| Tudo confirmado, inclusive anexos | Sincronizado, com check discreto. |
| Mesa confirmada, arquivo ainda pendente | Mesa salva + 1 arquivo pendente; nunca sinalizar sincronização total. |
| Sem rede, conta ainda autenticada localmente | Salvo neste computador · aguardando conexão. |
| Permissão Google expirada/revogada | Reconectar Google ou Reconectar Drive, conforme a causa. |
| Falha recuperável | Não foi possível sincronizar + tentar novamente e detalhes. |

O check da nuvem exige confirmação de persistência do servidor. O indicador local exige gravação local concluída.

### Conta e primeiro acesso

1. Abrir autenticação Google no navegador do sistema; durante testes automatizados, usar o Opera GX isolado conforme as instruções do projeto.
2. No primeiro login, mostrar painel próprio para escolher `@username`, validar disponibilidade e explicar que ele será usado para convites.
3. Mostrar claramente que conectar a conta ativa a nuvem para mesas e histórico. Apresentar os dados locais que serão associados à conta antes do primeiro envio, especialmente em um computador já utilizado.
4. Retornar à mesa imediatamente e mostrar o progresso em segundo plano.
5. Trocar o botão Google pelo avatar e username. O painel de conta oferece estado da sincronização, conexão do Drive, pausa e saída.

### Arquivo grande

Ao adicionar, guardar o item localmente e exibir pequeno ícone de nuvem com seta. Hover/clique mostra nome, tamanho, estado e ação. Proposta: quando o Drive já estiver autorizado, iniciar automaticamente o upload; sem autorização, oferecer Conectar Google Drive. Permitir pausa e nova tentativa.

Um arquivo pendente aparece nos demais dispositivos como item ainda indisponível para download. O app continua utilizável durante a transferência.

### Colaboração futura

Preservar o espaço do botão Convidar; enquanto a função não existir, apresentar uma indicação clara de disponibilidade futura, sem fluxo que prometa enviar convites. Na etapa colaborativa, convidar em uma mesa local inicia login/sincronização antes do envio do convite.

Depois: avatares com cores individuais, username abaixo ou em rótulo compacto, cursores em anel, seleção com contorno correspondente e painel personalizado para ocultar cursores. Cor deve ser acompanhada de nome para acessibilidade. Preferências de visualização ficam por dispositivo.

## 5. Arquitetura proposta

| Componente | Responsabilidade |
| --- | --- |
| Cliente WPF + Core | Interface, funcionamento offline, modelos e aplicação de operações. |
| SQLite local por perfil | Mesas, histórico, índice de anexos e fila persistente de sincronização. |
| Acervo local de arquivos | Cópias gerenciadas dos anexos; caminhos originais tratados como informação local. |
| API própria em ASP.NET Core | Autenticação, autorização, usernames, operações, anexos e sessões. |
| PostgreSQL | Dados das contas, mesas, histórico, alterações e conteúdo binário de arquivos até 30 MB. |
| SignalR | Avisar dispositivos autorizados de novas operações; posteriormente, presença e cursores. |
| Google Drive API | Transferir e recuperar arquivos acima do limite na conta do proprietário. |

Essa proposta usa banco de dados também para os binários pequenos, atendendo literalmente ao pedido. Substitui a recomendação anterior de armazenamento de objetos em `ARCHITECTURE.md`. Anexos terão tabelas separadas e transferência em blocos; não serão embutidos em grandes mensagens da mesa. O custo é aumentar volume do banco, backups e tráfego: medir antes de escolher o plano de hospedagem.

Proposta para o novo servidor: .NET 10, validando compatibilidade do cliente durante implementação. A atualização do runtime do desktop será avaliada separadamente; a nuvem não exige reescrever a interface.

O servidor será necessário para sincronização real entre computadores. Preparar primeiro API e banco locais para testes; escolher hospedagem, região, orçamento e configuração de produção em aprovação própria. Desenvolvimento e produção terão bancos, credenciais OAuth e endpoints separados.

## 6. Banco e isolamento

| Entidade | Conteúdo principal |
| --- | --- |
| users | ID interno, identidade Google (`issuer` + `sub`), username normalizado, nome/foto e datas. |
| devices / sessions | Dispositivos, sessões revogáveis e progresso de sincronização. |
| workspaces | ID, proprietário, nome, versão e exclusão lógica. |
| workspace_members | Relação usuário/mesa e papel; inicialmente apenas proprietário. |
| workspace_items | Conteúdo e geometria dos cards, grupo pai, versão e exclusão lógica. |
| clipboard_entries | Histórico vinculado exclusivamente ao usuário, sem herdar membros de mesas. |
| attachments / attachment_chunks | Proprietário, tamanho, tipo, integridade e binários até 30 MB, ou referência Drive. |
| attachment_links | Uso do mesmo anexo por cards/histórico, para excluir sem quebrar outras referências. |
| sync_operations / changes | ID único da operação, versão, sequência do servidor e confirmações. |
| invitations | Contrato reservado para a futura etapa; convites ainda não ativos. |

Username proposto: 3–24 caracteres, letras ASCII minúsculas, números e `_`; reservar nomes administrativos e validar unicidade no banco, inclusive em cadastros simultâneos. O ID interno permanece o mesmo se o username mudar. Email não será chave de propriedade nem dado público de busca.

Todas as consultas, downloads e inscrições de eventos verificam usuário e permissão no servidor. O cliente nunca escolhe livremente o proprietário. Aplicar políticas de linha no PostgreSQL como defesa adicional, usando papel de aplicação sem privilégios que contornem essas políticas. Testar também reutilização de conexões para impedir que contexto de conta vaze entre requisições.

O diretório público futuro expõe somente o necessário para identificar um convite: username, nome e avatar autorizado. Sem listagem pública de emails, mesas ou histórico; limitar consultas para reduzir enumeração.

## 7. Autenticação e confidencialidade

- Login Google com Authorization Code, PKCE S256, `state`, `nonce`, callback loopback temporário e prazo de expiração. Nenhuma senha Google é solicitada dentro do app.
- Validar assinatura, emissor, audiência e validade do ID token no servidor, além do vínculo com a tentativa de login. Criar uma sessão própria do ClipDesk vinculada à identidade validada.
- Desktop é cliente público: um segredo incluído no executável não constitui proteção. Segredos do servidor ficam apenas no ambiente servidor.
- Tokens locais protegidos com recursos do Windows, como DPAPI; sessões revogáveis, curta duração de acesso e renovação controlada.
- TLS nas transferências; criptografia em repouso do banco, anexos e backups; acesso administrativo restrito. Logs sem conteúdo do clipboard, tokens ou caminhos pessoais.
- Revogar sessão e interromper filas ao sair; separar rigorosamente o perfil da conta A, da conta B e do modo local. Cache permanece local por padrão, mas não aparece para outra conta; oferecer remoção explícita.
- Perfil local e cache não protegem contra alguém que já controla a mesma sessão Windows. Credenciais, backups e arquivos temporários precisam acompanhar o nível de privacidade aprovado.

**Decisão de aprovação:** o desenho-base oferece confidencialidade entre usuários, mas não é criptografia ponta a ponta. Administradores com acesso privilegiado à infraestrutura podem tecnicamente acessar dados. Se a intenção é que nem a equipe do ClipDesk possa ler o conteúdo, incluir criptografia ponta a ponta desde a base, antes de implementar sincronização.

Nesse caso, usar chaves de conteúdo por usuário/mesa, proteção autenticada dos anexos antes do envio (inclusive Drive), distribuição de chaves a dispositivos autorizados e código de recuperação. O login Google sozinho não recupera essas chaves. Compartilhamento futuro terá de distribuir/revogar acesso às chaves; busca de conteúdo será local. Decidir também se nomes de arquivos e títulos serão cifrados. Sem chaves ou recuperação, o conteúdo cifrado não é recuperável pelo suporte. Nenhuma variante promete confidencialidade absoluta contra um dispositivo comprometido.

## 8. Sincronização e conflitos

1. Cada ação confirma uma transação local contendo a alteração e uma operação pendente. Fechar o app ou perder energia não deve apagar uma operação já confirmada.
2. Com login e rede, enviar operações em lotes pequenos, com ID idempotente e versão esperada. Repetir um envio nunca deve duplicar itens.
3. Servidor valida permissão, grava alteração e evento na mesma transação e confirma o resultado.
4. Canal em tempo real avisa dispositivos autorizados. Ao reconectar, buscar alterações após a última sequência confirmada; o socket sozinho não é uma fila durável.
5. Aplicar mudanças recebidas localmente sem reenviá-las como novas ações. Histórico remoto não escreve automaticamente no clipboard do Windows e não gera ciclos de captura.
6. Sincronização inicial usa paginação e ponto de referência consistente. Fazer ressincronização completa quando o cliente estiver mais antigo que a retenção dos eventos.

Não enviar o JSON inteiro da mesa a cada movimento: usar operações por card/campo, agrupando movimentos durante arrasto. Posição e tamanho concorrentes podem seguir a última versão aceita pelo servidor; não confiar no relógio do computador. Alterações independentes se combinam. Para edição concorrente de texto, manter a versão rejeitada como conflito recuperável, sem descarte silencioso. Edição de texto realmente simultânea exige mecanismo específico na fase colaborativa.

Exclusões usam marcadores persistentes para evitar que dispositivos offline ressuscitem itens. Undo/redo passam a produzir operações inversas locais, respeitando alterações remotas, sem restaurar uma cópia antiga de toda a mesa.

Meta inicial de aceitação: uma alteração pequena aparecer no segundo cliente em até 1 segundo no percentil 95 sob conexão estável e carga de teste definida. Isso será medido; não é uma garantia universal. Offline significa gravação local imediata e convergência após retorno da conexão. Arquivos grandes têm progresso independente e dependem da rede e do Drive.

## 9. Arquivos e Google Drive

Proposta objetiva para o limite: até **30.000.000 bytes**, inclusive, no banco do app; acima disso, Drive. Aplicar ao tamanho original de cada arquivo antes de compressão/criptografia. Se preferir 30 MiB, alterar o contrato antes de implementar para manter cliente e servidor iguais.

### Acervo local

Criar cópia estável do arquivo antes de confirmar que está preservado pelo ClipDesk. Migração de caminhos antigos verifica existência e acesso; arquivos ausentes permanecem identificados como indisponíveis. Falta de espaço ou falha de cópia deve ficar visível, sem confirmação enganosa.

Pastas virtuais do ClipDesk sincronizam sua estrutura. Pastas reais adicionadas ao app serão representadas por estrutura e arquivos individuais, com tratamento para links simbólicos, ciclos, permissões e arquivos alterados durante a cópia. A regra de 30 MB é por arquivo. Proposta: preservar a versão capturada; monitorar continuamente alterações externas é outro recurso.

### Armazenamento do app

Anexos pequenos ficam em tabelas de conteúdo binário separadas dos metadados. Validar tamanho e integridade no servidor, finalizar atomicamente e limpar uploads incompletos. Deduplicação restrita ao proprietário, sem expor se outro usuário possui determinado conteúdo. Só marcar anexo disponível depois de conferir o envio completo.

### Drive do proprietário

- Solicitar acesso ao Drive quando necessário, em fluxo separado do login básico; usar `drive.file` e os escopos necessários de identidade, com acesso limitado aos arquivos gerenciados pelo app.
- Criar pasta comum e visível `ClipDesk`, com subpastas `Mesas/<nome>-<id>/` e `Historico/`. Guardar IDs das pastas/arquivos: nomes podem ser modificados pelo usuário.
- Não usar `appDataFolder`: ela não permite compartilhamento e impediria o caminho planejado para colaboração.
- Enviar em segundo plano com upload retomável, sessão persistida com proteção e retentativas com espera progressiva. Recriar sessão se expirar.
- Transferir conteúdo do cliente ao Drive; o banco do ClipDesk guarda referência, proprietário, tamanho, integridade e estado. Nenhum binário acima do limite é persistido no banco do app.
- Tratar quota cheia, arquivo removido, permissão revogada, interrupção, download falho e mudança de conta. A cópia local continua disponível.
- Histórico e mesa podem referenciar o mesmo arquivo. Remover um card não deve apagar uma cópia do Drive que ainda seja utilizada por outro item.
- No futuro, conceder acesso específico a colaboradores, nunca tornar arquivos públicos. Username resolve identidade no ClipDesk; acesso Google exige a identidade Google correspondente e pode depender de restrições da organização do proprietário.
- Permissões da mesa e do Drive precisam ser reconciliadas, inclusive após remoção de participante e alterações externas. Acesso a arquivo no Drive concedido diretamente fora do app é uma permissão independente. Downloads já realizados não podem ser recolhidos remotamente.

## 10. Histórico, retenção e exclusão

Histórico continua sendo pessoal, mesmo quando um item dele é colocado em uma mesa compartilhada. Só o item adicionado e seu anexo são compartilhados. Não transmitir o fluxo global de clipboard aos colaboradores.

Proposta inicial para aprovação: manter o limite atual de 80 entradas, agora por conta, com ordenação determinística e deduplicação no escopo do usuário. Diferenciar remoção de cache local de exclusão explícita na nuvem. Expandir a retenção pode ser feito depois de medir armazenamento e custo; não presumir histórico ilimitado.

Disponibilizar limpar histórico, excluir mesa, exportar dados, sair e excluir conta, com efeitos claros. Proposta de retenção: lixeira recuperável por 30 dias e histórico técnico de alterações suficiente para reconexão; backups cifrados por até 30 dias, com reaplicação de exclusões em restaurações. Documentar o prazo real de eliminação. Anexos sem referência seguem coleta após a janela de recuperação.

Na exclusão da conta, oferecer opção explícita para manter ou remover arquivos criados pelo ClipDesk no Drive; se a autorização tiver sido revogada, informar que a remoção no Drive depende do próprio usuário. Nunca remover arquivos externos ao acervo gerenciado.

## 11. Etapas de implementação e aceite

| Etapa | Entrega | Como validar |
| --- | --- | --- |
| 0 — Isolar desenvolvimento | Perfil DEV, diretório próprio, identificação visual, mutex/eventos/atalhos separados, inicialização e atualização adequadas ao ambiente; atualizar instruções do projeto. | Abrir DEV e uso diário juntos; confirmar dados, bandeja e ativação separados; manter atualizador instalado funcionando. |
| 1 — Persistência local | SQLite, migração com backup e rollback, acervo de anexos, operações locais e separação por perfil. | Reiniciar, simular escrita interrompida, validar mesas/pastas/imagens/histórico; dados antigos preservados. |
| 2 — API e identidade | Banco com migrações, Google OAuth, sessões, perfil, username e autorização. | Login/cancelamento/expiração, username simultâneo, duas contas tentando acessar dados uma da outra. |
| 3 — Mesas e histórico | Fila, protocolo incremental, eventos em tempo real, conflitos, exclusões e estados da UI aprovada. | Dois dispositivos, alterações simultâneas, offline, reconexão, ausência de duplicação e de ciclos de clipboard. |
| 4 — Anexos pequenos | Conteúdo até 30 MB no PostgreSQL e recuperação local. | Limites exatos, integridade, retomada, autorização por anexo, exclusão com referências compartilhadas. |
| 5 — Drive | Pasta própria, consentimento, upload/download retomável e indicador sutil. | Arquivos acima do limite, rede interrompida, quota cheia, arquivo removido, permissão revogada e conta diferente. |
| 6 — Estabilização local | Testes de integração, acessibilidade, responsividade WPF, desempenho, exportação/exclusão e restauração de backup. | Cenários abaixo aprovados, consumo de recursos medido, pacote local da versão DEV validado. |
| 7 — Colaboração futura | Convites por username, papéis, presença, cursores, seleção e permissões Drive. | Nova aprovação de escopo; testes de edição concorrente, revogação e privacidade do histórico. |

Na etapa 0, reservar um caminho como `%LocalAppData%\ClipDesk-Dev` e identificar claramente `ClipDesk DEV`. Caso as duas instâncias disputem o atalho global atual, DEV usará combinação diferente configurável. Captura da área de transferência de testes deve ser controlável para não enviar conteúdo de uso diário acidentalmente. Release é uma configuração de compilação e não deve determinar sozinha o ambiente: manter configuração explícita DEV/produção. Quando autorizado a implementar, gerar e validar a Release local de desenvolvimento em `bin\Release\net8.0-windows\ClipDesk.exe`, sem copiar para a pasta instalada.

Os testes de integração de autenticação, migração, autorização e sincronização são parte obrigatória desta mudança. A aprovação de cada etapa será sobre comportamento executável, sem declarar concluída uma função apoiada apenas em dados simulados.

## 12. Cenários mínimos de aprovação funcional

1. Sem login: criar mesa, adicionar conteúdo, fechar e reabrir; nenhum conteúdo enviado.
2. Conta A: entrar, escolher username, sincronizar e recuperar em outro dispositivo.
3. Conta B: não visualizar nem baixar conteúdo de A, inclusive alterando IDs em requisições e inscrições de eventos.
4. Offline: editar em dois dispositivos, reconectar, convergir sem apagar silenciosamente o texto divergente.
5. Reiniciar durante envio: retomar sem duplicar cards, histórico ou anexos.
6. Arquivos de 29.999.999, 30.000.000 e 30.000.001 bytes: destinos e indicadores corretos.
7. Arquivo grande pendente: mesa disponível em outro dispositivo e anexo identificado como pendente.
8. Remover card/histórico: respeitar referências de anexos; exclusão não reaparece após cliente offline reconectar.
9. Sair e trocar de conta: nenhum cache, token, operação pendente ou evento entregue ao perfil errado.
10. DEV e instalação funcionando simultaneamente: nenhum conflito de dados, ativação ou atualização.
11. Backup restaurado: banco e anexos coerentes; exclusões anteriores respeitadas.
12. Fase futura: convidado acessa apenas mesa autorizada e seus anexos; histórico do proprietário permanece privado.

## 13. Aprovações necessárias

- **Visual:** aprovar ou ajustar a imagem antes de qualquer mudança no front-end.
- **Escopo:** login + nuvem pessoal agora; colaboração preparada e executada posteriormente.
- **Arquitetura:** SQLite local, API própria, PostgreSQL com binários até 30 MB e Drive acima disso.
- **Confidencialidade:** confirmar se basta isolamento entre usuários com criptografia em trânsito/repouso, ou se é requisito impedir também a leitura pela equipe/infraestrutura com criptografia ponta a ponta. Isso muda a etapa 1 em diante.
- **Comportamentos propostos:** 30 MB decimais; upload automático após autorizar Drive; histórico de 80 itens por conta; captura de versões dos arquivos; lixeira/backups de 30 dias.
- **Infraestrutura, depois:** conta/projeto Google, identificadores OAuth, tela de consentimento, domínio, hospedagem/região, orçamento, quotas e backups. Credenciais devem ser configuradas no ambiente seguro, não coladas na conversa.

A solicitação atual autoriza planejamento e prévia visual. Implementação começará após aprovação do usuário. A aprovação do desenvolvimento não autoriza publicação no GitHub nem atualização manual da instalação de uso diário.

## 14. Referências oficiais conferidas

- [Google OAuth para aplicativos desktop: PKCE e loopback](https://developers.google.com/identity/protocols/oauth2/native-app).
- [Google: validar identidade no backend](https://developers.google.com/identity/sign-in/web/backend-auth).
- [Drive: escopos e acesso limitado com drive.file](https://developers.google.com/workspace/drive/api/guides/api-specific-auth).
- [Drive: uploads retomáveis](https://developers.google.com/workspace/drive/api/guides/manage-uploads).
- [Drive: limitações da pasta oculta de dados de aplicativos](https://developers.google.com/workspace/drive/api/guides/appdata).
- [Microsoft: SignalR e comunicação em tempo real](https://learn.microsoft.com/en-us/aspnet/core/signalr/introduction?view=aspnetcore-10.0).
- [PostgreSQL: políticas de segurança por linha e exceções de privilégio](https://www.postgresql.org/docs/current/ddl-rowsecurity.html).

Essas fontes sustentam os mecanismos disponíveis; a arquitetura, os limites de produto, as metas de latência e a ordem de entrega são propostas específicas deste plano.
