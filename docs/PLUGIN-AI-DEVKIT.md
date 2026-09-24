# ClipDesk Plugin AI DevKit — especificação autossuficiente

Use este arquivo junto com `ClipDesk-Plugin-AI-DevKit.zip`. Ele é a especificação completa para criar um plugin ClipDesk v2 sem analisar a codebase do aplicativo.

## Instrução para o modelo de IA

Crie **somente** uma nova pasta de plugin a partir de `PluginPackages/Modules/Starter`. Não altere `PluginSdk`, `PluginSdk.Windows`, scripts, feeds, branches ou o aplicativo. Não pesquise a codebase do ClipDesk: os SDKs incluídos no ZIP são dependências de compilação e esta especificação é a fonte de verdade.

Antes de entregar:

1. substitua `Starter`, `starter`, nomes, descrição, ícone e cor pelo plugin solicitado;
2. implemente regras no projeto `Core` e UI somente no renderizador WPF;
3. compile, teste manualmente os comandos e execute o empacotador;
4. preencha a revisão visual obrigatória descrita abaixo, usando a instância real na mesa;
5. entregue a pasta do plugin, o ZIP empacotado, a revisão visual e um resumo dos comandos, estado, permissões e testes.

Se um requisito não for coberto pelos contratos abaixo, não invente APIs do host. Registre a limitação no resumo.

## Resultado esperado

```text
PluginPackages/Modules/<NomePascal>/
├── Core/
│   ├── ClipDesk.Plugin.<NomePascal>.Core.csproj
│   └── <NomePascal>Module.cs
├── ClipDesk.Plugin.<NomePascal>.csproj
├── <NomePascal>Plugin.cs
├── UX-REVIEW.md
└── manifest.json
```

Use um ID estável em minúsculas, como `clipdesk.pomodoro`, e o mesmo namespace em todos os arquivos, como `ClipDesk.Plugin.Pomodoro`. O projeto Core é `net8.0`; o renderizador Windows é `net8.0-windows` com WPF. Mantenha a mesma versão no manifesto e nos dois projetos.

## Divisão obrigatória

### Core compartilhável

O Core contém estado, validação, cálculos, migrações e comandos. Ele pode usar somente .NET e `ClipDesk.PluginSdk`. É proibido importar WPF, MAUI, Win32, clipboard, navegador, caminhos de usuário ou timers residentes.

Implemente:

```csharp
public interface IClipDeskPluginModule
{
    string Id { get; }
    int StateVersion { get; }
    PluginState CreateDefaultState();
    PluginState NormalizeState(PluginState state);
    ValueTask<PluginCommandResult> ExecuteAsync(
        PluginState state, PluginCommand command,
        IPluginExecutionContext context,
        CancellationToken cancellationToken = default);
    string? GetClipboardText(PluginState state);
}
```

API necessária:

```csharp
new PluginState(IReadOnlyDictionary<string,string>? values = null)
state.Values
state.GetString(key)
state.GetInt32(key, fallback)
state.GetDecimal(key, fallback)
state.With(key, value)       // devolve novo estado; null remove a chave
state.ToDictionary()

new PluginCommand(name, arguments)
command.Argument(key)

new PluginCommandResult(state, status, message, data)
PluginCommandResult.Invalid(state, message)
```

Status válidos: `Success`, `ValidationError`, `PermissionDenied`, `Unavailable` e `Failed`. Erros esperados devem retornar um status e uma mensagem curta, não lançar exceção.

Regras de estado:

- Estado é um dicionário imutável de `string` para `string` e é sincronizado entre dispositivos.
- Grave a versão em `PluginStateKeys.SchemaVersion` (`"$schema"`).
- `NormalizeState` deve aceitar estado vazio, antigo ou incompleto, preservar chaves desconhecidas e ser idempotente.
- Use `CultureInfo.InvariantCulture` para dados persistidos; localização pertence à UI.
- Não persista senhas, tokens, caminhos locais, respostas grandes ou dados meramente visuais.
- Não misture aparência ao estado. A cor escolhida pelo usuário pertence ao host.
- Propague `CancellationToken` e não mantenha views ou contexto de plataforma no módulo.

