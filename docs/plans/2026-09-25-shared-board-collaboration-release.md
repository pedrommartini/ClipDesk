# Plano de correção e publicação — colaboração, plugins e diagnóstico

Status: implementação iniciada no DEV; deploy ainda não executado.
Data: 25/09/2026.
Base: [diagnóstico da sessão Pedro/Renan](../diagnostics/2026-09-25-shared-board-sync.md), [Dev Kit atual](../PLUGIN-DEVKIT.md), [processo de release](../RELEASE-PROCESS.md) e revisão das branches locais.

## Progresso até 26/09/2026

Implementado no checkout DEV, ainda sem release:

- A coleta local precede uma leitura remota completa e paginada; um 403 em uma mesa deixa sua operação pendente, registra metadados sem conteúdo e não impede outra mesa de avançar. A tentativa recusada tem intervalo de espera de um minuto. Dependentes de uma mesa não criada aguardam a criação.
- A retirada de acesso preserva edições locais pendentes na área de conflitos antes de ocultar a mesa. O estado de sincronização mostra pendência e erro de permissão com mais clareza.
- O marcador do cursor compensa corretamente o zoom; seleções paradas e múltiplas passam pela presença temporária e expiram junto com o cursor.
- O carregador de plugins classifica falhas, gera um incidente local sem dados do documento, mostra o código na instância e permite copiar o relatório. Estado de plugin com versão futura não é aberto por um renderer antigo, e callbacks de renderers substituídos não podem sobrescrever o estado atual.
- Regressões de 403 entre mesas, paginação, recuperação de edição local, posição do cursor, seleções e falha/reparo de plugin passaram nas suítes Cloud e Visual.
- A ponte `workspace.assets` armazena anexos de plugins por ID portátil, com importação, cópia local autenticada e verificação de integridade. Conversor de arquivos, compressor, upscale, gravador e TTS já preservam entradas, saídas e configurações em estado compartilhado; testes locais reconstruíram esses arquivos numa segunda instância sem repetir a operação.
- Relógio, cronômetro, temporizador, fuso horário e QR Code foram importados com inventário de origem/hash e tiveram estado portátil e entrada tardia verificados. Os quatro plugins incluídos já têm testes de estado na nuvem. Cinco pacotes adaptados passaram pelo validador do Dev Kit e foram gerados com versão mínima de host 0.4.4.
- Prévia temporária de desenho e formas, seleção parada/múltipla e desfazer/refazer por alteração local foram implementados e passaram nos testes de interface e núcleo. O desfazer ignora uma entidade modificada em outro cliente, preservando alterações remotas independentes.
- O diagnóstico de plugin distingue pacote ausente, desativado, manifesto inválido, host/contrato incompatível e montagem ausente; registra versões do host/SDK e evita dados do documento. A recuperação por reparo do pacote foi testada.
- As suítes Release de Core, Cloud, Dev Kit, Installer e Visual passaram, inclusive as verificações de entrega e loja de plugins. O GitHub mostrava 0.4.3 como última versão do aplicativo em 26/09/2026; 0.4.4 foi escolhida como candidata no checkout DEV.

**Bloqueios de release ainda abertos:** a operação 403 específica da mesa Pedro/Renan ainda não foi identificada pela fila local; faltam testes com dois clientes reais e com os pacotes ZIP novos instalados, além de casos concorrentes de texto/notas, interação de controle e zoom/DPI. O fluxo de anexos de plugin ainda limita arquivos a 30 MB; arquivos maiores precisam de uma solução definida e verificada. O gravador precisa manter captura local durante atualizações remotas da instância. A matriz de DEV↔DEV, Production↔Production e DEV↔Production, geração/verificação de instaladores e revisão da landing não foram executadas. Por isso, nenhuma versão DEV ou Production está pronta para publicação.

## Decisão e resultado esperado

Corrigir o ClipDesk WPF atual em `codex/clipdesk-dev`, validar e publicar DEV, integrar o conjunto aprovado em `main` e publicar Production. Construir regras e contratos de colaboração sem dependências de WPF para reaproveitamento no ClipDesk New.

