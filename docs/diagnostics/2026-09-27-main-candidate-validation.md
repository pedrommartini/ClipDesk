# Candidato Production 0.4.4 — validação local

Data: 27/09/2026. Ramo isolado `codex/shared-board-main-044`, integração `5844922`. Nenhuma publicação externa realizada. A instalação diária e o checkout main do usuário permaneceram intactos.

## Compilação e pacote

A integração transportou as correções de colaboração do candidato DEV para main e preservou o tratamento específico de zoom do renderer de plugins em Production. Core Release: 45 verificações passaram. Cloud Release: 82 passaram. Visual Release completo passou. Dev Kit Release passou a verificação de manifesto v3, capabilities, permissões, versões e fluxos de áudio/imagem/arquivo.

O instalador Production foi compilado com o payload na unidade E: por falta de espaço livre na unidade C:. `ClipDeskPayloadPath` é um parâmetro opcional do projeto do instalador; o caminho padrão original permanece. O instalador extraído por `--test-install` relatou `success: true`, 539 arquivos e destino padrão `C:\Program Files\ClipDesk`. Os hashes de `ClipDesk.dll`, `ClipDesk.Core.dll` e `ClipDesk.PluginSdk.Windows.dll` extraídos coincidiram com os do payload publicado localmente.

- Instalador candidato: `artifacts/release-smoke/main-0.4.4-integration/setup/ClipDesk-Setup.exe`, 206.874.429 bytes, SHA-256 `96E5B5AC37A2BDE2AB88435C2EABF6729C4CF088D17B32FBB3BC06FB6F407161`.
- ZIP candidato: `artifacts/release-smoke/main-0.4.4-integration/ClipDesk-Windows-x64.zip`, 128.484.426 bytes, SHA-256 `0214D82221C1BF716E845BC4D4239CF516B05AD34623C344F785F2FD97F066E5`.
- Binários de teste extraídos: `artifacts/release-smoke/main-0.4.4-integration/scratch/app`. Os artefatos ficam fora do ramo main, em `E:\AI\Codex\ClipDesk\artifacts`.

## Mesa real descartável

Dois perfis Production isolados, autenticados como `@robsonarruda` e `@priscilaromao`, abriram a mesa descartável `Teste sync Codex 26-09` (`7996723f92044f0eac03e0dd104f0e05`). Ambos carregaram texto e nota anteriores ao convite, checklist e cards posteriores. Uma marcação do terceiro item no Cliente 1 apareceu no Cliente 2; a marcação inversa no Cliente 2 apareceu no Cliente 1 e restaurou o estado inicial.

O perfil Production do segundo participante não possui o pacote opcional Texto para Fala. O card permaneceu na mesa e exibiu `package_missing`, um ID de incidente, opção de copiar relatório e ação de instalação. Isso confirma o comportamento de pacote ausente, não a renderização do pacote nesse perfil.

Depois, o Cliente 1 Production foi substituído pelo cliente DEV 0.4.4 do perfil isolado existente. DEV abriu a mesma mesa com os elementos anteriores e o TTS 1.1.0 renderizado. Uma alteração de checklist DEV → Production e a alteração reversa Production → DEV chegaram ao outro cliente. Nesse teste misto, o zoom era 89% no DEV e 78% no Production. A posição do cursor foi observada qualitativamente, sem medida nova de erro geométrico ou latência.

Ao final, os bancos locais de DEV Cliente 1 e Production Cliente 2 continham oito entidades da mesa, versões de 1 a 31, e o SHA-256 do conjunto ordenado de ID, versão e JSON remoto era idêntico: `60FBCAB05FFC0C3251D5C7E1348515638FC162DF08B982E8CD7312C7814D5E8A`. Em ambos, nenhuma entidade tinha JSON local diferente do remoto, nem havia `sync_failures` ou conflitos associados à mesa. O conflito antigo registrado no DEV Cliente 2 não participou deste teste.

O controle visual foi desligado depois dos ensaios. Os dois processos de teste restantes podem continuar abertos para inspeção; nenhum deles substitui a instalação diária.

## Pendências antes do deploy

Ainda faltam ensaio visual dos 14 plugins em ambos os canais; configurações/anexos e resultados em cada plugin; anexos acima de 30 MB com autenticação Drive e integridade de download; edição concorrente, texto/desenho/nota/conectores/seleção/desfazer em uso real; gravação de áudio enquanto chegam eventos; medição de latência de edição e cursor entre computadores, diferentes DPI e zoom; upgrade/rollback; e identificação da operação 403 original de Pedro/Renan. As medições de presença anteriores cobrem somente dois clientes DEV no mesmo computador (medianas 32,9–36,3 ms e p95 54,5–56,0 ms).

O caso de pacote ausente é esperado no perfil Production sem TTS; para aprovação da matriz de plugins é necessário instalar pacotes candidatos em perfis isolados e verificar o estado compartilhado. Não publicar os feeds DEV ou Production, pacotes, release ou landing como versão estável até completar os critérios de saída do plano de colaboração.