O executável do plugin não é sincronizado com mesas compartilhadas. O host preserva ID, nome, versão e estado; se o pacote estiver ausente no outro dispositivo, mostra o nome e **Instalar** sem executar nenhum fallback. Depois da instalação, reabre a mesma instância com o estado recebido. Portanto, mantenha `manifest.id` e `manifest.name` estáveis e faça `NormalizeState` aceitar dados produzidos por versões anteriores ou posteriores.

### Renderizador Windows

Implemente `IWindowsPluginRenderer.CreateBody(WindowsPluginViewContext context)` e retorne um `FrameworkElement`. O contexto fornece:

```csharp
context.State                 // estado atual normalizado
context.Module                // módulo Core
context.Execution             // serviços portáveis
context.IsDarkMode
context.IsEditing
context.Width / Height / Scale
context.AccentColor           // cor resolvida por instância
context.FilesDropped += files => { ... }; // entrega de arquivos soltos nesta instância
context.LayoutChanged += () => { ... }; // atualiza layout quando largura, altura ou escala mudam

await context.ExecuteAsync(command, rebuild, cancellationToken, recordUndo)
context.Commit(state, rebuild)
context.NotifyBeforeChange()
context.RequestHostAction(action)
```

Use `ExecuteAsync` para alterar estado. `rebuild: false` preserva foco durante digitação; `recordUndo: false` pode ser usado em mudanças contínuas depois de registrar um ponto de desfazer adequado. Marque controles interativos com `Tag = "plugin-interactive"`.

A UI deve:

- funcionar em tema claro e escuro desde o `minimumSize` declarado;
- redimensionar sem cortar conteúdo e usar rolagem quando necessário;
- tratar `context.AccentColor` como o tema da instância: aplicar em botões primários/ativos, indicadores e resultados relevantes; controles neutros podem manter a cor de superfície;
- garantir contraste para qualquer cor personalizada e não manter pincéis de destaque estáticos entre reconstruções da view;
- não criar seletor de cor dentro do plugin; o host mostra a paleta somente quando o objeto é selecionado pelo cabeçalho para mover/redimensionar;
- não usar `AccentColor` para bordas ou alças de seleção: o host desenha esse outline com a cor de presença do colaborador e transmite o arraste em tempo real;
- respeitar `context.Scale`, teclado, foco e alvos de toque confortáveis;
- adaptar-se a **ambas** as dimensões, não apenas multiplicar fontes: use `Grid` com colunas proporcionais, quebre linhas, reduza margens em largura curta e ponha conteúdo extenso em `ScrollViewer`; nunca dependa de largura/altura fixas para o conteúdo;
- em `minimumSize`, renderizar o modo compacto completo em um **quadrado**: `width` e `height` devem ser iguais, com ações essenciais visíveis, texto legível e rolagem para o restante; o host impede redimensionar abaixo desse limite, portanto teste exatamente nele e em proporções largas, altas e intermediárias;
- tratar `context.Width`, `context.Height` e `context.Scale` como valores atuais da instância. O host pode redimensionar sem reconstruir o corpo: prefira layout WPF fluido e use `SizeChanged` ou `context.LayoutChanged` para alternar modos quando necessário; evite medidas calculadas uma única vez em `CreateBody`;
- não repetir o nome do plugin no corpo: o cabeçalho da instância já exibe `manifest.name` ou título. Use o espaço interno para conteúdo e ações, como no exemplo de Relógio Mundial;
- criar botões com `PluginButtons.Create(context, "Ação", primary: true/false)` do SDK Windows. Ele aplica cor de destaque, contraste, cantos, estados hover/pressed e escala consistente; mantenha rótulos curtos e deixe espaço para quebra ou reorganização dos botões no modo compacto;
- cancelar operações pendentes em `Unloaded`;
- apresentar erros curtos e recuperáveis, sem detalhes técnicos na tela.

