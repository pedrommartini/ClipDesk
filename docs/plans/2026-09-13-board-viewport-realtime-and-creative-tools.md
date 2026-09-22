# Plano — mesa navegável, colaboração fluida e base para ferramentas criativas

Data: 13/09/2026  
Ambiente: somente **ClipDesk Development**  
Estado: em implementação no ambiente Development. O mockup inicial está em `board-viewport-concept-v1.png`.

## Resultado esperado

O ClipDesk passará a tratar a mesa como um espaço visual único, com coordenadas próprias e limites definidos. Zoom e navegação alteram somente a câmera local do usuário. Cards, cursores, seleções e futuros desenhos permanecem nas mesmas coordenadas da mesa para todos os colaboradores, independentemente da resolução, tamanho da janela ou zoom de cada pessoa.

O mesmo núcleo deverá suportar depois texto livre, desenho, formas, conectores e outros objetos sem uma nova troca de arquitetura.

## Decisões de produto propostas

- A mesa começa com **12.000 × 8.000 unidades lógicas**. Esses valores ficam salvos na mesa e podem ser ampliados posteriormente por migração.
- Zoom proposto: **10% a 300%**, com comandos para 100% e “enquadrar mesa”. O mínimo efetivo pode ficar abaixo de 10% quando necessário para enquadrar toda a mesa em uma janela pequena.
- A roda do mouse mantém a navegação vertical; `Ctrl + roda` aplica zoom no ponto sob o cursor. O controle existente também altera o zoom da câmera.
- Pressionar e arrastar o botão direito move a câmera. Pressionar e soltar sem ultrapassar uma pequena distância continua abrindo o menu de contexto.
- Zoom, deslocamento da câmera e última região visitada são configurações locais por mesa e por dispositivo. Eles não são sincronizados entre colaboradores.
- Largura, altura e conteúdo da mesa são compartilhados, pois fazem parte do documento.
- Nenhum card pode sair completamente dos limites. Uma pequena margem pode permanecer fora para permitir organização visual sem perder o objeto.

## 1. Núcleo de coordenadas e câmera

### Estrutura visual

Substituir a escala aplicada individualmente em cada `ItemCard` por uma transformação única da mesa:

1. `WorkspaceViewport`: área visível, recortada pela janela.
2. `WorldSurface`: mesa com largura e altura lógicas fixas.
3. `CommittedObjectsLayer`: cards e futuros objetos persistidos.
4. `TransientInteractionLayer`: movimentos, desenhos e redimensionamentos ainda em andamento.
5. `PresenceLayer`: cursores, seleções e movimentos dos colaboradores.
6. `ScreenOverlayLayer`: seleção retangular, menus e indicadores que precisam manter tamanho legível na tela.

Criar um `BoardViewportController` como fonte única para:

- matriz de transformação da mesa;
- conversão `ScreenToWorld` e `WorldToScreen`;
- zoom ancorado no cursor;
- deslocamento e limites da câmera;
- enquadramento da mesa e de uma seleção;
- conversão correta de pontos recebidos por drag-and-drop, clipboard e presença remota.

O fundo quadriculado será renderizado no espaço da mesa e acompanhará o zoom. Cabeçalho, pesquisa, colaboradores, status e lixeira continuarão fixos na tela.

### Persistência e migração

Adicionar à mesa `WorldWidth`, `WorldHeight` e `SchemaVersion`. Adicionar um documento local `ViewportState` por mesa contendo `Zoom`, `PanX` e `PanY`.

Mesas existentes serão migradas sem alterar as posições atuais:

- calcular o retângulo ocupado pelos cards atuais;
- posicioná-lo dentro da nova área lógica com margem segura;
- manter tamanhos em unidades de mesa;
- começar com uma câmera que enquadre o conteúdo existente;
- gravar a migração somente depois de salvar com sucesso a cópia local.

## 2. Entrada do mouse e fluidez

Remover operações de disco, serialização, reconstrução visual e rede dos eventos de movimento do mouse. Esses eventos atualizarão somente estado em memória.

