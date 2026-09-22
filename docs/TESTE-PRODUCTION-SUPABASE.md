# Roteiro de validação do ClipDesk Production com Supabase

Este roteiro valida a versão final antes do deploy. Use duas instâncias isoladas do pacote Production:

- **Janela 1:** `@robsonarruda`
- **Janela 2:** `@priscilaromao`
- **Mesa descartável:** `Teste realtime 19-09`

Registre em cada etapa: horário, resultado, tempo observado e qualquer comportamento inesperado.

## 1. Preparação

- [ ] Confirmar que as duas janelas executam exatamente o mesmo pacote Production.
- [ ] Confirmar que cada janela usa uma pasta de dados isolada.
- [ ] Confirmar que nenhuma janela aponta para a instalação de uso diário.
- [ ] Confirmar que as duas contas estão conectadas ao Supabase.
- [ ] Confirmar que o status da mesa deixa de mostrar `Salvando` e chega a `Salvo`.

## 2. Login e restauração da sessão

1. Entrar na janela 1 com `@robsonarruda`.
2. Entrar na janela 2 com `@priscilaromao`.
3. Fechar e reabrir cada instância.
4. Confirmar que a sessão correta é restaurada em cada janela.
5. Confirmar que uma sessão não substitui a outra.
6. Confirmar que a interface não fecha nem trava após o login.

## 3. Convite para mesa

1. Criar uma mesa nova na janela 2.
2. Convidar `@robsonarruda`.
3. Manter a janela 1 aberta durante o envio.
4. Confirmar que o convite aparece sem reiniciar e sem refazer login.
5. Conferir o visual do convite nos temas claro e escuro.
6. Aceitar o convite na janela 1.
7. Confirmar que a janela 1 abre diretamente a mesa aceita.
8. Confirmar que a mesa aparece como compartilhada nas duas contas.
9. Confirmar que o histórico pessoal não aparece para o colaborador.

## 4. Sincronização em tempo real

Faça pelo menos cinco repetições em cada sentido.

1. Criar uma nota na janela 1.
2. Cronometrar até ela aparecer na janela 2.
3. Editar a nota enquanto o editor continua aberto.
4. Cronometrar cada alteração até aparecer na outra janela.
5. Repetir da janela 2 para a janela 1.
6. Mover e redimensionar a nota e conferir a outra janela.
7. Criar, mover e apagar uma forma.
8. Alterar a calculadora e conferir o resultado remoto.
9. Confirmar que o texto não alterna entre versões antiga e nova.
10. Confirmar que nenhuma edição remota gera uma nova operação local de retorno.

Critérios:

- [ ] Mediana de chegada menor ou igual a 1 segundo.
- [ ] Nenhuma repetição acima de 2 segundos com conexão estável.
- [ ] Nenhuma espera recorrente de 5 a 10 segundos.
- [ ] Nenhuma perda, duplicação, oscilação ou ressurreição de conteúdo antigo.

## 5. Plugins de texto

### Nota

- [ ] Sincroniza durante a digitação.
- [ ] Continua estável após perder e recuperar o foco.
- [ ] Não alterna entre o texto anterior e o atual.

### Tradutor

1. Criar o tradutor.
2. Digitar e alterar o texto sem usar botão de edição.
3. Confirmar atualização e sincronização em tempo real.
4. Trocar idiomas e repetir.

- [ ] Não existe etapa desnecessária de `Editar`.

### Conversor de moeda

1. Criar o conversor.
2. Alterar valor e moedas diretamente.
3. Confirmar atualização e sincronização em tempo real.

- [ ] Não existe etapa desnecessária de `Editar`.

### Checklist

1. Criar itens, editar textos e marcar/desmarcar opções.
2. Confirmar cada alteração na outra janela.

- [ ] O botão de edição continua disponível no checklist.

## 6. Colagem de imagem

1. Copiar uma imagem real para o clipboard.
2. Colar com `Ctrl+V` na mesa compartilhada.
3. Aguardar pelo menos 30 segundos.
4. Copiar outra imagem e colar novamente.
5. Repetir depois de trocar de mesa e voltar.
6. Confirmar que a imagem original continua visível no dispositivo de origem.
7. Confirmar que nenhum caminho local do Windows é enviado à nuvem.

- [ ] A colagem nunca fica indisponível depois de alguns segundos.
- [ ] A segunda instância recebe e exibe a imagem completa, sem depender do arquivo local da origem.
- [ ] O arquivo é protegido pela participação na mesa e não por link público.