O escopo é **todos os plugins do catálogo, os quatro incluídos e as ferramentas nativas da mesa: desenho, texto, notas, seleção, formas e conectores**. Upscale foi um exemplo. Cada instância deve compartilhar configurações, entradas, documentos/anexos, resultados e estado de execução relevante, inclusive para quem entra depois. A mesa deve convergir em conteúdo, posições, dimensões e exclusões. Um plugin ausente, incompatível ou com falha deve preservar seus dados e explicar o problema.

Configurações do documento/instância são compartilhadas. Credenciais, permissões do sistema, dispositivos de áudio e caminhos de destino pertencem ao dispositivo. O recebimento de estado remoto não deve repetir efeitos como gravar microfone, tocar áudio, exportar arquivos, executar processamento pago ou copiar conteúdo para o clipboard.

## ClipDesk New: recomendação

Não iniciar a migração de interface como pré-requisito desta correção. A branch `clipdesk-new` já existe e contém trabalho de contrato/Dev Kit; não é uma reescrita pronta. O plano de migração ainda registra a fundação compartilhada como próxima fase e o shell MAUI Windows como não iniciado. A inspeção da árvore dessa branch não encontrou um host MAUI ou os projetos Domain/Application/Sync separados.

A fila bloqueada, os arquivos de plugins não compartilhados e a recuperação de estado precisam ser resolvidos em qualquer interface. Começar pelo New acrescentaria implementação de host, renderers, migração de dados e validação de paridade antes de entregar a correção aos usuários atuais.

Reaproveitamento proposto: DTOs, regras de conflito, referências de mídia, sessões de plugin, contratos de erro e testes portáveis. WPF fica responsável por adaptação visual, entrada e serviços do Windows. Levar as mudanças portáveis aprovadas ao New em integração separada, preservando suas diferenças; não promover a branch inteira para main.

## 0. Preparação, preservação e identificação da operação recusada

Entregas:

- Fixar commits-base de DEV e main, inventário de plugins/versões, esquema remoto e pacote instalado. Usar checkouts isolados e diretórios de saída separados para cada edição; há trabalho paralelo e diferenças entre branches.
- Exportar um retrato consistente da mesa e da fila pelo próprio aplicativo/conexão de dados ativa, com backup dos anexos. A leitura comum de arquivos durante o diagnóstico não correspondeu ao estado visível; não usá-la como base para recuperação.
- Acrescentar diagnóstico local estruturado de sincronização: mesa, entidade, operação, versão-base, etapa, código HTTP/SQL, contagem de tentativas e horário. Excluir tokens, mídia e texto dos documentos.
- Identificar o ID e a condição exata da operação `workspaces` recusada. Reproduzir a situação real em mesa descartável: criação, versão zero desatualizada, falta de propriedade ou participação removida. O defeito de bloqueio global já foi reproduzido; o motivo específico daquele 403 ainda não foi confirmado.
- Fotografar a matriz de compatibilidade real: main/DEV executam v1/v2; o SDK tem interfaces v3, mas o carregador Windows atual não ativa automaticamente módulos v3.

Aceite: cópia recuperável verificada, operação bloqueadora identificada e caso de regressão correspondente. Não substituir a mesa do servidor pela cópia local inteira para tentar recuperar a sessão.

## 1. Sincronização resiliente de mesas e elementos

Áreas principais: `Services/CloudSyncService.Facade.cs`, `Services/SupabaseRestClient.cs`, `Core/LocalDatabase.cs`, projeção de entidades e `MainWindow.Cloud.cs`.

- Separar coleta local, reconciliação remota, envio de entidades e transferência de anexos. Uma falha de envio ou upload não deve impedir o recebimento de mudanças.
- Reconciliar identidade, propriedade e versão antes de inserir uma mesa de versão local zero. Tratar criação repetida e confirmação perdida de forma idempotente. Um editor não tenta publicar metadados exclusivos do dono.
- Isolar falhas por mesa e operação. A criação da mesa é dependência de seus elementos; uma dependência recusada fica pendente com explicação. Outras mesas e operações independentes continuam.
- Distinguir conflito de versão, falta de autorização, sessão expirada, limitação temporária e ausência de rede. Usar recuperação adequada e intervalos de repetição; evitar ciclos de 403 a cada dois segundos.
- Fazer atualização inicial completa e reconciliação após reconexão, com paginação e ordenação estável. Não interpretar uma resposta parcial ou leitura fracassada como remoção de acesso ou exclusão de entidades.
- Preservar alterações locais durante leituras remotas; mesclar campos independentes e manter conflitos reais revisáveis. Confirmar operação pela identidade/versão, evitando duplicação se a resposta de um envio se perder.
- Reavaliar inscrição em canais após reconexão e expiração de sessão. Estado de conexão deve refletir o transporte atual.
- Exibir estado por mesa: sincronizada, alterações pendentes, offline ou falha que precisa de ação. `Salvo localmente` e `Sincronizado com participantes` precisam ter significados claros.

