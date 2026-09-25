# Guia de desenvolvimento de plugins v2

Este guia mostra como criar, testar e distribuir um plugin no padrão compartilhável do ClipDesk. Consulte [Arquitetura de plugins](PLUGIN-ARCHITECTURE.md) para decisões de segurança, compatibilidade e Android.

Para delegar a criação de um plugin a outro modelo de IA sem fornecer a codebase inteira, envie apenas [ClipDesk Plugin AI DevKit](PLUGIN-AI-DEVKIT.md) e `ClipDesk-Plugin-AI-DevKit.zip`. O kit contém uma especificação compacta, um template compilável e cópias dos SDKs usadas somente como dependências.

O contrato visual do DevKit exige revisão da instância real em tamanhos, proporções, zoom e janela diferentes. A [auditoria da Mesa 3](PLUGIN-UX-AUDIT-2026-09-24.md) registra os problemas que motivaram essa exigência; `UX-REVIEW.md` acompanha o template para cada novo plugin documentar sua aprovação antes da entrega.

## Estrutura recomendada

```text
PluginPackages/Modules/Counter/
├── Core/
│   ├── ClipDesk.Plugin.Counter.Core.csproj   # net8.0, sem UI
│   └── CounterModule.cs                      # estado, comandos e regras
├── ClipDesk.Plugin.Counter.csproj            # net8.0-windows + WPF
├── CounterPlugin.cs                          # somente renderização Windows
└── manifest.json
```

O projeto Core referencia apenas `PluginSdk/ClipDesk.PluginSdk.csproj`. O projeto Windows referencia `PluginSdk.Windows`, o Core e, se desejado, vincula `Modules/Common/WidgetUi.cs`. Exclua `Core/**/*.cs` do projeto pai para não compilar os mesmos tipos duas vezes.

```xml
<ItemGroup>
  <Compile Remove="Core\**\*.cs" />
  <ProjectReference Include="..\..\..\PluginSdk.Windows\ClipDesk.PluginSdk.Windows.csproj" />
  <ProjectReference Include="Core\ClipDesk.Plugin.Counter.Core.csproj" />
  <Compile Include="..\Common\WidgetUi.cs" Link="WidgetUi.cs" />
</ItemGroup>
```

## 1. Escreva o módulo portável

Implemente `IClipDeskPluginModule`. O módulo não pode importar `System.Windows`, MAUI, clipboard, caminhos do usuário ou qualquer API específica do sistema.

```csharp
using ClipDesk.PluginSdk;

namespace ClipDesk.Plugin.Counter;

public sealed class CounterModule : IClipDeskPluginModule
{
    public string Id => "clipdesk.counter";
    public int StateVersion => 1;

    public PluginState CreateDefaultState() => new(new Dictionary<string, string>
    {
        [PluginStateKeys.SchemaVersion] = "1",
        ["count"] = "0"
    });

    public PluginState NormalizeState(PluginState state) =>
        state.With(PluginStateKeys.SchemaVersion, "1")
             .With("count", state.GetInt32("count").ToString());

    public ValueTask<PluginCommandResult> ExecuteAsync(
        PluginState state, PluginCommand command, IPluginExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        state = NormalizeState(state);
        if (command.Name != "increment")
            return ValueTask.FromResult(PluginCommandResult.Invalid(state, "Comando desconhecido."));
        return ValueTask.FromResult(new PluginCommandResult(
            state.With("count", (state.GetInt32("count") + 1).ToString())));
    }

    public string? GetClipboardText(PluginState state) => state.GetString("count");
}
```

Regras importantes:

- `NormalizeState` deve ser idempotente e tolerar estado antigo/incompleto.
- Não mutile `state.Values`; produza um novo estado com `With` ou `ToDictionary`.
- Use cultura invariável para números persistidos. Localização pertence ao renderizador.
- Valide argumentos e retorne `ValidationError`, `PermissionDenied` ou `Unavailable` em vez de lançar para erros esperados.
- Propague `CancellationToken`. Não mantenha timers, views ou contexto de plataforma no módulo.
- Não armazene tokens, segredos, caminhos locais ou respostas grandes no estado sincronizado.