## 7. Arquivos e Google Drive

### Arquivo pequeno

1. Adicionar um arquivo com menos de 30 MB.
2. Confirmar upload pelo armazenamento do ClipDesk.
3. Abrir ou baixar na outra janela e comparar o conteúdo.

### Arquivo grande

1. Adicionar um arquivo com mais de 30 MB.
2. Concluir a autorização do Google Drive quando solicitada.
3. Confirmar que o upload termina e a mesa volta ao estado `Salvo`.
4. Abrir ou baixar o arquivo na outra conta.
5. Comparar tamanho e hash com o original.
6. Fechar e reabrir as duas instâncias e repetir o acesso.

- [ ] O arquivo grande não é gravado no banco do aplicativo.
- [ ] O link do Drive funciona para o colaborador autorizado.
- [ ] Falhas de autorização ou rede mostram uma mensagem clara e permitem tentar novamente.

## 8. Conflitos e uso simultâneo

1. Editar a mesma nota quase ao mesmo tempo nas duas janelas.
2. Confirmar que o resultado é previsível e não fica oscilando.
3. Editar objetos diferentes simultaneamente.
4. Confirmar que ambas as alterações são preservadas.
5. Desconectar uma janela, editar e reconectar.
6. Confirmar convergência sem perder as mudanças já confirmadas.

## 9. Estabilidade

1. Manter as duas janelas abertas por pelo menos 30 minutos.
2. Alternar mesas, temas e níveis de zoom.
3. Criar e editar diferentes plugins durante o período.
4. Observar uso de memória e capacidade de resposta.
5. Conferir logs e eventos do Windows ao final.

- [ ] Nenhuma janela fecha inesperadamente.
- [ ] Nenhuma janela deixa de responder.
- [ ] A sincronização continua ativa durante todo o período.

## 10. Compatibilidade Production e DEV

1. Compilar Production e Development a partir do mesmo código corrigido.
2. Confirmar que ambos usam Supabase.
3. Confirmar que nenhum fluxo antigo de rede local foi reintroduzido.
4. Criar uma mesa em um ambiente e validar a compatibilidade do contrato de dados no outro sem misturar pastas de dados.
5. Executar todas as verificações automatizadas antes de empacotar.

## 11. Aprovação final

- [ ] Compilação Production sem erros nem avisos.
- [ ] Verificações do núcleo aprovadas.
- [ ] Verificações de nuvem aprovadas.
- [ ] Verificações visuais aprovadas.
- [ ] Proteções do instalador aprovadas.
- [ ] Backup dos pacotes anteriores salvo em `E:\AI\Codex\Clipdesk_test\Backup`.
- [ ] As duas janelas de revisão usam o pacote Production mais recente.
- [ ] Nenhuma instalação de uso diário foi modificada.
- [ ] Deploy remoto autorizado explicitamente pelo usuário.

## Registro de resultados