- Consolidar movimentos por quadro com `CompositionTarget.Rendering`.
- Usar captura explícita do ponteiro para pan, arraste, redimensionamento e seleção.
- Definir uma máquina de estados de interação: `Idle`, `Selecting`, `Panning`, `Dragging`, `Resizing`, `Drawing` e `EditingText`.
- Fazer hit testing no espaço da mesa e evitar transformações repetidas por card.
- Persistir e sincronizar apenas após intervalos controlados ou no fim da ação.
- Preparar seleção múltipla, alinhamento e transformação de grupos usando o mesmo sistema de coordenadas.

Meta: manter a interação visual próxima de 60 fps em uma mesa comum e impedir que uma chamada de rede bloqueie a thread da interface.

## 3. Sincronização em filas independentes

A sincronização atual mantém uma trava única e aguarda cópia, hash, cadastro e upload de anexos. Ela será dividida em quatro fluxos:

| Prioridade | Fila | Conteúdo |
| --- | --- | --- |
| Imediata | Presença transitória | cursor, seleção, arraste e redimensionamento em andamento |
| Alta | Alterações da mesa | posição final, texto, título, criação, remoção, ordem e configurações |
| Média | Metadados de anexos | nome, tamanho, hash, proprietário e estado de disponibilidade |
| Baixa | Conteúdo de arquivos | banco do app até 30 MB e transferências pelo Drive |

Regras:

- Uma transferência de arquivo nunca segura a fila de alterações da mesa.
- Operações de posição ainda não enviadas são consolidadas por `workspace + objeto`; somente a posição mais recente precisa ser persistida.
- Cada fila tem cancelamento, retentativa com espera progressiva e estado próprio.
- Alterações da mesa continuam sendo salvas localmente antes do envio.
- O canal em tempo real notifica a operação específica; o cliente deixa de baixar e reconstruir toda a mesa a cada aviso.
- A fila de arquivo pode continuar em segundo plano após a mesa já aparecer sincronizada.

## 4. Arraste colaborativo em tempo real

Durante o arraste, publicar mensagens transitórias contendo `workspaceId`, `objectId`, coordenadas lógicas, número de sequência e instante. A frequência inicial será limitada a aproximadamente 20 atualizações por segundo, suficiente para movimento contínuo sem congestionar a rede.

Nos outros computadores:

- o objeto aparece se movendo na camada transitória;
- pequenas lacunas são interpoladas visualmente;
- mensagens atrasadas são descartadas pelo número de sequência;
- ao soltar, o dono da ação envia uma alteração persistente com a posição final;
- se a conexão cair, a visualização transitória expira e o objeto retorna à última posição confirmada.

O mesmo protocolo será reutilizado para redimensionamento e, no futuro, para o traço de desenho antes de sua confirmação.

## 5. Modelo extensível de objetos da mesa

Criar um contrato comum `BoardObject` com:

- `Id`, `WorkspaceId`, `Kind` e `SchemaVersion`;
- `X`, `Y`, `Width`, `Height`, `Rotation` e `ZIndex`;
- `Locked`, `CreatedBy`, `CreatedAt` e `UpdatedAt`;
- estilo serializável;
- conteúdo específico por tipo;
- referências de anexos separadas do objeto visual.

Os cards atuais serão representados como `Kind = Card`, mantendo o conteúdo de `ClipboardItem` durante a migração. A primeira etapa pode usar um adaptador para evitar converter todos os dados de uma vez.

Tipos reservados para evolução:

- `Card`: arquivos, pastas, links, imagens e conteúdo do clipboard;
- `Text`: texto livre com fonte, alinhamento, cor e largura;
- `Shape`: retângulo, elipse, linha, seta e forma futura;
- `Stroke`: pontos vetoriais, pressão opcional, cor e espessura;
- `Connector`: referências aos IDs dos objetos de origem e destino;
- `Group`: agrupamento sem duplicar conteúdo;
- `StickyNote`: possível especialização futura.

Criar também uma interface de ferramenta (`IBoardTool`) para `Select`, `Pan`, `Pen`, `Text`, `Shape` e `Connector`. Nesta entrega somente seleção e pan precisam estar ativos; os demais contratos e tipos ficam prontos, sem expor botões incompletos.

Desfazer/refazer deverá migrar gradualmente de snapshots da mesa inteira para comandos por objeto. Isso é necessário para desenhos grandes, colaboração e conflitos mais precisos.

