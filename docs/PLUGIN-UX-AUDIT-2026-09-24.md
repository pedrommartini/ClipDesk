# Auditoria visual dos plugins da Mesa 3 — ClipDesk DEV

Inspeção manual em 24/09/2026 com a instância aberta do aplicativo. Foram comparados os 13 exemplares em visão geral (28–44% de zoom), parte deles em 60–66%, e o Upscale de Imagem em 92%. A janela foi observada em aproximadamente 1536×864 e 1065×864. Upscale de Imagem e Temporizador foram reduzidos perto do limite compacto e restaurados ao tamanho anterior. Os demais apontamentos de compactação são leitura visual da instância, não resultado de redimensionamento individual.

| Plugin | O que apareceu na mesa | Direção para a próxima atualização do plugin |
| --- | --- | --- |
| Checklist | Três itens ocupam a parte superior; sobra muita altura vazia. Texto e caixas são pequenos no zoom distante. | Aproximar itens, aumentar rótulos e área clicável; deixar a lista crescer e rolar sem reservar vazio permanente. |
| Calculadora | Teclado ocupa quase tudo; visor e número atual ficam pequenos em relação às teclas. | Priorizar o número no visor e manter as teclas legíveis no mínimo; copiar o resultado atual por ícone discreto. |
| Tradutor | Entrada e resultado se perdem à distância; a mensagem de validação aparece em inglês; não há cópia junto à tradução. | Valorizar a tradução, localizar o estado e incluir ícone que copie apenas a tradução concluída. |
| Conversor de Moeda | `0,19 USD` é pequeno e há distância vertical grande entre entrada e resultado; não há cópia direta. | Aproximar entrada/resultado, ampliar o valor convertido e copiar valor com moeda por ícone ao lado. |
| Gerador de QR Code | O código ocupa bem a área e já existe cópia; URL fica muito pequena e o botão “Copiar” ocupa uma linha inteira. | Manter o QR como foco, mostrar o conteúdo codificado legível e usar ícone de cópia junto à URL/resultado. |
| Conversor de Arquivos | Título repetido no corpo; três colunas e área de saída vazia dominam o espaço, com textos muito pequenos. | Alternar etapas/colunas conforme a largura, compactar estado vazio e preservar conversão e entrega do arquivo como ações claras. |
| Upscale de Imagem | Título repetido; três colunas com área de saída vazia. Ao reduzir de cerca de 400×245 para 215×175 px na tela (zoom 60%), rótulos e opções ficaram truncados e muito pequenos. | Criar modo compacto de uma coluna ou etapas, preservar seleção e ação principal, mover ajustes secundários para expansão e rolar apenas o restante. |
| Relógio Mundial | O corpo repete “Relógio”; o relógio analógico recebe mais espaço que a hora digital; data/fuso ficam pequenos e chegaram ao limite inferior em zoom alto. | Priorizar hora digital e local, compactar seleção de formato/segundos e eliminar o título interno. |
| Conversor de Fuso Horário | O corpo repete o nome; há cópia, mas um aviso de incompatibilidade, atalhos de cidades e textos pequenos competem com o horário. | Destacar os horários comparados, apresentar erro recuperável perto do campo pertinente e recolher atalhos secundários. |
| Compressor de Imagens | Título repetido; três colunas, muitos presets e metadados minúsculos; saída vazia ocupa uma coluna. | Empilhar entrada, ajuste essencial e resultado quando estreito; recolher ajustes avançados e reduzir saída vazia. |
| Gravador de Áudio | Controles e siglas pequenos à distância; o visor de áudio fica vazio antes de gravar. | Tornar gravar/parar e estado de captura evidentes; nomear opções ambíguas e compactar o visor vazio. |
| Cronômetro | Número principal é reconhecível, mas o corpo repete “Cronômetro”; “Copiar resumo” ocupa espaço antes de haver voltas; tabela inferior fica muito pequena. | Manter tempo e iniciar/pausar como foco; transformar cópia em ícone junto ao tempo/resumo válido e expor voltas por rolagem. |
| Temporizador | Contagem principal é clara. Ao reduzir a instância para cerca de 188×188 px na tela (zoom 66%), presets foram truncados e “Iniciar” saiu da área visível. | Garantir contagem e iniciar/pausar no mínimo; mover presets e personalização para controle secundário compacto. |

## Regras extraídas da inspeção

1. O mínimo declarado precisa ser aprovado visualmente: resultado/estado e ação principal devem aparecer sem rolagem; não basta o contorno aceitar o tamanho.
2. O layout precisa mudar de estrutura com largura **e** altura, preservando foco e texto ao redimensionar. Reduzir tudo proporcionalmente faz os controles desaparecerem na prática.
3. O resultado principal deve receber a maior tipografia e contraste do corpo. Opções secundárias podem ser recolhidas quando a mesa está distante.
4. O cabeçalho do host já informa o nome. O corpo deve usar esse espaço para resultado e ação, sem repetir título.
5. Saídas textuais úteis precisam de um ícone persistente de copiar junto ao output atual; QR Code, Cronômetro e Fuso já mostram que cópia tem valor, mas o padrão visual deve ser menor e consistente.
6. Espaço vazio de estado inicial não deve determinar o tamanho da composição. Instrução curta e revelação do resultado quando disponível aproveitam melhor o widget.

Esta auditoria fundamenta o contrato e a matriz de aprovação no DevKit. Ela não altera os plugins instalados. Cada plugin deverá ser revisado individualmente contra a matriz quando for atualizado.
