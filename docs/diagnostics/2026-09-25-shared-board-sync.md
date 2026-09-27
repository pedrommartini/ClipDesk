# Diagnóstico da mesa compartilhada — 25/09/2026

Mesa: **Revisoes Pedro e Renan**, `0d8feba2-9fba-4a28-af23-2ee0b759499d`.
Inspeção do servidor entre aproximadamente 16:21 e 16:27, horário de Brasília, com os dois participantes ativos. Pedido: diagnosticar conexão, conteúdo inicial, posições, cursores e estado interno de plugins. Os textos nos screenshots foram usados somente como evidência visual.

**Conclusão:** a sessão tem presença funcionando, mas o conteúdo persistido está divergente. Há erros recorrentes de gravação de mesas e um defeito reproduzido no cliente que permite a uma operação recusada bloquear o restante da fila. Separadamente, o upscale não compartilha a imagem de entrada nem a prévia de saída. Existe ainda um pequeno erro de ancoragem do marcador do cursor dependente do zoom.

## Evidências observadas

- O cliente de Pedro estava aberto na mesa e mostrava `Salvando`, vários textos, molduras, imagens e plugins. A instalação inspecionada é 0.4.3, revisão `21044ea4ea5f968bf74b1b10f8d7bc77b9287778`.
- O screenshot enviado de Renan mostra 0.4.2. Essa diferença deve ser eliminada no reteste, mas não foi demonstrada como causa dos erros de autorização.
- O banco confirma Pedro como `owner` e Renan como `editor` da mesma mesa. Não é um convite pendente ou duas mesas com nomes iguais.
- Às 16:24, havia registros recentes de presença de ambos, com posições, centro da visão e zoom distintos. Logo, não havia desconexão total de nenhum dos participantes naquele instante. Isso não mede a latência do Broadcast nem prova a fidelidade visual de cada cursor.
- Na consulta das 16:24, a mesa tinha 14 registros de elementos: **7 ativos e 7 excluídos**. Os ativos eram cinco textos, um plugin de upscale e um item de imagem. Essa composição não representa a mesa completa visível no cliente de Pedro.
- Na cópia remota, o texto da barra de espaço estava em `x=145, y=226`; o texto sobre ALT em `x=145, y=397.1711910299996`. Essa organização corresponde ao conteúdo do screenshot de Renan e difere da organização visual apresentada por Pedro.
- O plugin remoto de upscale tinha os campos `model`, `engine`, `$schema`, `denoise`, `sharpness`, `faceEnhance`, `lastMessage`, `scaleFactor`, `outputFormat`, `cloudEndpoint`, `totalImagesUpscaled` e `totalPixelsGenerated`. Não havia referência de imagem de entrada ou de resultado em seu estado.
- O painel indicou aproximadamente 1.852 erros de Postgres na janela da última hora. Na amostra de logs, havia rejeições a cada dois ou três segundos: `new row violates row-level security policy for table "workspaces"`.
- O log de API das 16:26:31 confirma `403 POST /rest/v1/workspaces?on_conflict=id`. Requisições de presença e consulta de convites próximas desse horário retornaram `200`.
- A consulta SQL associada à rejeição era um `INSERT ... ON CONFLICT(id) DO UPDATE ... RETURNING` de `workspaces`. As políticas ativas exigem que o usuário seja dono para criar/atualizar uma mesa; leitura exige participação.

