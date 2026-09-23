# ClipDesk New — plano e status da migração

Última atualização: 22 de setembro de 2026.

## Propósito e regras da branch

`clipdesk-new` é uma branch de desenvolvimento de longo prazo. Ela não substitui `main`, não é o canal de produção e não deve ser promovida integralmente enquanto o novo host não atingir os critérios de estabilidade deste documento.

- `main`: ClipDesk principal e produção.
- `codex/clipdesk-dev`: desenvolvimento e validação contínua do WPF atual.
- `clipdesk-new`: arquitetura compartilhada e futura aplicação MAUI.

Mudanças portáveis podem nascer em `clipdesk-new` e seguir para `codex/clipdesk-dev` quando preservarem compatibilidade. O movimento inverso deve trazer apenas correções necessárias, não dependências novas de WPF para o Core. Nenhuma etapa deste plano autoriza deploy em `main`.

Quando o ClipDesk DEV for futuramente promovido para `main`, a loja de plugins deverá fazer parte do release já funcional e com o catálogo de produção completo. Durante desenvolvimento os feeds permanecem separados; na promoção, plugins opcionais aprovados são copiados para o feed de `main`, enquanto os quatro plugins padrão continuam incluídos no próprio build.

## Objetivo arquitetural

Compartilhar Core, regras, sincronização, estado e ViewModels entre Windows e Android. UI será compartilhada quando a experiência realmente for equivalente; integrações do sistema permanecem atrás de adaptadores de plataforma.

```text
ClipDesk.Domain / Application / Sync / PluginSdk
                     │
             ViewModels compartilhados
               ┌─────┴─────┐
          MAUI Windows   MAUI Android
               │             │
      serviços Windows  serviços Android
```

O WPF atual permanece funcional durante toda a transição. A aposentadoria do WPF só será considerada depois de paridade funcional, migração de dados, telemetria de estabilidade e período de uso real do MAUI no Windows.

## Status por fase

| Fase | Status | Entrega / critério |
| --- | --- | --- |
| 0. Governança e isolamento | Concluída | Branch dedicada, `main` preservado e documentação viva criada |
| 1. Contratos de plugins portáveis | Concluída | Manifesto v2, estado versionado, comandos, permissões e módulos `net8.0` |
| 2. Plugins no ClipDesk DEV/WPF | Concluída | Carregador v1/v2, quatro plugins convertidos, feed DEV separado e cor de destaque por instância |
| 3. Fundação da solução compartilhada | Próxima | Separar Domain/Application/Infrastructure e remover dependências de desktop do fluxo principal |
| 4. Shell MAUI Windows | Não iniciada | Navegação, DI, tema, armazenamento e primeira mesa funcional |
| 5. Renderizadores MAUI de plugins | Não iniciada | Contrato MAUI e paridade visual/funcional dos quatro plugins |
| 6. Android | Não iniciada | Clipboard consciente do ciclo de vida, SAF, WorkManager, permissões e sincronização resiliente |
| 7. Paridade e estabilização | Não iniciada | Migração, acessibilidade, desempenho, testes e período de convivência WPF/MAUI |
| 8. Decisão sobre aposentadoria WPF | Bloqueada por critérios | Só depois de a versão MAUI Windows comprovar maturidade |

## Gate de release DEV → Main

Uma futura promoção do DEV para o ClipDesk principal exige, no mínimo:

- loja acessível e funcional no build de produção;
- todos os plugins padrão visíveis como instalados;
- todos os plugins opcionais aprovados visíveis pelo feed de `main`;
- instalação, atualização, integridade do ZIP e preservação de estado validadas;
- nenhuma URL de feed DEV no binário de produção;
- testes `plugin-v2`, `plugin-delivery` e `plugin-store` verdes;
- rollback documentado para aplicativo, feed e pacotes.

Esse gate promove uma versão validada da loja; ele não conecta produção permanentemente ao catálogo de desenvolvimento.

## Entregas registradas