Aceite: falha na mesa A não bloqueia B; o cliente com pendências recebe mudanças remotas; entrada tardia e reabertura recuperam o conteúdo inteiro; permissões do Supabase continuam restritas aos membros/dono corretos.

## 2. Contrato comum de colaboração no Dev Kit

Áreas: `PluginSdk`, `PluginSdk.Windows`, `PluginModuleSession`, contextos de execução, validador, templates e documentação.

- Definir o estado compartilhado por instância como fonte persistente das configurações, entradas, resultados e referências de anexos. O renderer não é o único dono de informação necessária para reconstruir o plugin.
- Definir revisões e alterações por campo/comando, com ID de operação. Evitar que um comando assíncrono iniciado sobre estado antigo sobrescreva uma alteração remota posterior. Usar IDs estáveis para itens de listas.
- Definir serviço do host para anexos da instância: importar um arquivo selecionado, publicar referência portátil, obter stream/cache local e liberar referências. Nomes de interfaces/capability serão estabilizados junto com a implementação e o versionamento do contrato.
- Manter bytes grandes, streams, caminhos Windows e segredos fora de `PluginState`. Reutilizar o armazenamento privado da mesa, com integridade, limites, cancelamento, progresso e retomada. Referenciar arquivos por identidade compartilhada, não por handle válido apenas em um dispositivo.
- Definir operações longas com executor, ID, revisão de entrada, estado e resultado. Executar uma vez; receber o resultado não dispara a operação novamente. Falha ou desconexão do executor deixa estado recuperável, sem tomada automática de microfone ou processamento externo.
- Separar estado persistente de eventos temporários, como progresso e indicação de controle em interação. Eventos temporários expiram; a conclusão é persistida.
- Implementar primeiro a ponte aditiva necessária aos pacotes Windows v2, preservando assinaturas existentes. Os mesmos contratos portáveis serão utilizáveis por capabilities v3. Só declarar execução v3 nos hosts quando registro, renderer, lifecycle e serviços realmente existirem e forem testados.
- Negociar versão do app, contrato, pacote e estado. Um host/pacote que não entende o estado deve conservar os dados e pedir atualização, sem regravá-los com defaults. Publicar `minAppVersion` coerente para os novos pacotes.
- Acrescentar exemplos colaborativos e verificações ao Dev Kit: configuração, arquivo compartilhado, comando assíncrono, recebimento remoto, migração e erro. A análise estática ajuda a detectar violações, mas os testes de comportamento comprovam a colaboração.

Aceite: o mesmo módulo recebe estado em dois contextos e reconstrói a instância, incluindo anexos, sem depender de memória do primeiro renderer. Normalização é idempotente e não perde campos desconhecidos compatíveis.

## 3. Adequar todos os plugins e sua interface

Inventário atual: quatro incluídos e dez opcionais. Revisar o pacote efetivamente distribuído, sua fonte e hash; uma fonte externa com o mesmo número de versão não prova equivalência ao ZIP publicado.

| Grupo | Plugins | Estado a validar nos dois clientes |
| --- | --- | --- |
| Incluídos | Calculadora, Checklist, Tradutor, Conversor de moedas | Valores, listas, seleções, configurações e resultados |
| Relógios | Relógio Mundial, Fuso Horário, Cronômetro, Temporizador | Cidades/fusos, duração, execução, pausa e marcos temporais |
| Arquivos | Conversor de Arquivos, Compressor, Upscale | Entradas, opções, fila/trabalho, progresso e referências de saída |
| Áudio | Gravador, TTS | Configurações da instância, texto quando houver, trabalho e áudio concluído |
| Código visual | QR Code | Entrada, aparência e resultado reproduzível |

