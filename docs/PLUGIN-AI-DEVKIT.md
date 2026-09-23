# ClipDesk Plugin AI DevKit — especificação autossuficiente

Use este arquivo junto com `ClipDesk-Plugin-AI-DevKit.zip`. Ele é a especificação completa para criar um plugin ClipDesk v2 sem analisar a codebase do aplicativo.

## Instrução para o modelo de IA

Crie **somente** uma nova pasta de plugin a partir de `PluginPackages/Modules/Starter`. Não altere `PluginSdk`, `PluginSdk.Windows`, scripts, feeds, branches ou o aplicativo. Não pesquise a codebase do ClipDesk: os SDKs incluídos no ZIP são dependências de compilação e esta especificação é a fonte de verdade.

Antes de entregar:

1. substitua `Starter`, `starter`, nomes, descrição, ícone e cor pelo plugin solicitado;
2. implemente regras no projeto `Core` e UI somente no renderizador WPF;
3. compile, teste manualmente os comandos e execute o empacotador;
4. entregue a pasta do plugin, o ZIP empacotado e um resumo dos comandos, estado, permissões e testes.

Se um requisito não for coberto pelos contratos abaixo, não invente APIs do host. Registre a limitação no resumo.

## Resultado esperado

```text
PluginPackages/Modules/<NomePascal>/
├── Core/
│   ├── ClipDesk.Plugin.<NomePascal>.Core.csproj
│   └── <NomePascal>Module.cs
├── ClipDesk.Plugin.<NomePascal>.csproj
├── <NomePascal>Plugin.cs
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

await context.ExecuteAsync(command, rebuild, cancellationToken, recordUndo)
context.Commit(state, rebuild)
context.NotifyBeforeChange()
context.RequestHostAction(action)
```

Use `ExecuteAsync` para alterar estado. `rebuild: false` preserva foco durante digitação; `recordUndo: false` pode ser usado em mudanças contínuas depois de registrar um ponto de desfazer adequado. Marque controles interativos com `Tag = "plugin-interactive"`.

A UI deve:

- funcionar em tema claro e escuro e aproximadamente a partir de 300×240;
- redimensionar sem cortar conteúdo e usar rolagem quando necessário;
- tratar `context.AccentColor` como o tema da instância: aplicar em botões primários/ativos, indicadores e resultados relevantes; controles neutros podem manter a cor de superfície;
- garantir contraste para qualquer cor personalizada e não manter pincéis de destaque estáticos entre reconstruções da view;
- não criar seletor de cor dentro do plugin; o host mostra a paleta somente quando o objeto é selecionado pelo cabeçalho para mover/redimensionar;
- não usar `AccentColor` para bordas ou alças de seleção: o host desenha esse outline com a cor de presença do colaborador e transmite o arraste em tempo real;
- respeitar `context.Scale`, teclado, foco e alvos de toque confortáveis;
- cancelar operações pendentes em `Unloaded`;
- apresentar erros curtos e recuperáveis, sem detalhes técnicos na tela.

Ações externas permitidas no renderizador:

```csharp
context.RequestHostAction(new(PluginHostActionKind.CopyToClipboard, texto));
context.RequestHostAction(new(PluginHostActionKind.OpenUri, url));
context.RequestHostAction(new(PluginHostActionKind.ShowMessage, mensagem));
```

Não chame clipboard, `Process.Start`, shell ou APIs globais diretamente.

## Rede e permissões

Para rede, declare `"network"` em `permissions`, liste hosts exatos em `networkHosts` e use apenas:

```csharp
if (!context.HasPermission(PluginPermissions.Network))
    return new(state, PluginCommandStatus.PermissionDenied, "Rede não autorizada.");

var json = await context.Network.GetStringAsync(
    new Uri("https://api.example.com/data"), cancellationToken);
```

Somente HTTPS é aceito; subdomínios não são implícitos, redirecionamentos são bloqueados e a resposta é limitada pelo host. Defina comportamento offline. Arquivos e tarefas em segundo plano ainda não possuem serviço v2: não os implemente diretamente.

## Manifesto v2

Edite o `manifest.json` do template. Preserve:

- `manifestVersion: 2`, `runtime: "portable-v2"` e `pluginApiVersion: 2`;
- `stateVersion` igual a `IClipDeskPluginModule.StateVersion`;
- `module` apontando para a montagem Core e o tipo completo do módulo;
- renderizador Windows com `platform: "windows"` e `runtime: "wpf-v2"`;
- `platforms: ["windows"]` enquanto não existir renderizador MAUI compilado;
- `capabilities: ["board-widget"]` para widgets da mesa;
- `defaultContent` coerente com `CreateDefaultState`;
- `installByDefault: false` para plugins opcionais.

Não anuncie Android apenas porque o Core é portável. No Android, o módulo e um renderizador MAUI precisarão ser compilados estaticamente no aplicativo.

## Compilar, validar e empacotar

Na raiz extraída do DevKit:

```powershell
dotnet build .\PluginPackages\Modules\<NomePascal>\ClipDesk.Plugin.<NomePascal>.csproj
.\PluginPackages\pack-plugin.ps1 -PluginDirectory .\PluginPackages\Modules\<NomePascal>
```

O segundo comando recompila em Release, valida as montagens declaradas e cria `PluginPackages/dist/<id>-<versão>.zip`, imprimindo SHA-256 e a entrada de feed. Corrija todos os erros antes de entregar.

Teste no mínimo:

- estado vazio, antigo, incompleto e malformado;
- todos os comandos válidos e inválidos;
- cancelamento e falha offline, quando aplicável;
- abrir, editar, fechar e reabrir sem perder estado;
- tema claro/escuro, cor de destaque, tamanhos diferentes, zoom e teclado;
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
- [ ] Trocar a cor atualiza cabeçalho, ações primárias e indicadores imediatamente, sem reabrir o plugin.
- [ ] O plugin não desenha nem persiste outline de seleção ou cor de colaborador.
- [ ] Manifesto anuncia somente recursos realmente implementados.
- [ ] Build e empacotamento terminam sem erro.
- [ ] Nenhum feed, SDK, aplicativo ou branch foi modificado.