### Contrato visual obrigatório

O plugin é um widget da mesa, não uma página ou formulário de desktop. **Não entregue um plugin que apenas reduza a mesma composição ao diminuir de tamanho.** O renderizador precisa trocar a disposição do conteúdo conforme largura **e** altura disponíveis. Trate o tamanho de `minimumSize` como um produto utilizável, e não como um limite técnico para o contorno.

1. **Hierarquia e espaço.** O primeiro olhar deve encontrar o valor/resultado ou a tarefa principal e uma ação principal. No tamanho padrão, entrada, resultado e ação principal devem caber sem rolagem vertical. Elimine cabeçalhos internos iguais ou equivalentes ao título do host, subtítulos decorativos, estados repetidos, caixas vazias e instruções permanentes que o controle já explica. Estado vazio pode ter uma instrução curta. Recursos raros, presets extensos e opções avançadas vão para menu, expansão ou modo de edição; não comprimem a tarefa principal.
2. **Tamanho compacto.** Em `minimumSize` quadrado, mantenha resultado/estado atual e ação principal visíveis e acionáveis sem rolar. Uma barra de rolagem vertical pode expor opções secundárias, histórico ou texto longo. Nunca esconda a ação principal fora da área visível, reduza todo o painel por `Viewbox`/`ScaleTransform`, trunque rótulo essencial, sobreponha controles ou exija rolagem horizontal. Se isso não couber, aumente `minimumSize` no manifesto ou simplifique o modo compacto.
3. **Composição fluida.** Ações e campos devem quebrar ou empilhar quando a largura diminuir; painéis largos devem passar de colunas para uma coluna ou etapas. Em altura curta, preserve primeiro resultado e ação; torne o restante rolável. Use `Grid` com `Auto`/`*`, `WrapPanel`, `TextWrapping`, `MinWidth`/`MaxWidth` apropriados e um `ScrollViewer` para conteúdo secundário. Não fixe medidas que só funcionam no tamanho padrão. Reaja a `SizeChanged` e `context.LayoutChanged` sem perder foco, seleção ou texto digitado.
4. **Tipografia.** Dê maior peso visual ao dado que o usuário veio buscar. Como ponto de partida em WPF, use pelo menos 18 DIP para resultado principal, 15 DIP para entrada/ação e 12 DIP para informação secundária, já considerados os limites de `context.Scale`; não compense falta de espaço diminuindo fontes abaixo desses pisos. No zoom distante, detalhes podem ceder lugar ao resultado, mas o dado principal e o estado da ação precisam permanecer distinguíveis. Verifique contraste nos temas claro/escuro e com cores de destaque claras e escuras.
5. **Cópia do output.** Se o plugin produz texto, número, URL, código ou resumo útil, exiba um **ícone pequeno e persistente de copiar junto ao resultado**, inclusive no modo compacto. Ele copia o output atual, não a entrada, a legenda, um placeholder ou valor anterior. `GetClipboardText` do Core e o ícone devem concordar sobre o que é copiável. Use `context.RequestHostAction(new(PluginHostActionKind.CopyToClipboard, texto))`; desative o ícone sem resultado válido. Adicione `ToolTip`, nome acessível como “Copiar tradução”/“Copiar valor convertido”, foco de teclado e alvo clicável confortável. Não substitua a ação principal por um grande botão “Copiar” quando um ícone ao lado do valor resolver.
6. **Funções com propósito.** Cada controle visível deve servir à tarefa central ou a uma necessidade frequente. Remova modos, botões, métricas e textos que apenas repetem o estado ou aparecem sem utilidade naquele contexto. Preserve recursos necessários em um local secundário em vez de apenas ocultá-los sem acesso. Se o output é um arquivo ou mídia sem representação textual útil, não invente texto para copiar; ofereça a ação adequada ao artefato.