Em mesas compartilhadas, o pacote executável não é sincronizado. O host grava uma cópia do `manifest.name` junto da identidade do objeto e, em dispositivos sem o plugin, exibe o nome e **Instalar** sem executar o renderizador nem interpretar o estado. Depois da instalação, a mesma instância é aberta com o estado sincronizado existente. Por isso, mantenha `id` e `name` estáveis, publique o pacote no catálogo correto e trate todo estado recebido como potencialmente criado por outra versão do plugin.

## 2. Escreva o renderizador WPF

Implemente `IWindowsPluginRenderer`. A view envia comandos e apresenta o estado retornado.

```csharp
using System.Windows;
using System.Windows.Controls;
using ClipDesk.PluginSdk;
using ClipDesk.PluginSdk.Windows;

namespace ClipDesk.Plugin.Counter;

public sealed class CounterPlugin : IWindowsPluginRenderer
{
    public FrameworkElement CreateBody(WindowsPluginViewContext context)
    {
        var panel = new StackPanel { Margin = new Thickness(12) };
        var label = new TextBlock { Text = $"Cliques: {context.State.GetInt32("count")}" };
        var button = new Button { Content = "Somar", Tag = "plugin-interactive" };
        button.Click += async (_, e) =>
        {
            e.Handled = true;
            var result = await context.ExecuteAsync(new PluginCommand("increment"));
            label.Text = $"Cliques: {result.State.GetInt32("count")}";
        };
        panel.Children.Add(label); panel.Children.Add(button);
        return panel;
    }
}
```

`ExecuteAsync` registra o ponto de desfazer, executa o módulo, normaliza e persiste o novo estado. Passe `rebuild: false` durante digitação para preservar foco. Cancele chamadas pendentes em `Unloaded`. Use `context.RequestHostAction` para clipboard, URI, mensagens e arquivos na mesa; não chame APIs globais diretamente.

O host fornece tema, modo de edição, dimensões e escala. Use layouts fluidos, rolagem e alvos de toque confortáveis. `WidgetUi` oferece controles coerentes com tema claro/escuro. Teste em aproximadamente 300×240, em tamanhos grandes e com zoom.

### Cor de destaque por instância

`manifest.accentColor` define a cor inicial. Depois de adicionar o plugin à mesa, o usuário o seleciona pelo cabeçalho para entrar no modo de movimentação; a paleta **Destaque** aparece automaticamente junto às alças de seleção. Ela não pertence ao menu de contexto, não aparece com o botão direito e é fechada quando o usuário interage com os controles internos do plugin. A escolha é persistida em `BoardObject.Style["accent"]`, separada do estado funcional e sincronizada com a mesa.

O renderizador recebe o valor resolvido em `WindowsPluginViewContext.AccentColor`. Essa cor é o tema visual da instância: o host usa o valor no ícone do cabeçalho, enquanto o renderizador deve aplicá-lo a botões primários/ativos, indicadores e resultados relevantes. Elementos neutros, como teclas numéricas ou texto comum, podem continuar usando a paleta de superfície. Garanta contraste de texto para qualquer cor personalizada e confirme que uma troca atualiza a interface imediatamente, sem reabrir o plugin.

A borda e as alças de seleção **não** usam `AccentColor`. Elas representam presença colaborativa e usam a cor estável do usuário que seleciona/movimenta o objeto. Durante um arraste em mesa compartilhada, posição e cor do colaborador são acompanhadas pelos outros clientes através do canal efêmero de presença; o outline remoto desaparece ao soltar ou expirar a presença. Essa informação não pertence a `BoardObject.Style`, `PluginState` nem ao manifesto, e o plugin não deve tentar desenhá-la.

Não crie um seletor de cor próprio no renderizador: seleção, paleta, persistência e invalidação visual são responsabilidades do host. Não grave uma cópia da cor em `PluginState`: isso quebraria o reset para o padrão do manifesto e misturaria apresentação com regras compartilhadas. Um futuro renderizador MAUI receberá o mesmo valor pelo adaptador de apresentação da plataforma.

## 3. Declare o manifesto

