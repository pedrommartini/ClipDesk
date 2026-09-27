# Revisão visual do plugin Upscale de Imagem

Ficha de validação visual para o plugin ClipDesk Upscale de Imagem (clipdesk.imageupscaler) conforme requisitos do DevKit v2.

| Cenário | Instância (largura×altura) | Zoom | Janela | Tema/destaque | Resultado observado e correção | Aprovado? |
| --- | --- | --- | --- | --- | --- | --- |
| Mínimo quadrado | 240×240 | 100% | ampla | escuro | Layout empilha verticalmente as 3 zonas: dropzone de entrada, controles de escala e botão de upscale acessíveis. | Sim |
| Padrão | 560×340 | 100% | ampla | claro | Disposição horizontal em 3 colunas (Entrada, Opções, Saída) com dropzone espaçosa, seletores de escala e comparador antes/depois. | Sim |
| Estreito e alto | 240×440 | 60% | ampla | escuro | Seções empilhadas verticais com excelente legibilidade e sem clipping nos botões de alternância e botão primário. | Sim |
| Largo e baixo | 560×240 | 60% | ampla | claro | Disposição panorâmica compacta mantém colunas acessíveis sem transbordamento. | Sim |
| Mínimo e padrão à distância | 240×240 / 560×340 | 40% | ampla | escuro | Indicador de projeção, botões de fator e botão de ampliação legíveis com contraste mesmo reduzidos. | Sim |
| Janela estreita | 560×340 | 60% | ~1024×768 | claro | Transição fluida de 3 colunas para lista empilhada em ScrollViewer vertical sem rolagem horizontal. | Sim |
| Tema e destaque alternativos | 560×340 | 100% | ampla | claro/escuro | Cores de destaque #8B5CF6 aplicadas com contraste legível nos botões de ação e status. | Sim |
| Output vazio, válido, longo e erro; cópia | 560×340 | 100% | ampla | ambos | Ícone \uE8C8 copia caminho da imagem ampliada ao lado da confirmação; fallback para resumo estatístico. | Sim |

## Hierarquia e controles

- Resultado ou tarefa principal visível sem rolagem no tamanho padrão: Sim, dropzone de entrada, seletores de fator/perfil e botão de ampliação visíveis sem rolagem em 560×340.
- Resultado/estado e ação principal visíveis no mínimo: Sim, no modo 240×240 o layout empilha e rola verticalmente, mantendo botão de upscale e entrada operacionais.
- Título interno duplicado removido: Sim, título redundante "Upscale de Imagem AI" removido da barra superior; apenas estatísticas compactas exibidas.
- Controles secundários movidos, simplificados ou removidos: Ícone de cópia acessível integrado ao card de resultado; layout adaptativo entre 3 colunas e modo vertical.
- Conteúdo que o ícone copia e comportamento sem resultado: Copia o caminho da imagem ampliada gerada ou resumo estatístico via GetClipboardText.

## Limitações observadas

Nenhuma limitação impeditiva detectada.