| Data e hora | Etapa | Direção | Tempo | Resultado | Observação |
|---|---|---|---:|---|---|
| 2026-09-19 | Convite com aplicativo aberto | Priscila → Robson | imediato | Aprovado | Convite apareceu sem novo login; aceitar abriu a mesa correta. |
| 2026-09-20 | Realtime 1 | Robson → Priscila | 319 ms | Aprovado | Alteração confirmada no banco local da segunda instância. |
| 2026-09-20 | Realtime 2 | Robson → Priscila | 424 ms | Aprovado | Alteração entregue pelo canal Realtime. |
| 2026-09-20 | Realtime 3 | Robson → Priscila | 488 ms | Aprovado | Alteração entregue pelo canal Realtime. |
| 2026-09-20 | Realtime 4 | Robson → Priscila | 548 ms | Aprovado | Alteração entregue pelo canal Realtime. |
| 2026-09-20 | Realtime 5 | Robson → Priscila | 427 ms | Aprovado | Mediana: 427 ms; pior caso: 548 ms. |
| 2026-09-20 | Nota | Robson → Priscila | tempo real | Aprovado | Cinco edições consecutivas sem retorno ao texto anterior. |
| 2026-09-20 | Tradutor | Priscila → Robson | tempo real | Aprovado | Edição direta: `Boa noite` → `Good evening`; sem botão Editar. |
| 2026-09-20 | Conversor | Priscila → Robson | tempo real | Aprovado | Edição direta: `25 BRL` → `USD 4,86`; sem botão Editar. |
| 2026-09-20 | Checklist | Priscila → Robson | tempo real | Aprovado | Segundo item marcado e recebido; botão Editar preservado. |
| 2026-09-20 | Colagem de imagem | Robson → Priscila | após vários minutos | Aprovado | Nova imagem colada; representação recebida; nenhum caminho `C:\` publicado. |
| 2026-09-20 | Arquivo grande no Google Drive | Priscila | 30.000.001 bytes | Aprovado | Upload, acesso por link, download e hash SHA-256 idêntico; arquivo temporário removido ao final. |
| 2026-09-20 | Verificações do núcleo | local | 30/30 | Aprovado | Todas as verificações concluídas. |
| 2026-09-20 | Verificações de nuvem | local | 68/68 | Aprovado | Inclui conflitos, arquivos, isolamento e notificações em tempo real. |
| 2026-09-20 | Verificações visuais | local | 12/12 | Aprovado | Inclui convite nos temas claro/escuro e componentes de nuvem. |
| 2026-09-20 | Segurança do instalador | local | 8/8 | Aprovado | Dados pessoais não foram acessados. |
| 2026-09-20 | Compilação Production | local | 0 avisos/0 erros | Aprovado | Arquitetura Supabase e recuperação automática de sessão. |
| 2026-09-20 | Compilação Development | local | 0 avisos/0 erros | Aprovado | Mesmo contrato Supabase, com dados isolados. |
| 2026-09-20 | Estabilidade | duas instâncias | 30 min | Aprovado | Ambas responsivas; nenhum erro do processo ClipDesk no log do Windows. |
| 2026-09-20 | Pacote ZIP e instalador | Production | hashes SHA-256 | Aprovado | ZIP contém app, runtime e atualizador; DLL do pacote é idêntica à usada na revisão. |
| 2026-09-20 | Realtime do pacote final | Robson → Priscila | 534 ms | Aprovado | Medição feita após abrir as duas instâncias do pacote Production final. |
| 2026-09-20 | Imagem compartilhada pelo Supabase Storage | Priscila → Robson | 2,2 s no primeiro upload | Aprovado | Card `Peixe 1` exibido com a imagem completa; os dois caches receberam 2.327 bytes e SHA-256 `054736B59D64F42EB4D6F491171153CCC9B9153ADB672CCA0624FB6A4B6249C3`. |
| 2026-09-20 | Transporte do cursor em canal privado | Priscila → Robson | medianas 26,0–27,2 ms; máximo 64,8 ms | Aprovado | Medida do canal Realtime, sem incluir captura do mouse, renderização e troca visual entre janelas. O pacote também exibiu `@priscilaromao` dentro da mesa de Robson. |
| 2026-09-20 | Pacote final após imagens e cursor | duas instâncias | sessão restaurada | Aprovado | Ambas abertas em `Production-2026-09-20-final-assets-realtime`, usando Supabase e a mesa `Teste realtime 19-09`. |
| 2026-09-20 | Instalador final | Production | SHA-256 `222EAB658AE2AEA9D685C9C18D7360151E497B9AB06EBBB3459BB482DF4F71FC` | Aprovado | `dist/ClipDesk-Setup.exe`; cópia anterior e pacote final preservados em `E:\AI\Codex\Clipdesk_test\Backup`. |
| 2026-09-20 | ZIP final | Production | SHA-256 `7EA8C252B2C1B99F8D6AD3B5A95B039E80FA32377914F2803970AC153DFD6689` | Aprovado | A DLL da janela revisada é idêntica à DLL do payload do instalador. |
| 2026-09-21 | Persistência do cursor | Priscila → Robson | mais de 10 s sem movimento | Aprovado | O marcador permaneceu visível durante a ociosidade com o heartbeat de presença a cada 3 s. |
| 2026-09-21 | Acompanhar visão em repouso | Priscila → Robson | mais de 10 s | Aprovado | O acompanhamento permaneceu ativo e mostrou `Câmera e zoom sincronizados`. |
| 2026-09-21 | Acompanhar alteração de zoom | Priscila → Robson | menos de 700 ms | Aprovado | Foram confirmadas as mudanças de 46% para 56% e de 56% para 46%; a última captura terminou em 641 ms, incluindo ativação e captura visual da janela. |
| 2026-09-21 | Criação de forma | Priscila → Robson | cerca de 350 ms | Aprovado | A forma apareceu na segunda conta com sombra e destaque de seleção. |
| 2026-09-21 | Movimento de forma | Priscila → Robson | cerca de 320 ms | Aprovado | A nova posição e o marcador do colaborador apareceram na segunda conta. |
| 2026-09-21 | Criação de checklist | Priscila → Robson | cerca de 350 ms | Aprovado | O plugin apareceu na segunda conta com sombra; o botão de edição permaneceu disponível. |
| 2026-09-21 | Aparência dos objetos | local | 7 tipos | Aprovado | Texto, forma, nota, checklist, calculadora, tradutor e conversor possuem sombra, animação de entrada e destaque durante arraste. |
| 2026-09-21 | Compilação Production revisada | local | 0 avisos/0 erros | Aprovado | Pacote temporário de teste em `E:\AI\Codex\Clipdesk_test\Testes\Production-2026-09-21-pointer-follow-visual-v2`. Nenhum novo instalador foi criado. |
| 2026-09-21 | Verificações automatizadas revisadas | local | núcleo 30/30; nuvem 68/68; visual 12/12 | Aprovado | Inclui heartbeat, anexos, Google Drive e tratamento visual dos sete tipos de objeto. |
| 2026-09-21 | Reconexão final das contas | duas instâncias Production | sincronizado | Aprovado | `@robsonarruda` e `@priscilaromao` ficaram conectados à mesa `Teste realtime 19-09`; cursor remoto visível e acompanhamento encerrado para a revisão manual. |

## Estado da revisão atual

A rodada de 21/09/2026 ainda aguarda a revisão visual do usuário. Não gerar ZIP, instalador, release ou deploy enquanto esta etapa estiver aberta.

### Rodada de movimento contínuo — 21/09/2026

Código principal: `E:\AI\Codex\ClipDesk`, branch `main`. Compilação Production para as duas contas:
`E:\AI\Codex\Clipdesk_test\Testes\Production-2026-09-21-continuous-motion`.

- Compilação do código principal: zero avisos e erros.
- Testes automatizados da cópia de recuperação: núcleo 30/30, nuvem 68/68, visual 17 verificações aprovadas.
- A suíte visual inclui movimento contínuo do cursor, recuperação após redesenho, geometria de arraste via presença, prévia sem alterar o modelo local e 20 mudanças de tema com plugins.
- Esses testes não medem a fluidez real entre duas contas. Os testes de nuvem usam o servidor local de testes, não o Supabase de produção.
- As medições isoladas de transporte e as capturas visuais registradas acima não comprovam latência contínua de ponta a ponta.
- Dois crashes às 12:22 tiveram código `0xc0000006`, junto de erros de leitura/gravação e desconexões do disco E. O tema claro não apresentou exceções no teste automatizado; isso não encerra a investigação dos crashes na utilização real.
- A cópia de recuperação foi preservada em `C:\Users\Pedro\AppData\Local\ClipDesk-Recovery\2026-09-21-1235`. As correções foram transferidas seletivamente para a main após reconexão. O Windows ainda indicava necessidade de reparar o sistema de arquivos de E.
- O controle Computer Use não está exposto nesta sessão. Revisão interativa ainda pendente.

#### Revisão nas duas janelas

1. Usar Robson no cliente 1 e Priscila no cliente 2; abrir a mesma mesa compartilhada.
2. Mover o cursor continuamente por 20 segundos e confirmar que o marcador remoto não congela. Repetir na direção inversa.
3. Arrastar uma nota, um card e um plugin em trajetórias curvas; confirmar movimento contínuo e posição final idêntica. Repetir com vários itens selecionados.
4. Ativar “Acompanhar visão do colaborador”; mover a mesa e variar o zoom continuamente. Confirmar movimento suave, alinhamento e encerramento do acompanhamento.
5. Alternar tema claro/escuro com plugins e imagens presentes, durante a colaboração. Confirmar que ambas as janelas continuam respondendo.
6. Após esses testes, repetir colagem de imagem e abertura de arquivo do Drive na outra conta conforme as seções anteriores.
7. Registrar falhas observadas e conferir os eventos de aplicativo e disco antes de aprovar a rodada.

### Atualização de presença contínua — 21/09/2026

- A presença do cursor e do arraste foi configurada com intervalo de 20 ms. Medição contínua posterior mostrou cerca de 32 envios por segundo no Windows; a configuração não comprovava os 50 envios anteriormente anunciados.
- O emissor conserva somente a posição mais nova quando a rede demora, evitando que movimentos antigos apareçam depois do cursor atual.
- A transição de cursor e de prévia de arraste caiu de 45 para 28 ms.
- Compilação Production concluída sem avisos nem erros em `Production-2026-09-21-continuous-motion-v3`.
- Verificação visual concluída: 17 verificações aprovadas, incluindo cursor contínuo, prévia de arraste e 20 alternâncias de tema.
- Medição real final do canal privado Supabase entre as duas sessões: 10 entregas, mediana de 25,7 ms e máximo de 48,8 ms. Essa medição avalia o transporte; a revisão visual nas duas janelas continua necessária para confirmar a percepção de fluidez.

### Revisão da captura e renderização — 21/09/2026, v4

Código na main em `E:\AI\Codex\ClipDesk`; executável Production em
`E:\AI\Codex\Clipdesk_test\Testes\Production-2026-09-21-collaboration-stability-v4`.
Backups desta rodada em `E:\AI\Codex\ClipDesk\Backup\2026-09-21-collaboration-review`.

Correções:

- Captura do mouse na janela raiz, inclusive durante captura do mouse para deslocar a mesa; coleta por quadro também acompanha o zoom animado.
- Envio conserva apenas a posição mais recente. Intervalo mínimo de 12 ms evita o arredondamento de 20 ms para dois ticks do temporizador do Windows.
- Prévia remota usa transformação de desenho, sem animar as coordenadas salvas. Posições intermediárias confirmadas não fazem a prévia retroceder.
- Sincronização atualiza os elementos existentes; preserva cursores, modelos e seleção. Mesas sem alterações não são redesenhadas.
- Mover um plugin não reconstrói sua superfície a cada evento do mouse.
- Acompanhamento preserva a câmera lógica ao retomar movimento após uma pausa. O novo teste contínuo falhou antes desta correção e passou depois.
- Iteração de elementos durante troca de tema usa uma lista estável.

Evidências desta rodada:

- 19 verificações da suíte visual aprovadas, incluindo 60 alvos consecutivos de câmera/zoom, posição provisória versus persistida, atualização incremental e 20 alternâncias de tema.
- Transporte privado Supabase: 150 de 150 mensagens entregues, 63,5 envios/s, mediana de 26,0 ms e máximo de 64,0 ms. É um teste do canal, sem incluir o percurso mouse → tela remota.
- Compilação Production sem avisos ou erros. Nenhum pacote de distribuição ou novo instalador foi gerado nesta rodada.
- O crash ao trocar tema não foi reproduzido. Os eventos nativos anteriores de 12:22 registram falha de acesso ao disco E; os testes não permitem declarar todos os crashes resolvidos.
- Computer Use continua sem ferramenta callable nesta sessão. Fluidez percebida, convite real, colagem/compartilhamento de imagens e acesso real ao Drive continuam exigindo revisão nas duas janelas; resultados históricos acima não são nova aprovação da v4.

Repetir o roteiro “Revisão nas duas janelas” acima e registrar o resultado antes de liberar distribuição.

As duas instâncias v4 foram abertas às 17:43 e confirmadas como responsivas; os arquivos de sessão protegida permaneceram presentes. Elas iniciaram na “Mesa principal”: selecionar a mesma mesa compartilhada em ambas antes de avaliar a colaboração. Os dois perfis anteriores foram copiados para a pasta `test-profiles` dentro do backup desta rodada.
SHA-256 da DLL Production revisada: `FC4EC3AABF9C18E8E776E965F3AFDCABB54725B8B65170A2666AE972A0C91D14`.

### Falha reproduzida na leitura do movimento — v5

O usuário reprovou a fluidez da v4. A investigação passou a verificar o conteúdo recebido pelo aplicativo, além do tempo de transporte.

**Causa reproduzida:** o SDK Supabase Realtime 8.1.1 usa `ObjectToInferredTypesConverter`, que entrega números como `Double`/`Int64`. `PayloadDouble` convertia esses números em texto usando a cultura do Windows e depois tentava lê-los como números internacionais. Em pt-BR, `0.73` virava `0,73` e era rejeitado. Qualquer coordenada decimal ou zoom fracionário fazia `TryPresence` descartar a mensagem inteira. A presença pelo banco, atualizada em intervalos de segundos, podia continuar aparecendo, mascarando a falha do caminho rápido.

- Teste anterior à correção, usando o serializador do SDK instalado: `pt-BR`, `Double`, mensagem rejeitada.
- Correção: leitura direta dos valores numéricos, independente da cultura. Valores JSON inválidos são rejeitados sem lançar exceção.
- Regressão aprovada em pt-BR, en-US e de-DE; inclui coordenadas, zoom e posições de arraste, além de valores não numéricos.
- Teste real no canal privado com o codificador e decodificador do aplicativo: Priscila → Robson, 150/150 interpretadas, mediana 22,7 ms, máximo 75,8 ms; Robson → Priscila, 150/150 interpretadas, mediana 21,4 ms, máximo 78,0 ms. Mensagens de diagnóstico usam evento separado, sem alterar a mesa ou o cursor dos usuários.
- Suíte WPF agora passa mensagens pelo serializador real antes de validar cursor e prévia de arraste. Os testes antigos usavam presença pronta ou mensagens de transporte apenas com inteiros, e não cobriam este defeito.
- Nas instâncias de teste, `motion-diagnostics.json` registra a cada 5 segundos contadores locais de entrada, fila, envio, recebimento, rejeição e aplicação na tela. Sem conteúdo, coordenadas, contas ou tokens; sem envio desses registros a qualquer serviço.

Esta correção mantém Supabase e os canais privados existentes. Antes de trocar o transporte, foi encontrada e reproduzida uma perda de mensagens dentro do próprio aplicativo. A revisão humana da fluidez continua necessária; aprovação do transporte não equivale à aprovação visual.

Pacote para revisão: `E:\AI\Codex\Clipdesk_test\Testes\Production-2026-09-21-presence-protocol-v5`.
Backup anterior: `E:\AI\Codex\ClipDesk\Backup\2026-09-21-presence-protocol`.

1. Abrir Robson no Teste 1 e Priscila no Teste 2; selecionar a mesma mesa compartilhada.
2. Ajustar o zoom para 73% e mover o mouse em círculos por 20 segundos. Repetir no sentido inverso.
3. Arrastar um card e uma nota continuamente, soltar e confirmar a posição final.
4. Repetir em 100% e 46%; o marcador deve continuar atualizando em todos os valores.
5. Se houver pausas, comparar `Received`, `Rejected` e `Applied` dos registros das duas instâncias com `Input`, `Queued` e `Sent` do emissor. Informar qual etapa parou antes de alterar novamente a frequência ou o transporte.

Validação da v5: 20 verificações automatizadas aprovadas; build Production com zero erros e avisos. Ambas as janelas foram abertas às 17:58, responderam à verificação do processo e produziram os registros locais. Iniciaram na Mesa principal; selecionar a mesa compartilhada antes da revisão. SHA-256 da DLL: `4CD312287FFB19F8E2D859819607E74D4CA97C84C2D03F910B511205F11C8949`. Nenhum instalador foi gerado.

### Contorno após soltar o arraste — v6

O usuário confirmou grande melhora do movimento na v5 e relatou um contorno que continuava preso ao cursor após soltar o item. A presença já encerrava `Drags`, mas o renderer interpretava `ItemId` (seleção mantida) como razão para desenhar um contorno junto ao mouse quando não havia arraste. Esse desenho foi removido: a seleção permanece no elemento da mesa, e o cursor contém apenas o marcador e o nome do colaborador. Mudanças de seleção agora atualizam o mesmo cursor, preservando a interpolação.

Regressão WPF: mensagens passam pelo serializador do SDK; arrastar e soltar card e nota, mantendo seleção, não deixa contorno no cursor; desmarcar também atualiza a seleção remota. Repetir visualmente com as duas contas, movendo o mouse para longe do elemento após soltar.

Compilação para revisão: `E:\AI\Codex\Clipdesk_test\Testes\Production-2026-09-21-drag-release-v6`.
Backup: `E:\AI\Codex\ClipDesk\Backup\2026-09-21-drag-release`.

### Correção do comportamento solicitado — v7

A interpretação da v6 estava errada: o usuário quer contorno remoto **somente durante o arraste**, desaparecendo inteiramente ao soltar, mesmo que o item continue selecionado. A v7 associa o contorno exclusivamente às posições em `Drags`; `ItemId` sozinho não produz contorno. A transição de arraste para soltura remove o destaque tanto do card quanto da nota, mantendo o cursor existente.

O teste verifica presença durante o arraste e ausência após soltar com `ItemId` ainda preenchido, inclusive a visibilidade real de `CollaboratorSelection` no card e a cor de destaque na nota.

Revisão: `E:\AI\Codex\Clipdesk_test\Testes\Production-2026-09-21-drag-outline-v7`.
Backup: `E:\AI\Codex\ClipDesk\Backup\2026-09-21-drag-outline-only`.
