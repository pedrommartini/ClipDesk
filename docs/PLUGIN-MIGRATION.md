# Migração de plugins para o contrato comum

1. **Inventarie o plugin.** Liste comandos, estado, arquivos, rede, áudio, imagens, permissões e dependências de UI/plataforma. Para pacotes v1 ou WPF monolíticos, extraia primeiro regras e estado para um projeto Core `net8.0`.
2. **Preserve estado.** Escolha `stateVersion`, implemente `NormalizeState` para formatos antigos, mantenha chaves desconhecidas quando possível e teste idempotência. Não copie mídia grande para `PluginState`.
3. **Troque efeitos diretos por capabilities.** No Core, substitua `File`, `Directory`, `HttpClient`, microfone nativo, clipboard e caminhos do usuário por interfaces do host. Declare requirement e permission; trate `Unavailable` e `PermissionDenied`.
4. **Separe renderers.** Mantenha WPF apenas no adapter Windows; crie renderers locais para Web/Android/iOS quando esses hosts existirem. Use `PluginViewport` e layout lógico, sem mínimo quadrado. Um recurso exclusivo declara `platformExtensions` e fallback.
5. **Valide.** Rode `PluginDevKit.Tool`, build e testes de comandos com implementações falsas de capabilities. Inspecione tamanhos, orientação, escala de texto, teclado e touch no host real.
6. **Distribua conforme o host.** O Windows atual instala v1/v2. Para v3, registre módulos/renderers no build do host antes de anunciar suporte. Não renomeie um manifesto v2 para v3 sem migrar o contrato de execução.

Padrões depreciados: `PluginStoragePaths` e `PluginBoardFile.FromPath` no Core, mínimo quadrado obrigatório, acesso a rede/arquivos diretamente do Core e dependência universal de DLL. As APIs antigas permanecem para compatibilidade binária Windows; novos plugins usam `IPluginFileService` e capabilities. TTS já moveu seus caminhos locais para `WindowsPluginFiles`; seu processamento de fala ainda é Windows e precisa de adapter para outros hosts.
