# Dev Kit oficial de plugins ClipDesk

Este é o ponto de entrada para criar plugins. As regras executáveis em `PluginSdk/PluginManifestValidator.cs` e `PluginDevKit.Tool` são a fonte de verdade do manifesto e da separação do Core. Execute a ferramenta após cada alteração. O [plano](PLUGIN-DEVKIT-PLAN.md) registra o diagnóstico que motivou este contrato; o [guia de migração](PLUGIN-MIGRATION.md) cobre pacotes existentes.

## Situação dos hosts

| Host | Pacotes executáveis hoje | Contrato planejado |
| --- | --- | --- |
| ClipDesk Windows atual, `main` e DEV | v1 e v2 com renderer WPF | v3 quando o host registrar módulo, renderer e capabilities |
| ClipDesk New Windows | v1 e v2, conforme a branch atual | v3 com host próprio; WPF não integra o Core |
| Web, Android, iOS | Nenhum host de plugins neste repositório | v3 com registro estático e renderer local |

O SDK é único: módulos v2 continuam válidos para hosts que anunciam API 2; hosts futuros anunciam API 3 e capabilities implementadas. O contrato v3 não exige DLL nem carregamento dinâmico. Um manifesto com `registration` descreve entradas que o build/host registra estaticamente. Declarar uma plataforma ou registration não cria o renderer nem a implementação de host correspondente.

`PluginHostRegistry<TView>` liga IDs de registration a factories compiladas no host. Ele confere plataforma, manifesto e presença do renderer antes de criar a sessão. `IPluginRendererAdapter<TView>` recebe estado, viewport e uma função de despacho de comandos; o tipo da view pertence ao host. O mesmo módulo pode ser registrado em Windows, Web e Mobile com renderers distintos. A execução v3 permanece planejada nos aplicativos atuais até que eles incluam esses registros e serviços.

## Comece pelo alvo real

- **Plugin executável no Windows atual:** copie `PluginPackages/Templates/Starter`, renomeie projeto, namespace, tipo, ID e manifesto; mantenha `manifestVersion: 2`. O [guia v2](PLUGIN-AI-DEVKIT.md) explica o renderer WPF e o empacotador atual.
- **Plugin para um novo host:** use `PluginPackages/Examples/AudioNotes` como referência do Core v3 e implemente os registros do módulo e renderer no host alvo. Use `manifestVersion: 3`, `runtime: portable-v3`, `pluginApiVersion: 3` e `contractVersion: 3.x.y`.

Valide qualquer pasta antes de empacotar:

```text
dotnet run --project PluginDevKit.Tool -- <caminho>/manifest.json <caminho>
```

Erros `CDK` explicam a violação e a correção. `pack-plugin.ps1` invoca a mesma validação antes de compilar. O empacotador atual produz pacotes Windows v1/v2; distribuição v3 depende do host que registrar suas entradas.

O ZIP inclui `manifest-v3.schema.json`, gerado dos tipos do SDK no build do kit para autocomplete e campos válidos. Regras entre campos, como capabilities e permissões, são verificadas por `PluginManifestValidator`.

## Modelo portátil

O Core referencia apenas `ClipDesk.PluginSdk` e usa `net8.0`, sem sufixo de plataforma. Na v3 ele implementa `IClipDeskPluginModuleV3`: estado inicial, normalização e comandos assíncronos. `PluginState` é o estado persistido e sincronizado; armazene dados pequenos e versão em `$schema`. Use `CancellationToken` para trabalho longo. O renderer recebe viewport lógico, densidade, escala de texto, orientação e tipo de entrada; traduz interação em `PluginCommand`. Não persista streams, bytes grandes, caminhos locais nem handles no estado.

`IClipDeskPluginLifecycle` é opcional para recursos que precisam de initialize/activate/suspend/resume/save/dispose. O host chama os métodos nessa ordem e cancela trabalho ao suspender ou descartar. Cada host deve executar comandos fora da thread de UI, impor limite de duração/memória e revogar recursos no dispose. Esse isolamento ainda não existe para DLLs Windows v2 executadas no processo.