### 2026-09-22 — sistema de plugins v2

- Criados contratos portáveis sem referência a WPF ou MAUI.
- Separados módulos Core e renderizadores WPF.
- Convertidos Checklist, Calculadora, Tradutor e Conversor de moedas.
- Checklist migrou de índices frágeis para itens com IDs estáveis.
- Rede passou a ser fornecida pelo host com HTTPS, allowlist, timeout, limite de resposta e cancelamento.
- Mantida leitura e execução de pacotes v1 durante a transição.
- Catálogo, empacotamento e feed passaram a entender manifesto v2.
- Feed de desenvolvimento separado do feed de produção.
- Adicionada cor de destaque por instância, sincronizável e configurável no submenu de seleção.
- Criados testes portáveis e validações de carregamento, instalação, loja e ZIP.
- Criado Plugin AI DevKit autossuficiente, com especificação compacta, template compilável e SDKs isolados para desenvolver plugins sem analisar a codebase do aplicativo.
- Ajustada a paleta de destaque para aparecer exclusivamente na seleção de movimentação do plugin, separada da interação interna e do menu de contexto.

## Próximos passos recomendados

1. Criar projetos `ClipDesk.Domain`, `ClipDesk.Application` e `ClipDesk.Infrastructure.Abstractions`, movendo código em fatias pequenas e mantendo adaptadores no WPF.
2. Formalizar ViewModels compartilhados e fluxos de navegação sem tipos de UI.
3. Criar o shell MAUI Windows como aplicação paralela, inicialmente lendo um perfil de dados isolado.
4. Implementar um adaptador de armazenamento compatível com o formato atual e ensaiar migração reversível.
5. Criar o contrato de renderizador MAUI e portar um plugin local primeiro — Calculadora é o candidato de menor risco.
6. Validar a mesma Calculadora no Android antes de portar plugins com rede.
7. Introduzir clipboard, arquivos e execução em segundo plano por interfaces específicas de plataforma.

## Critérios para considerar uma fase concluída

- Build limpo sem avisos nos projetos afetados.
- Testes automatizados das regras e migrações.
- Dados antigos abrem sem perda e a migração é idempotente.
- Nenhuma dependência WPF/Win32 entra em projetos compartilhados.
- Falhas offline e cancelamento possuem comportamento definido.
- Documentação e matriz de compatibilidade são atualizadas na mesma mudança.
- Alterações que chegam ao DEV são verificadas no WPF antes de qualquer promoção futura.

## Riscos acompanhados

| Risco | Situação e mitigação |
| --- | --- |
| Carregamento dinâmico no Android | Não será usado; módulos Android serão compilados e registrados estaticamente |
| Clipboard em segundo plano | Android não garante monitoramento contínuo; desenhar experiência baseada em ciclo de vida e ações explícitas |
| Arquivos e caminhos Windows | Usar abstração de documentos/streams e Storage Access Framework no Android |
| Tarefas longas | Usar serviço do host; WorkManager/foreground service quando necessário no Android |
| Divergência WPF/MAUI | Regras, comandos, estado e testes ficam nos módulos compartilhados |
| Código de terceiros | Plugins Windows ainda não têm sandbox; manter catálogo confiável até existir assinatura e isolamento |
| Migração de dados | Manter backups, schema versionado e caminho de rollback até estabilização |

## Verificações pendentes conhecidas

A suíte de nuvem atualmente falha no cenário `Real desktop sync service completes an initial personal sync`, depois de as verificações anteriores passarem. Esse ponto precisa de diagnóstico próprio antes da fase de infraestrutura compartilhada ser considerada concluída.

## Como manter este documento

Toda entrega relevante em `clipdesk-new` deve atualizar:

1. a data no topo;
2. o status da fase correspondente;
3. a seção **Entregas registradas** com comportamento e validação;
4. riscos novos ou resolvidos;
5. próximos passos, evitando marcar como concluído o que existe apenas como contrato ou protótipo.