```json
{
  "manifestVersion": 2,
  "id": "clipdesk.counter",
  "name": "Contador",
  "description": "Conte ações rápidas diretamente na mesa.",
  "version": "1.0.0",
  "publisher": "ClipDesk",
  "iconGlyph": "\ue145",
  "accentColor": "#A78BFA",
  "runtime": "portable-v2",
  "pluginApiVersion": 2,
  "stateVersion": 1,
  "module": {
    "assembly": "ClipDesk.Plugin.Counter.Core.dll",
    "type": "ClipDesk.Plugin.Counter.CounterModule"
  },
  "renderers": [{
    "platform": "windows",
    "runtime": "wpf-v2",
    "assembly": "ClipDesk.Plugin.Counter.dll",
    "type": "ClipDesk.Plugin.Counter.CounterPlugin"
  }],
  "minimumHostVersion": "0.3.3",
  "installByDefault": false,
  "sortOrder": 50,
  "defaultSize": { "width": 300, "height": 240 },
  "defaultContent": { "$schema": "1", "count": "0" },
  "platforms": ["windows"],
  "permissions": [],
  "capabilities": ["board-widget"],
  "networkHosts": []
}
```

Mantenha o `id` para sempre e aumente `version` em cada pacote. Aumente `stateVersion` apenas ao evoluir o formato persistido e implemente a migração antes. `minimumHostVersion` descreve a primeira versão capaz de executar o pacote.

### Rede

Adicione `"network"` a `permissions` e liste domínios exatos em `networkHosts`. Use somente `context.Network` no Core:

```csharp
if (!context.HasPermission(PluginPermissions.Network))
    return new(state, PluginCommandStatus.PermissionDenied, "Rede não autorizada.");

var json = await context.Network.GetStringAsync(
    new Uri("https://api.example.com/data"), cancellationToken);
```

Subdomínios não são implícitos. Redirecionamentos são bloqueados. Trate modo offline e mostre uma mensagem curta; detalhes técnicos podem ir em `Data["detail"]`, nunca na UI principal.

### Adicionar arquivos à mesa

Declare a capacidade `board-files`. Use `file.write` para conteúdo gerado pelo plugin e `file.read` somente quando o plugin já possui um caminho local legítimo:

```json
"permissions": ["file.write"],
"capabilities": ["board-widget", "board-files"]
```

O renderizador solicita a ação; o host valida, materializa o arquivo, cria um cartão normal ao lado da própria instância e cuida de desfazer, persistência e sincronização:

```csharp
using System.Text;

var arquivo = PluginBoardFile.FromContent(
    "relatorio.csv",
    Encoding.UTF8.GetBytes("nome,valor\nClipDesk,1"));

context.RequestHostAction(PluginHostAction.AddFiles(
    new PluginBoardFileRequest([arquivo])));
```

Para um arquivo local já existente:

```csharp
var arquivo = PluginBoardFile.FromPath(caminhoAbsoluto, "Resultado.pdf");
context.RequestHostAction(PluginHostAction.AddFiles(
    new PluginBoardFileRequest([arquivo])));
```

Nesse segundo caso, declare `file.read`. Não envie caminhos relativos, pastas ou um caminho arbitrário obtido sem ação clara do usuário. Cada solicitação aceita até 16 arquivos; conteúdo gerado é limitado a 25 MiB por arquivo e 64 MiB no total. O nome deve ser apenas um nome de arquivo, sem diretórios. Uma solicitação inválida é rejeitada integralmente e não altera a mesa.

`PluginBoardFile` aceita exatamente uma origem: `Content`, armazenado pelo host em sua área gerenciada, ou `Source`, que no Windows deve ser um caminho absoluto para um arquivo existente. O contrato permanece portável: um futuro host MAUI materializa `Content` da mesma maneira e adapta identificadores de arquivo específicos da plataforma.

## 4. Teste

Da raiz do repositório:

```powershell
dotnet build .\PluginPackages\Modules\Counter\ClipDesk.Plugin.Counter.csproj
dotnet build .\ClipDesk.csproj
dotnet run --project .\Tests\Visual\ClipDesk.Visual.Checks.csproj -- --plugin-v2
dotnet run --project .\Tests\Visual\ClipDesk.Visual.Checks.csproj -- --plugin-delivery
dotnet run --project .\Tests\Visual\ClipDesk.Visual.Checks.csproj -- --plugin-store
```

Inclua testes do Core com rede falsa. Cubra estado vazio, migração, comandos inválidos, cancelamento, rede indisponível e conteúdo malformado. Manualmente verifique tema claro/escuro, redimensionamento, zoom, teclado/foco, fechar/reabrir e sincronização entre clientes com versões diferentes.