`PluginModuleSession` coordena estado, capabilities obrigatórias, lifecycle, cancelamento e timeout. O host fornece `IPluginExecutionScheduler` para tirar comandos da thread de UI ou isolá-los em worker/processo conforme sua plataforma. Timeout cancela a espera; código não confiável ainda exige isolamento real pelo host.

### Capabilities e permissões

O host passa `IPluginCapabilityProvider`; o módulo chama `Resolve<T>(id, minimumVersion)`. O resultado distingue recurso ausente, versão incompatível, permissão não declarada, permissão negada e tipo incompatível. A versão maior deve coincidir. O registro exige permissão declarada **e** concedida. A permissão continua sendo verificada pelo host; o registro do SDK não transforma código Windows em processo em sandbox.

`PluginModuleSession` envolve o provedor em `ManifestBoundCapabilityProvider`: uma capability ausente de `requirements` não pode ser resolvida, mesmo que o host a ofereça. O host deve criar `PluginCapabilityRegistry` com permissões declaradas pelo manifesto e concessões efetivas do usuário/sistema.

IDs convencionais incluem `host.http`, `host.file-read`, `host.file-write`, `host.storage`, `host.audio-input`, `host.audio-output`, `host.system-audio`, `host.image-processing`, `host.clipboard`, `host.notifications` e `host.background-tasks`. A lista é aberta: serviços futuros usam IDs próprios e interfaces versionadas. Leitura e escrita de arquivo são separadas para não conceder acesso além do necessário. APIs de arquivo usam handles opacos; a Web pode implementar com File API e o Mobile com URIs do sistema. `host.http` é implementado pelo host com allowlist e limites. Áudio é stream assíncrono; arquivo e áudio ficam fora do estado.

O manifesto v3 declara `requirements` com `id`, `minimumVersion`, `permission` e `optional`. Para `host.audio-input`, declare `microphone`; para `host.http`, `network`; para `host.file-read`, `file.read`; para `host.file-write`, `file.write`. Também declare a permissão em `permissions`. Recursos opcionais devem ter fallback de comando/UI. Extensões de plataforma usam `platformExtensions` com `platform`, `capability`, `permission` quando sensível e `fallbackCapability` para as demais plataformas.

### Layout e renderização

`layout.minimum`, `preferred` e `maximum` são dicas em unidades lógicas, não pixels físicos; todos são opcionais. `compactBelowWidth`/`compactBelowHeight` indicam pontos de reflow e `allowScroll` habilita overflow. Não há formato quadrado obrigatório na v3. O renderer escolhe composição, aparência e framework, mas deve funcionar no mínimo declarado, respeitar escala de texto e input touch/teclado e liberar assinaturas. Cada plataforma pode ter renderer distinto.

### Versões e compatibilidade

Versões de contrato/capability seguem `MAJOR.MINOR.PATCH` estável. Major incompatível exige negociação/fallback; minor adiciona recursos; patch corrige. `stateVersion` é separado e deve ter normalização idempotente. O host informa API e capabilities presentes; uma branch antiga pode carregar o mesmo módulo se atender às necessidades obrigatórias. APIs v1/v2 continuam na ponte Windows; não são base para novos hosts.

### Guardrails e limites

A ferramenta rejeita campos desconhecidos, entradas incompletas, versões incorretas, dimensões inválidas, permissão ausente e importação de WPF/Win32/System.Speech/PInvoke no Core. Também rejeita acesso direto comum a arquivos e HTTP no Core. Essas verificações são guardrails de desenvolvimento, não análise completa de segurança. O host precisa validar o pacote, conceder permissões e implementar isolamento apropriado antes de executar código de terceiros. Atualmente o Windows valida ZIP/SHA e limita rede, mas carrega DLLs em processo.

## Exemplos e verificação

Os quatro módulos padrão (`Calculator`, `Checklist`, `Translator`, `CurrencyConverter`) implementam v2 e v3 sobre a mesma lógica/estado. Seus renderers e pacotes atuais continuam v2 no Windows; novos hosts precisam registrar renderers v3. `AudioNotes` demonstra microfone e arquivos via capabilities; `ImageCompressor`, leitura, escrita e codec com permissões separadas. O TTS e plugins colaboradores continuam casos de migração Windows, não provas de suporte Web/Mobile. Execute `Tests/PluginDevKit` e os testes v2 ao modificar o contrato.