## 6. Correções específicas

### Arquivos duplicados

O Windows pode entregar o mesmo arquivo por `FileDrop`, `FileNameW`, texto e também pelo nome curto 8.3, como `ClipDesk-DEV-Setup3.exe` e `CLIPDE~2.EXE`. A deduplicação atual compara somente as strings.

Criar `DroppedFileIdentityResolver` para:

- resolver caminho completo e links do sistema;
- obter a identidade real do arquivo por volume e identificador do arquivo quando disponível;
- usar caminho final canônico como fallback;
- deduplicar todos os formatos antes de criar cards;
- registrar um único ID de importação durante o evento de drop para impedir processamento duplo.

Também impedir que materialização da nuvem e eco local criem dois objetos com IDs diferentes para a mesma ação.

### Pastas vazias

- Tornar toda a superfície do card de pasta uma área válida de arraste.
- Separar o drag visual da existência de filhos/anexos.
- Persistir uma entrada explícita de pasta, mesmo quando não há arquivos internos.
- Preservar pastas vazias na sincronização por meio de um manifesto de diretórios, em vez de inferi-las apenas pela lista de arquivos.

### Preview de PDF

Hoje cada reconstrução cria um novo `ItemCard` e uma nova instância do renderizador de PDF. Implementar:

- `PdfPreviewCache` compartilhado;
- chave por hash do anexo ou `caminho + tamanho + última modificação + largura solicitada`;
- limite de memória e descarte dos itens menos usados;
- cache local em disco para documentos sincronizados;
- cancelamento da renderização de cards que saíram da tela;
- atualização visual incremental por ID, sem recriar cards que não mudaram.

### Clipboard sempre ativo

Remover `_captureDevelopmentClipboard` e o botão de ativação no painel. O listener inicia junto com a janela em Development e Production. A escolha de sincronizar o histórico continua independente: captura local sempre ativa; envio à nuvem conforme a preferência de privacidade já existente.

### Cores dos colaboradores

Definir uma paleta estável de cores vivas com contraste adequado nos temas claro e escuro. O servidor atribui um `ColorSlot` estável ao membro da mesa, garantindo que todos vejam a mesma cor para a mesma pessoa. A cor aparece no avatar, cursor, nome, seleção e contorno do objeto em movimento.

### Pendências por usuário

- O status principal mostra somente alterações e arquivos pendentes do usuário conectado.
- Anexos pertencentes a outro colaborador não entram no número pessoal.
- O avatar pode mostrar no tooltip o estado conhecido daquele colaborador, sem somar tudo em um número ambíguo.
- “Sincronizado” significa que as alterações prioritárias do usuário chegaram ao servidor; arquivos em segundo plano mantêm um indicador separado.

### Barra superior

Integrar a pesquisa à grade do cabeçalho para que permaneça alinhada e não use posicionamento independente sobre a mesa. O layout terá regiões flexíveis para nome/estado, pesquisa, colaboradores, zoom e lixeira, com comportamento compacto em janelas menores.

O botão textual **Convidar** será substituído por um botão circular `+` imediatamente após os avatares. Tooltip e acessibilidade continuam informando “Convidar colaborador”.

## 7. Renderização e desempenho para objetos futuros

- Manter um dicionário visual por `objectId` e aplicar alterações somente ao objeto afetado.
- Virtualizar objetos muito distantes da câmera, preservando uma margem para pan suave.
- Não virtualizar presença e objetos sendo manipulados.
- Agrupar traços longos em geometria vetorial eficiente e simplificar pontos sem alterar perceptivelmente o desenho.
- Separar estilo de conteúdo para que alterações de cor não regravem anexos ou texto não relacionado.
- Usar Z-order determinístico e sincronizado.
- Preparar exportação futura usando coordenadas da mesa, sem depender da tela do usuário.

## 8. Etapas de implementação

### Etapa 0 — validação visual

Gerar com ImageGen um mockup da mesa final mostrando limites, zoom global, barra superior alinhada, botão `+`, cores vivas e cursores/seleções. Ajustar conforme aprovação antes de editar XAML.

Concluída: mockup inicial salvo como `board-viewport-concept-v1.png`.

### Etapa 1 — fundação e migração