Exemplos: o Tradutor copia apenas a tradução concluída; o Conversor de Moeda copia o valor convertido com moeda; um QR Code copia o conteúdo codificado; o Cronômetro copia o tempo ou resumo atual. Um resultado em carregamento, vazio ou erro não é copiável. O cabeçalho do host já nomeia Tradutor, Relógio Mundial, Cronômetro e os demais plugins.

### Revisão visual que bloqueia a entrega

Crie `UX-REVIEW.md` na pasta do novo plugin e inclua-o na entrega da pasta (o ZIP executável continua contendo apenas o pacote). Registre uma linha por cenário com tamanho da instância, zoom, tamanho da janela, tema, resultado e correção feita. Faça a revisão no aplicativo com dados reais, resultado longo e estado vazio/erro; inspeção de código ou compilação **não** substitui essa etapa.

| Cenário obrigatório | Critério de aprovação |
| --- | --- |
| `minimumSize` quadrado e tamanho padrão, ambos em 100% | Resultado/estado e ação principal aparecem sem corte; foco, clique e rolagem funcionam. |
| Instância estreita/alta e larga/baixa, redimensionada sem reabrir | Controles reorganizam; não há sobreposição, corte, rolagem horizontal ou perda de entrada. |
| Zoom da mesa em 40%, 60% e 100% | Resultado principal e ação primária são reconhecíveis à distância; ao aproximar, texto e controles continuam proporcionais. |
| Janela do aplicativo em cerca de 1024×768 e em tela ampla | Widget conserva o comportamento interno ao entrar/sair da área visível; popup e rolagem continuam utilizáveis. |
| Tema claro/escuro e destaque claro/escuro | Contraste, estados selecionados, hover, foco e ícone de copiar são perceptíveis. |
| Output vazio, válido, longo e falha | O ícone copia somente o valor válido mais recente; fica desativado quando não há valor; texto longo rola/quebra sem esconder a ação principal. |

Reprove e corrija qualquer cenário com botão principal fora da área, texto essencial truncado, fonte ilegível, título duplicado, espaço vazio dominante ou cópia de valor incorreto. Descreva na revisão se algum recurso secundário foi movido para menu/expansão e por quê. Não marque um cenário como aprovado sem vê-lo na instância real.

### Controles globais do SDK Windows

Use os controles do SDK em vez de criar estilos isolados em cada plugin. Todos recebem `context`, acompanham tema, cor de destaque e escala, marcam a área inteira como interativa e mantêm o mesmo comportamento visual entre plugins.

#### Slider

`PluginSliders.Create` cria um slider moderno com trilho preenchido, marcador circular, teclado e clique em qualquer ponto do trilho. O usuário pode clicar ou arrastar sem precisar acertar o marcador.

No Windows, o visual **Minimal** mantém trilho de 4 px, marcador de 14 px, borda de 2 px e área de interação de 34 px na tela mesmo quando a mesa está com zoom reduzido. A cor de destaque vem do contexto do plugin.

```csharp
var volume = PluginSliders.Create(
    context, minimum: 0, maximum: 100,
    value: context.State.GetInt32("volume", 70),
    changed: value =>
    {
        var state = context.State.With("volume",
            ((int)value).ToString(CultureInfo.InvariantCulture));
        context.Commit(state, rebuild: false);
    },
    step: 1);

volume.InteractionStarted += context.NotifyBeforeChange;
```

Use `InteractionStarted` para registrar um único ponto de desfazer antes de uma alteração contínua. `ValueChanged` é emitido durante clique, arraste e uso das setas; `Home` e `End` selecionam os extremos.

#### Toggle modular

`PluginToggles.Create` aceita duas ou mais opções. Cada segmento ocupa toda a região clicável. A aparência pode usar uma letra, uma palavra ou um glifo de ícone por opção.

