# Revisão visual do plugin Gerador de QR Code

Ficha de validação visual para o plugin ClipDesk Gerador de QR Code (clipdesk.qrcode) conforme requisitos do DevKit v2.

| Cenário | Instância (largura×altura) | Zoom | Janela | Tema/destaque | Resultado observado e correção | Aprovado? |
| --- | --- | --- | --- | --- | --- | --- |
| Mínimo quadrado | 240×240 | 100% | ampla | escuro | Card de QR code ajustado, input de texto com botão de cópia e botões de ação visíveis sem rolagem. | Sim |
| Padrão | 300×340 | 100% | ampla | claro | Imagem QR em alta definição, badge de tipo e painel expansível de ECC integrados harmoniosamente. | Sim |
| Estreito e alto | 240×400 | 60% | ampla | escuro | QR code centralizado com proporções corretas e área de texto perfeitamente acessível. | Sim |
| Largo e baixo | 460×240 | 60% | ampla | claro | Placa QR e controles horizontais com boa distribuição e sem transbordamento. | Sim |
| Mínimo e padrão à distância | 240×240 / 300×340 | 40% | ampla | escuro | Contraste da matriz QR branca e preta nítido mesmo em zoom reduzido. | Sim |
| Janela estreita | 300×340 | 60% | ~1024×768 | claro | Elementos contidos no painel sem sobrepor bordas da aplicação. | Sim |
| Tema e destaque alternativos | 300×340 | 100% | ampla | claro/escuro | Cores de destaque #10B981 nos badges e botões com alto contraste e legibilidade. | Sim |
| Output vazio, válido, longo e erro; cópia | 300×340 | 100% | ampla | ambos | Ícone \uE8C8 copia texto/URL contido no QR code; desabilitado quando vazio. | Sim |

## Hierarquia e controles

- Resultado ou tarefa principal visível sem rolagem no tamanho padrão: Sim, matriz QR code gerada em tempo real com input para edição imediata.
- Resultado/estado e ação principal visíveis no mínimo: Sim, QR code, caixa de texto com ícone de cópia e ações principais cabem sem rolagem em 240×240.
- Título interno duplicado removido: Sim, sem cabeçalhos duplicando o nome do módulo; apenas badge contextual de tipo (Link/Texto).
- Controles secundários movidos, simplificados ou removidos: Ícone de cópia acessível integrado à barra de entrada e opções avançadas recolhidas por padrão.
- Conteúdo que o ícone copia e comportamento sem resultado: Copia o link ou texto bruto codificado no QR code; desabilitado quando campo vazio.

## Limitações observadas

Nenhuma limitação impeditiva detectada.