- Para cada plugin, listar todos os controles e dados que hoje existem apenas no renderer; migrar o que define o documento para o estado/capability comum.
- Relógios e temporizadores usam tempos de referência e compensação de diferença entre relógios, evitando publicar cada tick ou contar independentemente em cada máquina.
- Gravação, dispositivos e reprodução permanecem ações explícitas de um participante. O arquivo concluído e a configuração compartilhável ficam disponíveis aos demais.
- Atualizar views a partir de mudanças remotas preservando foco, edição em andamento, seleção, assinaturas e estado de execução. Substituir reconstruções desnecessárias do controle por atualização de estado; quando houver reconstrução, reidratar tudo a partir do modelo.
- Instância sem pacote continua recebendo e preservando estado/anexos. Depois de instalar/reparar, deve exibir o conteúdo atual com o mesmo ID e posição.
- Versionar e republicar todos os pacotes que exigirem adaptação. Plugins já conformes também entram na matriz de testes. Não considerar esta fase concluída apenas porque upscale passou.

Aceite: matriz completa dos 14 plugins, sem lacunas silenciosas. Para futuros plugins, exemplos e validação de compatibilidade do Dev Kit orientam a implementação; o host não consegue tornar colaborativos campos privados arbitrários de plugins antigos.

## 4. Ferramentas nativas, presença, cursor e geometria

A observação adicional do usuário abrange desenho, texto, seleção e notas. A revisão do código confirma que `MainWindow.CreativeTools.cs` mantém o traço em andamento em `_draftStroke`, cria o objeto persistente ao finalizar e usa caminhos de `Save` e atualização rápida para ações distintas. Em `RenderPresence`, a seleção remota é derivada dos objetos arrastados: não representa atualmente toda seleção parada ou múltipla. São caminhos que precisam de testes próprios, além dos plugins.

| Ferramenta/ação | Estado persistente | Presença temporária |
| --- | --- | --- |
| Desenho/marcador | Pontos do traço final, cor, espessura, opacidade, posição e exclusão | Traço em andamento, autor e cancelamento |
| Texto/notas | Conteúdo, estilo, dimensões, posição e revisão | Quem está editando; cursor/seleção de texto se suportados |
| Seleção | Somente os resultados de ações sobre os elementos | Conjunto de IDs selecionados e autor; expiração ao sair |
| Formas/conectores | Geometria, estilo, vínculos por ID e exclusão | Prévia de criação/transformação |
| Grupo, mover/redimensionar/rotacionar | Transformações finais coerentes de todos os participantes | Prévia de manipulação e autor |

- Cobrir criação, edição, estilo, duplicação, exclusão e transformações de cada tipo de elemento nos dois sentidos. Seleção do colaborador é uma indicação independente; não deve substituir a seleção nem a ferramenta ativa do usuário local.
- Transmitir traços em andamento em lotes com ID e sequência; persistir uma única entidade final. Receber novamente a conclusão não duplica o desenho. Cancelar, perder conexão ou sair remove a prévia; nunca tratar uma prévia parcial como traço concluído.
- Atualizar texto e notas durante a edição com frequência controlada e confirmação final. Uma edição local não pode suspender indefinidamente toda a atualização remota da mesa. Definir tratamento explícito para edição concorrente do mesmo texto, preservando divergências em vez de sobrescrever silenciosamente.
- Ampliar a presença para seleção múltipla/parada e indicação de edição, com limites e expiração. Não persistir seleções como conteúdo da mesa nem confundi-las com movimentos confirmados.
- Fazer desfazer/refazer atuar sobre as operações do autor e suas revisões. Restaurar um snapshot antigo da mesa inteira pode apagar trabalho remoto e não é uma solução aceitável para colaboração.