Checklist de Android futuro:

- nenhuma referência de plataforma no Core;
- estado pequeno e serializável;
- comandos independentes de gestos WPF;
- sem caminho de arquivo Windows;
- sem timer ou tarefa residente no módulo;
- comportamento útil offline ou erro recuperável.

## 5. Empacote e publique

```powershell
.\PluginPackages\pack-plugin.ps1 -PluginDirectory .\PluginPackages\Modules\Counter
.\PluginPackages\create-feed-draft.ps1
```

O empacotador publica o projeto Windows, confirma as montagens Core e WPF, exclui SDKs do host e cria `PluginPackages/dist/<id>-<versão>.zip`. Ele também imprime SHA-256 e uma entrada de feed. O segundo script reabre todos os ZIPs, valida entradas e gera `feed.draft.json`.

Publique o ZIP como asset de pré-lançamento `plugins-<versão>` no repositório oficial. Nunca substitua um ZIP sob a mesma versão; publique uma versão maior.

### Fazer o plugin aparecer em todos os ClipDesk

As edições DEV, principal e New usam o catálogo oficial da `main`:

| Cliente | Feed padrão | Finalidade |
| --- | --- | --- |
| ClipDesk principal | `main/PluginPackages/feed.json` | Catálogo oficial |
| ClipDesk DEV | `main/PluginPackages/feed.json` | Mesmo catálogo oficial |
| ClipDesk New | `main/PluginPackages/feed.json` | Mesmo catálogo oficial, quando a loja estiver disponível |

Fluxo de publicação:

1. Aumente a versão no projeto e no manifesto, execute os testes e gere o ZIP.
2. Publique o ZIP como asset do pré-lançamento `plugins-<versão>`. O binário pode ser compartilhado pelos clientes Windows DEV e New enquanto ambos usarem o renderizador WPF. Quando o renderizador MAUI existir, o pacote deverá incluir também sua entrada.
3. Copie a entrada produzida pelo empacotador para `PluginPackages/feed.json` na branch `main`, preservando as outras versões desejadas.
4. Faça commit e push da atualização do feed na `main`. Confirme que o feed abre em `https://raw.githubusercontent.com/pedrommartini/ClipDesk/main/PluginPackages/feed.json` e que o SHA-256 corresponde ao asset.
5. Reinicie o cliente, abra **Mais plug-ins** ou aguarde a verificação periódica. Um plugin novo aparece para instalação; uma versão maior de plugin instalado é atualizada automaticamente. O cliente consulta o feed ao iniciar, ao abrir a loja e a cada 30 minutos, respeitando um intervalo mínimo de 15 minutos e sem executar verificações sobrepostas.
6. Valide o pacote antes da publicação com um feed local de teste. Depois confirme a instalação no ClipDesk DEV e no principal. O deploy de uma nova versão do aplicativo não é necessário para atualizar o catálogo.

Os quatro plugins padrão são incluídos no build e aparecem como instalados mesmo com o feed vazio. O feed é necessário para plugins opcionais e atualizações independentes.

### Publicação de plugins de colaboradores no catálogo oficial

O ClipDesk principal lê `main/PluginPackages/feed.json` sem uma lista fixa de IDs no aplicativo. Para que um plugin criado por um colaborador apareça para todos, publique seu ZIP como asset imutável de uma Release, acrescente a entrada completa ao feed oficial com a URL e o SHA-256 do arquivo, e envie o feed para `main`. Guarde o pacote aprovado em `PluginPackages/Collaborators` quando o projeto fonte não estiver no repositório. O plugin inicial do Dev Kit é um modelo e não deve ser anunciado como plugin pronto.

Antes do deploy, confirme que o manifesto dentro do ZIP declara o mesmo ID e versão do feed, não inclui DLLs do SDK do host, e carrega no ClipDesk atual. Para plugins que usam controles Windows adicionados após uma versão anterior do aplicativo, aumente `minimumHostVersion` no feed para impedir que uma edição incompatível ofereça a instalação. Depois de publicar, confira a URL do asset e abra a loja no pacote Production. Os próximos plugins entram pelo mesmo feed; não é necessário alterar uma lista de nomes no código da loja.