Implementar `BoardObject`, dimensões da mesa, estado local de câmera, transformações e migração segura das mesas existentes. Adicionar testes de conversão de coordenadas e compatibilidade dos dados antigos.

Em andamento: dimensões da mesa, `BoardViewport`, estado local de câmera, normalização de mesas antigas e testes de zoom/limites foram adicionados. O adaptador de `BoardObject` entra antes das ferramentas criativas, após a migração da visualização atual.

### Etapa 2 — câmera e interação

Aplicar a transformação única, pan com botão direito, zoom no cursor, limites, enquadramento, seleção, arraste, redimensionamento, drop externo e lixeira usando coordenadas lógicas.

### Etapa 3 — renderização incremental

Parar de reconstruir todos os cards, introduzir atualização por ID, virtualização inicial e cache compartilhado de PDF.

### Etapa 4 — sincronização prioritária

Separar filas, remover uploads do caminho crítico, consolidar operações de movimento e enviar mudanças específicas em tempo real.

### Etapa 5 — colaboração transitória

Adicionar arraste e redimensionamento ao vivo, interpolação remota, expiração e posição final persistida. Adaptar cursores e seleções para coordenadas da mesa.

### Etapa 6 — correções de arquivos e pastas

Resolver aliases de arquivos, eliminar drops duplicados, corrigir pastas vazias e preservar a hierarquia completa na nuvem.

### Etapa 7 — acabamento da interface

Clipboard sempre ativo, cores vivas, pendências pessoais, pesquisa no cabeçalho, botão `+`, tooltips e estados compactos.

### Etapa 8 — homologação em dois computadores

Testar com contas, resoluções, zooms e redes diferentes; testar também upload simultâneo, perda de conexão e retorno. Gerar novo instalador DEV somente após todas as verificações locais.

## 9. Critérios de aceitação

- Dois usuários com zoom e câmera diferentes veem o mesmo objeto nas mesmas coordenadas lógicas.
- Zoom não altera individualmente tamanho, posição relativa ou ponto de ancoragem dos cards.
- Pan com botão direito é suave e clique direito curto ainda abre o menu.
- Um upload grande não atrasa movimento, texto, convite ou criação de card.
- O colaborador vê um arraste em andamento e ambos convergem para a posição final.
- Alterações prioritárias aparecem no outro computador em até 500 ms na condição normal de teste; o movimento transitório deve parecer contínuo.
- O mesmo arquivo entregue por caminho longo, curto ou formatos diferentes cria apenas um card.
- Pastas vazias podem ser movidas e continuam existindo após reinício e sincronização.
- Um preview de PDF não é renderizado novamente quando outro objeto muda.
- O clipboard é capturado desde a inicialização, inclusive no DEV.
- O número de pendências de um usuário não inclui arquivos pertencentes a outro.
- Pesquisa e controles permanecem alinhados nas larguras verificadas de 900, 1280 e 1920 px.
- Mesas locais continuam locais; mesas pessoais e compartilhadas preservam os modos atuais.
- A instalação diária permanece intocada durante todo o desenvolvimento.

## 10. Validação técnica

- Testes unitários da matriz, zoom ancorado, limites e migração.
- Testes de identidade de arquivo com caminho longo, caminho 8.3, links e múltiplos formatos de drop.
- Testes de prioridade garantindo que uma transferência bloqueada não impeça uma operação de mesa.
- Testes SignalR de sequência, expiração e posição final do arraste.
- Testes de cache de PDF por chave e invalidação após mudança real do arquivo.
- Testes de projeção dos novos tipos de objeto e compatibilidade com cards existentes.
- Teste visual automatizado em diferentes tamanhos e escalas do Windows.
- Sessão manual com dois computadores em redes distintas, incluindo zoom diferente, pan simultâneo, arraste, arquivo pequeno, arquivo acima de 30 MB e reconexão.

## 11. Limites desta entrega

Esta fase prepara contratos, camadas e sincronização para ferramentas criativas, mas não entrega ainda a interface completa de desenho, texto, formas e conectores. Esses recursos entrarão depois como implementações de `IBoardTool` e novos `BoardObject`, sem modificar novamente a câmera, o armazenamento básico ou o protocolo de presença.