Fontes do servidor: [log Postgres](https://supabase.com/dashboard/project/bnrwsndtxaizhmxsgknu/logs/postgres-logs?log=9eb36507-6ad5-4945-982b-bdc55d3eac02), [log da API](https://supabase.com/dashboard/project/bnrwsndtxaizhmxsgknu/logs/edge-logs?log=00c33b1c-a4be-4f6c-aa9b-a5c5fb3a036e). A retenção do serviço pode limitar sua disponibilidade futura.

## 1. Bloqueio da fila de sincronização — prioridade alta

Em `Services/CloudSyncService.Facade.cs:512`, o cliente envia operações antes de buscar o estado remoto. `PushAsync`, na linha 612, percorre a fila global e trata apenas `SyncConflictException` individualmente. Um HTTP 403 é convertido em `InvalidOperationException` por `SupabaseRestClient.EnsureAsync`; essa exceção interrompe o método, deixa operações pendentes e impede que o ciclo alcance a busca remota da linha 544.

A fila coloca mesas antes de elementos (`Core/LocalDatabase.cs:57`). Portanto, uma mesa recusada pode impedir o envio de textos, posições, imagens e plugins de outras mesas também. O caminho rápido de plugins usa a mesma fila em `SynchronizeBoardObjectsAsync`, de modo que ele não contorna o bloqueio. Eventos remotos diretos ainda podem chegar, explicando uma colaboração apenas parcial.

**Reprodução executada:** o backend da DLL instalada 0.4.3 foi chamado com banco descartável e transporte HTTP simulado. A fila continha uma mesa recusada e um elemento de outra mesa. O transporte retornou 403 para a primeira operação. Houve somente uma requisição, o elemento seguinte não foi enviado e ambas as operações continuaram pendentes. Nenhuma requisição desse teste chegou ao servidor.

O teste confirma o defeito de bloqueio. Os logs disponíveis não expuseram o corpo da requisição recusada ou a identidade do chamador; por isso, **não está confirmado qual ID de mesa está gerando o 403**, nem se a origem é uma criação nova, operação antiga ou tentativa sobre mesa de outro dono. Não se deve alterar permissões do servidor por suposição.

Correção recomendada:

1. Reconciliar propriedade, existência e versão remotas antes de tentar recriar uma mesa de versão local zero.
2. Isolar falhas por mesa/operação, preservando alterações pendentes e respeitando dependências. A falha da mesa A não deve bloquear a mesa B; elementos de uma mesa ainda não criada devem aguardar sua criação.
3. Permitir a busca remota mesmo quando houver erro de envio. Não descartar a versão local nem substituir silenciosamente alterações por uma cópia remota incompleta.
4. Tratar erros de autorização com estado explícito e intervalo de repetição adequado. `Salvando` indefinidamente mascara o problema (`MainWindow.Cloud.cs:378`).
5. Registrar localmente operação, entidade, mesa, versão-base e código de falha, sem tokens ou conteúdo privado, para identificar a operação específica.

Há uma fragilidade adicional: `PrepareAttachmentsAsync` ocorre antes do envio normal de entidades e da leitura remota. Uma falha de upload também pode interromper o ciclo. **Não houve evidência de erro de Storage como causa desta sessão**; a falha observada foi de `workspaces`.

## 2. Estado colaborativo incompleto no upscale — prioridade alta

Fonte examinada: `E:/AI/Antigravity/ClipDesk Plugin DEV/PluginPackages/Modules/ImageUpscaler/UI/ImageUpscalerControl.cs`.

- `_currentItem` é um campo privado do controle, linha 29.
- `LoadImageFile`, linha 749, atribui a imagem a esse campo e atualiza a interface local. Não publica a imagem nem uma referência compartilhada no estado do plugin.
- `RunUpscaleAsync` mantém o resultado em `_currentItem.Result` e atualiza o progresso localmente. O comando compartilhado registra métricas e mensagem, não o arquivo nem a prévia do resultado.
- `SyncSettingsToHost`, linha 938, compartilha parâmetros como escala, modelo, motor, nitidez e ruído.
- `Adicionar à Mesa` cria um item separado; isso não torna a imagem parte do estado interno compartilhado do plugin.

O host replica `BoardObject.Content`, mas só pode transmitir o que o plugin publica. Além disso, `Views/BoardObjectView.Plugins.cs:134` reconstrói a interface quando a assinatura do conteúdo muda. Um estado de imagem mantido exclusivamente no controle pode desaparecer quando uma atualização remota exige essa reconstrução. Esse risco foi identificado por leitura do fluxo; não foi exercitado na sessão dos usuários.

Correção recomendada: oferecer anexos compartilhados associados ao plugin, com referências portáveis, armazenamento autorizado pela mesa e restauração local em cada cliente. O estado deve identificar entrada, resultado, parâmetros e execução. Progresso pode trafegar como evento temporário; a conclusão precisa ser persistida. Somente o participante executor deve disparar a operação, evitando que o recebimento remoto rode o upscale novamente.

O critério de aceite é exatamente o pedido do usuário: ao anexar uma imagem no plugin, o outro participante vê a mesma imagem no mesmo plugin; parâmetros, resultado e interações compartilháveis acompanham as mudanças, e um participante que entra depois recupera esse estado. O cursor deve estar referenciado ao mesmo elemento e à mesma disposição interna. Janelas locais de seleção de arquivos e outros controles externos à mesa não são uma representação colaborativa do plugin.

## 3. Cursores e posições — discrepância principal e erro secundário

O cliente captura coordenadas do mundo da mesa com `Mouse.GetPosition(WorkspaceCanvas)` e transmite coordenadas fracionárias. O zoom e a câmera de cada participante podem ser diferentes sem constituir erro: a comparação correta é entre coordenadas da mesa e IDs dos elementos, não entre pixels dos screenshots.

O teste de protocolo foi executado contra a DLL instalada e o serializador real do SDK em `pt-BR`, `en-US` e `de-DE`. Coordenadas fracionárias de cursor, câmera, zoom e arraste foram preservadas nos três casos. Não se reproduziu a antiga falha de conversão decimal nessas condições.

Há, porém, um erro geométrico em `MainWindow.Cloud.cs:904` e `:914`: o cursor externo é posicionado em `(x-7,y-7)`, enquanto o marcador interno, cujo centro está em `(7,7)`, recebe escala inversa ao zoom. O deslocamento resultante por eixo é `7 × (1 − zoom)` unidades de tela WPF. Em 25%, são 5,25 unidades; em 71%, 2,03. É pequeno demais para explicar a diferença de organização das mesas, mas deve ser corrigido ancorando o centro do marcador no ponto transmitido independentemente do zoom.

A evidência favorece a hipótese levantada pelo usuário: **um cursor correto sobre elementos divergentes parece apontar para o lugar errado**. A fidelidade dos cursores ao vivo ainda precisa ser retestada depois de as duas cópias da mesa convergirem.

## Reteste necessário após a implementação

1. Usar a mesma versão em dois perfis isolados, com mesa contendo textos, imagens, molduras e plugins antes do convite.
2. Aceitar o convite e comparar IDs, tipos, posições, dimensões, estado e referências de anexos; repetir após reabrir o cliente.
3. Forçar uma falha de gravação em outra mesa e verificar que esta continua enviando e recebendo alterações, mantendo o erro e os dados pendentes da mesa afetada.
4. Com zoom e câmera diferentes, apontar para pontos conhecidos do mesmo elemento, incluindo controles dentro do plugin.
5. Anexar imagem ao upscale, mudar parâmetros, executar e recuperar o resultado no outro cliente e em uma entrada tardia.
6. Interromper e restabelecer a conexão, validando recuperação sem duplicação, perda de alterações ou repetição involuntária de processamento.

## Escopo e limites da investigação

Foram consultados o cliente visível, código, políticas, dados e logs do Supabase. O computador de Renan não foi acessado diretamente. As leituras usuais dos arquivos locais mostraram dados anteriores à sessão, e a identidade/tamanho dos arquivos abertos pelo processo divergiu dessas leituras; elas não foram tratadas como uma cópia fiel do estado ativo nem como prova de perda de dados. O erro 401 da sessão antiga lida em disco também não foi atribuído à sessão ativa, pois a presença de ambos foi confirmada no servidor.

Não foram alterados dados da mesa, políticas de acesso, instalação ou processo ativo. Este trabalho entrega diagnóstico e reprodução; não foi aplicada correção em produção. O relatório não afirma que os anexos ou a mesa estejam integralmente salvos na nuvem.