### Verificações antes de lançar o plugin

Antes de atualizar o feed da `main`:

1. confirme que os quatro plugins padrão estão presentes em `Plugins/Bundled`, com módulo Core, renderizador e manifesto compatíveis;
2. inclua em `main/PluginPackages/feed.json` as entradas opcionais aprovadas que devem aparecer para todos;
3. confirme que todos os assets anunciados no feed estão publicados, acessíveis e com SHA-256 correto;
4. teste com um build DEV e um feed local temporário; depois da publicação, abra **Mais plug-ins** no Production;
5. valide a lista completa: incluídos aparecem como instalados, opcionais aparecem para instalação e atualizações preservam estado;
6. execute `--plugin-v2`, `--plugin-delivery` e `--plugin-store` antes de atualizar o feed oficial.

Uma publicação está incompleta se o catálogo aprovado não estiver visível ou instalável no aplicativo principal.

Para testar outro feed sem publicar, defina `CLIPDESK_PLUGIN_FEED_URL` antes de abrir um build DEV/New. São aceitos HTTPS ou HTTP em loopback durante desenvolvimento. Exemplo:

```powershell
$env:CLIPDESK_PLUGIN_FEED_URL = "http://localhost:8080/feed.json"
dotnet run --project .\ClipDesk.csproj
```

Remova a variável ao terminar. DEV e Production usam o feed de `main` por padrão; o override não deve ser configurado em instalações de usuário. O botão de publicação via servidor exige `CLIPDESK_PLUGIN_STORE_URL` apontando explicitamente para um serviço compartilhado. Sem esse serviço, publique ZIP e entrada do feed diretamente no GitHub conforme os passos acima.

Os limites são 25 MiB compactado, 80 MiB extraído e 256 arquivos. O hash evita troca/corrupção relativa ao feed, mas não transforma código de terceiros em código confiável.

## Compatibilidade e localização dos arquivos

- V1 (`IWindowsPlugin`, `wpf-v1`) continua carregável, mas novos plugins devem usar v2.
- Incluídos: `Plugins/Bundled/<id>` ao lado do aplicativo.
- Opcionais: `Documentos\Clipdesk\<id>\<versão>`; no DEV, `Documentos\Clipdesk\Dev`.
- Atualização de incluído: cache privado `<DataRoot>/Plugins/<id>/<versão>`.
- `current-version.txt` registra a versão lógica e `current-package.txt` identifica o diretório imutável ativo quando há reparo; versões antigas não são apagadas automaticamente.

Na loja, **Adicionar** coloca uma nova instância na mesa. A seta ao lado abre:

- **Reparar**: obtém novamente o pacote validado, instala uma cópia limpa em novo diretório e a ativa de forma atômica. Objetos, configurações e estado funcional das instâncias são preservados.
- **Desinstalar**: desativa o plugin neste dispositivo sem remover os objetos ou seus dados das mesas. Enquanto estiver ausente, a mesa mostra a orientação de instalação; ao ativar o plugin novamente, o estado reaparece.

Uma atualização automática nunca reativa um plugin que o usuário desinstalou. Para plugins baixados, reparar exige que a versão esteja publicada no feed com URL e SHA-256 válidos. Para plugins incluídos no aplicativo, o pacote embarcado continua sendo o fallback confiável.

O estado da instância é sincronizado; o pacote não. Se o plugin faltar num dispositivo, o objeto e seus dados permanecem. Uma versão nova deve continuar lendo o estado produzido por versões anteriores durante a janela de atualização.

## Plugins padrão como referência

Cada um demonstra uma preocupação distinta:

- [Checklist](../PluginPackages/Modules/Checklist/Core/ChecklistModule.cs): migração e IDs estáveis.
- [Calculadora](../PluginPackages/Modules/Calculator/Core/CalculatorModule.cs): regra totalmente portável e determinística.
- [Tradutor](../PluginPackages/Modules/Translator/Core/TranslatorModule.cs): rede do host, cancelamento e limite UTF-8.
- [Conversor](../PluginPackages/Modules/CurrencyConverter/Core/CurrencyConverterModule.cs): cache concorrente, cultura invariável e dados transitórios.

Os respectivos arquivos `*Plugin.cs` mostram a camada WPF. Não copie lógica de domínio para o renderizador.