```csharp
var tamanho = PluginToggles.Create(context,
[
    new("medium", "M", ToolTip: "Médio"),
    new("small", "S", ToolTip: "Pequeno"),
    new("mute", "Silenciar", IconGlyph: "\uE74F",
        IconFontFamily: "Segoe MDL2 Assets")
],
context.State.GetString("size") ?? "medium",
value => context.Commit(context.State.With("size", value)));
```

O clique pode ocorrer em qualquer parte do segmento. Se o usuário clicar no pequeno espaço interno entre segmentos, o controle avança para a próxima opção. Use `Label` para letra ou palavra; quando `IconGlyph` é informado, o ícone substitui o texto visível e `Label` continua servindo como descrição acessível.

#### Dropdown pesquisável

`PluginDropdowns.Create` cria o botão e abre uma lista moderna com busca, opção selecionada, rolagem, teclado e fechamento ao clicar fora. Para abrir a lista a partir de um botão já existente, use `PluginDropdowns.Show`.

```csharp
var idioma = PluginDropdowns.Create(context,
    idiomas.Select(item => new PluginDropdownOption(
        item.Value, item.Label, SearchText: item.Value)),
    context.State.GetString("language") ?? "pt",
    value => context.Commit(context.State.With("language", value)),
    searchPlaceholder: "Pesquisar idioma…");
```

A busca considera `Label`, `Value` e `SearchText`. Use `SearchText` para incluir sinônimos, código, descrição ou termos que não precisam aparecer na linha. O dropdown global já é usado pelos seletores de idioma e moeda incluídos no ClipDesk.

Ações externas permitidas no renderizador:

```csharp
context.RequestHostAction(new(PluginHostActionKind.CopyToClipboard, texto));
context.RequestHostAction(new(PluginHostActionKind.OpenUri, url));
context.RequestHostAction(new(PluginHostActionKind.ShowMessage, mensagem));
```

Não chame clipboard, `Process.Start`, shell ou APIs globais diretamente.

### Receber arquivos arrastados do Windows ou da mesa

Declare `"file-drop"` em `capabilities` e `"file.read"` em `permissions`. No renderizador, registre o recebimento em `CreateBody`:

```csharp
context.FilesDropped += async files =>
{
    foreach (var file in files)
    {
        // file.Path, file.FileName e file.Length descrevem um arquivo local existente.
        // Valide extensão e tamanho antes de ler. Não persista file.Path no estado.
        var bytes = await File.ReadAllBytesAsync(file.Path);
        // Converta o conteúdo em comando/estado portátil, se necessário.
    }
};
```

O host entrega arquivos soltos diretamente sobre a instância, vindos do Explorer, desktop ou de cartões de arquivo da mesa. O cartão retorna à posição anterior após a entrega. Arquivos soltos fora do plugin seguem o fluxo normal de criação de cartão. Somente plugins que registram `FilesDropped` **e** declaram capacidade e permissão recebem arquivos. A entrega contém até 16 arquivos por vez, sem pastas. O caminho é temporário e pode deixar de existir; trate erros de leitura. O template Starter demonstra a importação de `.txt` de até 1 MiB.

Para criar cartões de arquivo ao lado do plugin, declare `"board-files"` em `capabilities` e `"file.write"` em `permissions`. Envie o conteúdo ao host:

```csharp
using System.Text;

var arquivo = PluginBoardFile.FromContent(
    "resultado.txt", Encoding.UTF8.GetBytes("Conteúdo gerado"));
context.RequestHostAction(PluginHostAction.AddFiles(
    new PluginBoardFileRequest([arquivo])));
```

Se o plugin já recebeu legitimamente um arquivo local, use `PluginBoardFile.FromPath(caminhoAbsoluto, nome)` e declare também `"file.read"`. Não leia diretórios nem descubra caminhos por conta própria. O host valida a solicitação, cria o cartão ao lado da instância, registra desfazer e executa persistência/sincronização. Limites: 16 arquivos, 25 MiB por conteúdo e 64 MiB por solicitação. `Content` e `Source` são mutuamente exclusivos.

