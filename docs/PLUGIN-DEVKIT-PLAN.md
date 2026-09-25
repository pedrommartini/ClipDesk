# Plano de evolução do Dev Kit (2026-09-25)

## Diagnóstico

1. **Arquitetura atual.** `PluginSdk` define manifesto v1/v2, estado em pares de strings, comandos e um contexto com rede. `PluginSdk.Windows` contém o renderer WPF e ações de host. O catálogo valida e instala pacotes; o loader Windows executa DLLs no processo. Os quatro plugins incluídos têm Core `net8.0` e renderer WPF. O TTS e os plugins colaboradores exercitam mídia, arquivos e APIs do Windows.
2. **Problemas.** Compatibilidade e validação estão espalhadas por catálogo, empacotador e SDK. A API 2 exige renderer e DLL no contrato, só reconhece Windows/Android, impõe mínimo quadrado e não oferece descoberta versionada de recursos, ciclo de vida ou acesso geral a capabilities. O contexto de rede é estreito; o renderer aciona efeitos por callback. Uma permissão declarada não cria sandbox para DLL executada no processo.
3. **Acoplamentos.** `PluginStoragePaths` no Core escolhe `Documents` via `Environment`; `PluginBoardFile.FromPath` usa caminho local. TTS usa `System.Speech`, arquivos e WPF no projeto Windows. O catálogo e o loader exigem montagens `.dll`; os renderers atuais são WPF. Web e iOS não constam da compatibilidade.
4. **Acertos.** Estado imutável, comandos canceláveis, separação Core/renderer, permissões de rede e arquivo, migração da checklist, instalação íntegra e fallback de plugin ausente já oferecem uma base aproveitável.
5. **Reuso.** Manter o contrato v1/v2 para hosts antigos, os módulos Core, o renderer Windows, o catálogo e os testes de integração como ponte. Os novos contratos serão aditivos e não exigirão refazer plugins por mudança de host.

## Contrato proposto

6. **Arquitetura.** Um núcleo sem UI/plataforma define manifesto, módulo, ciclo de vida, layout e capabilities. Cada host registra implementações; cada renderer adapta a UI. O manifesto não deve fazer DLL ser requisito universal: entradas serão descritores de artefato por runtime/plataforma, com registro estático possível em Web/Mobile. O carregamento de DLL permanece no host Windows.
7. **SDK Core.** Tipos estáveis de estado, comandos, resultado, metadados, versionamento, descoberta e resolução de capabilities, lifecycle cancelável e métricas de viewport. Nenhuma referência a WPF/Win32/DOM/MAUI.
8. **Hosts.** Resolver capabilities e permissões, controlar arquivos/rede/dispositivos, persistir, agendar, impor limites, revogar recursos e isolar execução não confiável. Hosts antigos anunciam apenas o que implementam.
9. **Renderers.** Traduzir comandos e viewport para UI local, fazer reflow/scroll/entrada acessível e limpar assinaturas. Windows pode continuar em WPF; Web e Mobile podem usar frameworks próprios.
10. **Capabilities.** Identificadores abertos, versões semânticas e `TryGet`/detecção antes do uso. Capabilities sensíveis passam por autorização do host. Extensões de plataforma são declaradas e têm fallback.
11. **Permissões.** Manifesto declara permissões; validador cruza declaração com uso conhecido e configurações como `networkHosts`. A autorização efetiva é do host, também em tempo de execução. DLLs Windows em processo continuam uma limitação de segurança explícita.
12. **Responsividade.** Mínimo/preferido/máximo opcionais, sem forma quadrada; viewport em unidades lógicas, escala, orientação e modo de entrada. Renderer decide composição e identidade visual.
13. **Versões.** SemVer no contrato e em cada capability; versão maior para quebra, menor para adição, patch para correção. Depreciação com diagnóstico e guia de migração. `pluginApiVersion` numérico v1/v2 permanece ponte.
14. **Branches.** Publicar um único contrato. `main` e DEV mantêm os mesmos arquivos base de SDK nesta revisão; `clipdesk-new` altera manifesto, renderer, entrega e remove TTS/colaboradores; `codex/release-043-main` diverge no Dev Kit/loader; `codex/slider-lab-main` remove TTS/feed. Integração deve ser por host e capability. ClipDesk New orienta os contratos novos. Não copiar arquivos para outra branch automaticamente enquanto seus worktrees mantêm trabalho próprio.
15. **Plugins.** Os quatro incluídos validam comandos, estado e rede; TTS e colaboradores expõem lacunas de áudio, imagem e arquivo. Migração gradual: retirar acesso a plataforma do Core, declarar capability, registrar fallback, implementar renderer por plataforma, testar. Compatibilidade v2 Windows continua enquanto isso.
16. **Guardrails.** Validador único de manifesto, verificação de arquitetura do Core, diagnóstico de permissão e compatibilidade, checagens no empacotamento/CI, exemplos executáveis. Mensagens devem indicar correção.
17. **Liberdade.** Sem lista fechada de tipos de plugin, layout obrigatório ou framework visual universal. Novas capabilities, renderers e extensões podem ser registrados sem mudar módulos existentes.

## Ordem de execução

1. Criar contratos aditivos e validação automatizada no SDK, preservando v1/v2.
2. Integrar validação ao empacotador e ao catálogo Windows; remover a regra do mínimo quadrado dos novos contratos.
3. Atualizar template, documentação oficial e exemplos de capabilities avançadas; usar os plugins reais como verificações.
4. Rodar builds e testes; registrar suporte real de cada host e lacunas que dependem da implementação futura de Web/Android/iOS.