- Corrigir a ancoragem do marcador do cursor para coincidir com a coordenada da mesa em qualquer zoom, incluindo mudanças de zoom com mouse parado.
- Conferir posições, dimensões e rotação de elementos antes de atribuir um desencontro ao cursor. Usar a mesma transformação de câmera para elementos e presença.
- Validar entrada sobre controles de plugins, inclusive controles que consomem eventos de mouse. Distinguir posição do ponteiro, seleção e movimento do elemento.
- Para layouts internos diferentes, definir indicação de interação por ID estável de controle e/ou posição relativa à instância, quando suportada pelo renderer. Cursor global continua no mundo da mesa; um evento remoto não simula clique local.
- Evitar que zoom da câmera altere o layout lógico interno de um plugin de forma inconsistente entre participantes. Tratar reflow por dimensão lógica/densidade e explicitar diferenças de renderer.
- Descartar presença vencida e eventos fora de ordem; confirmar posição final após arraste, reconexão e atualização persistente.

Aceite: desenhos, textos, notas, formas e conectores convergem durante uso e após entrada tardia/reconexão; seleções e prévias mostram corretamente o autor e desaparecem ao terminar; desfazer de um participante preserva alterações do outro. Referência do cursor coincide com alvos conhecidos em 25%, 71%, 100% e 200%, com DPI, câmera e tamanhos de janela diferentes. Diferenças de disposição interna não induzem uma falsa interação em outro controle.

## 5. Relatório de falha ao carregar plugins

O loader atual captura exceções, escreve em `Trace` e retorna `null`; a interface exibe uma orientação genérica para reparar. Substituir esse fluxo por resultado estruturado e diagnóstico acionável.

- Distinguir pacote ausente, download/integridade, manifesto inválido, versão incompatível, capability ausente, permissão, dependência/tipo, migração de estado, inicialização e criação/atualização de renderer.
- Produzir código estável e ID do incidente. Registrar plugin/versão, versão do host/SDK, etapa, horários e exceção sanitizada. Não incluir tokens, texto dos documentos, anexos ou caminhos pessoais desnecessários.
- Mostrar na própria instância: motivo legível e ações aplicáveis — instalar, atualizar, reparar, tentar novamente, ver detalhes, copiar/exportar relatório. Não sugerir reparo para uma incompatibilidade que exige atualizar o host.
- Preservar estado, anexos, posição e identidade. Uma falha de carregamento é local e não deve apagar ou marcar como defeituosa a instância nos outros clientes.
- Limitar repetição de logs/tentativas para não gerar tempestade de erros a cada frame. Manter as demais instâncias utilizáveis.
- Criar relatório local automaticamente; exportação/cópia é acionada pelo usuário. Envio a suporte depende de um destino definido e de ação explícita — não presumir um serviço de telemetria existente.

Aceite: provocar cada classe de falha com fixtures; verificar mensagem, relatório útil, ausência de dados sensíveis, preservação dos dados e recuperação depois de reparar/atualizar.

## 6. Testes e condições para publicação

Executar testes de Core, Dev Kit, cliente Supabase, interface, plugins, instalação e atualização. A suíte de nuvem legada não substitui os testes do backend Supabase usado em produção. Confirmar o estado atual da falha antiga mencionada no plano New em vez de tratá-la como resolvida.

Matriz obrigatória:

- Mesas Local, PersonalCloud e Shared; dono, editor, removido e não membro.
- Mesa preenchida antes do convite, entrada durante edição, entrada tardia e reabertura.
- Edições nas duas direções, mesmo campo e campos independentes; exclusão versus edição; comandos assíncronos concorrentes.
- Desenho longo e cancelado, escrita/estilo de texto e nota, seleção parada/múltipla, duplicação, transformação de grupo, conectores e desfazer/refazer intercalado entre autores.
- Perda de resposta após gravação, offline, reconexão, token vencido, 403, 429, timeout de anexo e snapshot paginado.
- Todos os plugins: configurações, anexos aplicáveis, resultados, falha de carregamento, instalação tardia, reparo e compatibilidade com versão antiga.
- Zoom, DPI, câmera, mouse sobre controles, movimentação e retomada da posição final.
- Upgrade de perfis antigos, migração de estado, recuperação de fila e rollback sem perda.

Validar DEV↔DEV, Production de teste↔Production de teste e DEV↔Production em contas/mesas descartáveis. DEV e main usam o mesmo Supabase: isolamento local não transforma o servidor em staging. Não executar testes destrutivos na mesa de trabalho de Pedro/Renan.