### Pasta local do plugin

Se o plugin precisar criar arquivos ou subpastas locais, use exclusivamente `Documents/ClipDesk/<pasta-do-plugin>`. Cada plugin escolhe um nome simples e estável para sua pasta, por exemplo `Documents/ClipDesk/clipdesk.pomodoro`. Não crie arquivos em Desktop, Downloads, AppData, na raiz do disco, nem em outra pasta de Documentos.

Use o helper do SDK para montar o caminho padrão e crie a pasta somente quando ela for necessária:

```csharp
var pastaDoPlugin = PluginStoragePaths.GetDefaultDirectory("clipdesk.pomodoro");
Directory.CreateDirectory(pastaDoPlugin);
var caminho = Path.Combine(pastaDoPlugin, "historico.json");
await File.WriteAllTextAsync(caminho, conteudo);
```

O único caso em que o plugin pode gravar fora dessa pasta é quando oferecer uma opção explícita de **Pasta de saída** e o usuário escolher o local. Guarde essa escolha no estado/configuração do plugin, mostre o caminho selecionado e mantenha `Documents/ClipDesk/<pasta-do-plugin>` como padrão. Nunca escolha outro destino automaticamente.

## Rede e permissões

Para rede, declare `"network"` em `permissions`, liste hosts exatos em `networkHosts` e use apenas:

```csharp
if (!context.HasPermission(PluginPermissions.Network))
    return new(state, PluginCommandStatus.PermissionDenied, "Rede não autorizada.");

var json = await context.Network.GetStringAsync(
    new Uri("https://api.example.com/data"), cancellationToken);
```

Somente HTTPS é aceito; subdomínios não são implícitos, redirecionamentos são bloqueados e a resposta é limitada pelo host. Defina comportamento offline. Arquivos podem ser adicionados à mesa somente pela ação tipada descrita acima; tarefas em segundo plano ainda não possuem serviço v2.

## Manifesto v2

Edite o `manifest.json` do template. Preserve:

- `manifestVersion: 2`, `runtime: "portable-v2"` e `pluginApiVersion: 2`;
- `stateVersion` igual a `IClipDeskPluginModule.StateVersion`;
- `module` apontando para a montagem Core e o tipo completo do módulo;
- renderizador Windows com `platform: "windows"` e `runtime: "wpf-v2"`;
- `platforms: ["windows"]` enquanto não existir renderizador MAUI compilado;
- `capabilities: ["board-widget"]` para widgets da mesa;
- defina `minimumSize` para o modo compacto como um quadrado (`width` igual a `height`), com lado mínimo de 120 e nunca maior que `defaultSize`; o padrão de compatibilidade é 190×190 e o empacotador rejeita valores inválidos ou retangulares;
- acrescente `"file-drop"` e `"file.read"` somente se aceitar arrastar arquivos;
- acrescente `"board-files"` somente se o renderizador realmente solicitar arquivos na mesa;
- `defaultContent` coerente com `CreateDefaultState`;
- `installByDefault: false` para plugins opcionais.

Não anuncie Android apenas porque o Core é portável. No Android, o módulo e um renderizador MAUI precisarão ser compilados estaticamente no aplicativo.

## Compilar, validar e empacotar

Na raiz extraída do DevKit, depois de preencher e aprovar todos os cenários de `UX-REVIEW.md`:

```powershell
dotnet build .\PluginPackages\Modules\<NomePascal>\ClipDesk.Plugin.<NomePascal>.csproj
.\PluginPackages\pack-reviewed-plugin.ps1 -PluginDirectory .\PluginPackages\Modules\<NomePascal>
```

