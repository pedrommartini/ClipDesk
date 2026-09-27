# Revisão visual do plugin Relógio Mundial

Ficha de validação visual para o plugin ClipDesk Relógio Mundial (clipdesk.clock) conforme requisitos do DevKit v2.

| Cenário | Instância (largura×altura) | Zoom | Janela | Tema/destaque | Resultado observado e correção | Aprovado? |
| --- | --- | --- | --- | --- | --- | --- |
| Mínimo quadrado | 240×240 | 100% | ampla | escuro | Mostrador do relógio analógico/digital e ações essenciais visíveis sem corte; cidades rolam em ScrollViewer. | Sim |
| Padrão | 340×300 | 100% | ampla | claro | Mostrador principal e lista de capitais mundiais exibidos confortavelmente sem rolagem externa. | Sim |
| Estreito e alto | 220×360 | 60% | ampla | escuro | Layout reorganiza com espaçamento compacto; cidades expandem verticalmente com cartões alinhados. | Sim |
| Largo e baixo | 440×220 | 60% | ampla | claro | Modo analógico e digital mantêm proporções; botões de alternância quebram suavemente sem corte. | Sim |
| Mínimo e padrão à distância | 240×240 / 340×300 | 40% | ampla | escuro | Ponteiros do mostrador analógico e dígitos em destaque mantêm alto contraste legível à distância. | Sim |
| Janela estreita | 340×300 | 60% | ~1024×768 | claro | Instância opera responsivamente nos limites da área visível da mesa sem transbordar controles. | Sim |
| Tema e destaque alternativos | 340×300 | 100% | ampla | claro/escuro | Cores de fundo, indicadores dia/noite (sol/lua) e ponteiro de destaque #10B981 com excelente contraste. | Sim |
| Output vazio, válido, longo e erro; cópia | 340×300 | 100% | ampla | ambos | Ícone acessível (\uE8C8) copia o resumo de fusos formatado pelo Core (GetClipboardText); desativado se nulo. | Sim |

## Hierarquia e controles

- Resultado ou tarefa principal visível sem rolagem no tamanho padrão: Sim, mostrador horário e cidades mundiais visíveis na abertura.
- Resultado/estado e ação principal visíveis no mínimo: Sim, relógio principal e botões de modo visíveis no modo 240×240 quadrado.
- Título interno duplicado removido: Sim, cabeçalho interno "Relógio" removido; o título oficial fica a cargo do cabeçalho da instância do host.
- Controles secundários movidos, simplificados ou removidos: Ações de visualização organizadas em barra compacta unificada no topo.
- Conteúdo que o ícone copia e comportamento sem resultado: Ícone posicionado junto ao horário principal copia o resumo completo de cidades mundiais.

## Limitações observadas

Nenhuma limitação impeditiva detectada.