Critérios de saída: igualdade dos IDs/revisões/conteúdo/posições/anexos após estabilização; nenhuma pendência inexplicada; nenhum erro de autorização em operação válida; nenhuma perda ou duplicação; execução externa única; relatórios de falha aprovados. Medir latência de edição e cursor com percentis e registrar condições de rede. Usar como alvo inicial p95 de até 1 s para edição pequena e 150 ms para presença em rede estável; anexos são avaliados separadamente por conclusão e integridade. Esses valores são metas a medir, não desempenho já comprovado.

## 7. Deploy de DEV e main

1. **Candidato DEV:** integrar correção, SDK, adaptações de plugins e diagnóstico em `codex/clipdesk-dev`. Escolher versão livre após verificar releases existentes; usar identificação inequívoca de edição e revisão.
2. **Banco, se necessário:** migrations aditivas e retrocompatíveis primeiro, testadas com clientes antigos. Não relaxar RLS para contornar o 403. Se a correção for só de cliente, não criar migration desnecessária.
3. **Pacote DEV:** gerar `ClipDesk-DEV-Setup.exe` e `ClipDesk-DEV-Windows-x64.zip`, publicar candidato/pré-release e feed DEV. Verificar que o atualizador Production ignora esse release. DEV usa seu instalador próprio; hoje seu atualizador automático está desativado.
4. **Validação real:** executar a matriz de dois clientes, validar anexos/14 plugins e recuperar a mesa afetada a partir de operações preservadas, com comparação antes/depois. Não usar restauração integral da nuvem como atalho.
5. **Integração main:** transportar as mudanças aprovadas, preservando funcionalidades específicas de main. Testar o resultado integrado; não presumir que os binários DEV e Production sejam idênticos por compartilharem commits.
6. **Pacotes Production:** atualizar versão do app, instalador, atualizador, notas e landing. Gerar `ClipDesk-Setup.exe` e `ClipDesk-Windows-x64.zip` com ambiente Production; ensaiar instalação/upgrade em pasta isolada via `--test-install`.
7. **Plugins e Dev Kit:** preparar pacotes e hashes imutáveis, versões mínimas de host e catálogos corretos. Publicar host compatível antes de ativar plugins que o exigem; confirmar que clientes antigos não instalam pacotes incompatíveis. Regenerar ZIP do kit e conferir igualdade dos hashes em docs e landing.
8. **Publicação estável:** promover o commit aprovado, publicar release com assets correspondentes, atualizar feed Production e landing. Verificar downloads, checksums, canal de atualização e ausência de links DEV em Production.
9. **Verificação após deploy:** instalar os artefatos publicados em perfis de teste e repetir convite, carga inicial, edição, anexo de plugin e relatório de erro. Registrar versões e evidências de saúde da fila/servidor; definir eventual monitoramento recorrente separadamente.

Rollback: manter instaladores, feeds e pacotes anteriores; congelar distribuição se aparecer regressão. Para dispositivos que já atualizaram, preparar pacote de recuperação com versão superior quando o atualizador não aceitar downgrade. Não reverter schemas destrutivamente nem apagar mídia/estado novos. Se o host anterior não entender o estado, preservar dados e usar modo protegido até a correção, em vez de permitir regravação incompatível.

## Ordem de execução e entregáveis

Sequência: **preservação/diagnóstico → sincronização e ferramentas nativas → contrato comum → adequação do catálogo/relatório de falhas → presença/validação integrada → DEV → main**. O relatório de falhas deve entrar cedo no candidato DEV para apoiar os demais testes.

Entregáveis finais: mudanças revisáveis de host/SDK/plugins, regressões automatizadas, matriz de compatibilidade dos 14 plugins, Dev Kit regenerado, notas de release, instalador e ZIP de cada edição, feeds coerentes, migrations quando necessárias e roteiro de recuperação/rollback.

O trabalho estrutural do New não reduz o caminho crítico desta entrega. A economia vem de implementar a colaboração uma vez em contratos/regras portáveis e adaptar o WPF atual, preservando essa base para a migração futura.