O segundo comando rejeita revisão ausente, incompleta ou com cenário reprovado; então recompila em Release, valida as montagens declaradas e cria `PluginPackages/dist/<id>-<versão>.zip`, imprimindo SHA-256 e a entrada de feed. A verificação da ficha não prova visualmente seu conteúdo: inspecione a instância real e registre os problemas com honestidade. Corrija todos os erros antes de entregar.

Teste no mínimo:

- estado vazio, antigo, incompleto e malformado;
- todos os comandos válidos e inválidos;
- cancelamento e falha offline, quando aplicável;
- abrir, editar, fechar e reabrir sem perder estado;
- tema claro/escuro, cor de destaque, tamanhos diferentes, zoom e teclado;
- tamanho mínimo quadrado, proporções largas/altas, e soltura de arquivos da mesa e do Windows quando aplicável;
- todos os cenários de `UX-REVIEW.md`, incluindo cópia do output e janela estreita/ampla;
- nenhuma referência de plataforma dentro de `Core`.

## Publicação controlada

O modelo que cria o plugin **não deve publicar nem editar o feed** salvo ordem explícita. A integração é feita depois no repositório oficial:

1. revisar código e pacote;
2. publicar o ZIP como asset do pré-lançamento `plugins-<versão>`;
3. adicionar a entrada gerada a `PluginPackages/feed.json` na branch `codex/clipdesk-dev`;
4. validar na loja do ClipDesk DEV e, quando disponível, ClipDesk New;
5. manter `main` intacta até aprovação de produção.

Na futura promoção DEV → Main, a loja deve ir funcional: plugins padrão aparecem instalados, opcionais aprovados entram no feed de `main`, assets e SHA-256 são verificados e os testes de loja/entrega precisam passar.

O host é responsável pelo ciclo de vida do pacote. A loja oferece **Adicionar**, **Reparar** e **Desinstalar**, e verifica automaticamente versões maiores no feed. Reparar troca somente os arquivos executáveis por uma cópia limpa; desinstalar não apaga o `PluginState` das instâncias. Portanto, toda versão publicada deve continuar normalizando estados antigos, e o plugin nunca deve implementar limpeza de pacote ou atualização por conta própria.

## Checklist final da entrega

- [ ] ID, namespaces, tipos, montagens e versões coincidem.
- [ ] Core não referencia WPF, MAUI, Win32 nem recursos globais.
- [ ] Estado é pequeno, versionado, normalizado e retrocompatível.
- [ ] Comandos validam argumentos e retornam status apropriado.
- [ ] UI usa o host para efeitos externos e a cor de destaque do contexto.
- [ ] Modo compacto funciona no `minimumSize` quadrado; título não é duplicado; controles usam `PluginButtons`, `PluginSliders`, `PluginToggles` e `PluginDropdowns` quando aplicável.
- [ ] Resultado e ação principal permanecem visíveis no mínimo; layout reorganiza em largura e altura; `UX-REVIEW.md` foi preenchido após inspeção real.
- [ ] Output textual útil tem ícone pequeno de copiar junto ao valor, acessível e desativado quando vazio/erro; o texto copiado corresponde a `GetClipboardText`.
- [ ] Arrastar arquivo só é anunciado quando o plugin trata `FilesDropped`.
- [ ] Arquivos são enviados por `PluginHostAction.AddFiles`, com `board-files` e somente as permissões necessárias.
- [ ] Se houver arquivos locais próprios, a pasta padrão é `Documents/ClipDesk/<pasta-do-plugin>`; outro destino só existe depois de escolha explícita do usuário.
- [ ] Trocar a cor atualiza cabeçalho, ações primárias e indicadores imediatamente, sem reabrir o plugin.
- [ ] O plugin não desenha nem persiste outline de seleção ou cor de colaborador.
- [ ] Manifesto anuncia somente recursos realmente implementados.
- [ ] Build e empacotamento terminam sem erro.
- [ ] Nenhum feed, SDK, aplicativo ou branch foi modificado.
