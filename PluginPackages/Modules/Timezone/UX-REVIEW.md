# Revisão visual do plugin Conversor de Fuso Horário

Ficha de validação visual para o plugin ClipDesk Conversor de Fuso Horário (clipdesk.timezone) conforme requisitos do DevKit v2.

| Cenário | Instância (largura×altura) | Zoom | Janela | Tema/destaque | Resultado observado e correção | Aprovado? |
| --- | --- | --- | --- | --- | --- | --- |
| Mínimo quadrado | 240×240 | 100% | ampla | escuro | Topbar com botão Agora, status de reunião e controle 24h visíveis; cards de cidades roláveis no ScrollViewer sem quebrar layout. | Sim |
| Padrão | 360×320 | 100% | ampla | claro | Slider de 24h com marcadores, chips de fusos sugeridos e horários comerciais/noturnos legíveis. | Sim |
| Estreito e alto | 240×400 | 60% | ampla | escuro | Lista vertical de cidades expandida, badges de dia (+1d) e viabilidade de reunião legíveis. | Sim |
| Largo e baixo | 460×240 | 60% | ampla | claro | Slider e cards organizados com boa densidade de informação sem overflow. | Sim |
| Mínimo e padrão à distância | 240×240 / 360×320 | 40% | ampla | escuro | Cores semânticas de viabilidade (verde/laranja/vermelho) discerníveis à distância. | Sim |
| Janela estreita | 360×320 | 60% | ~1024×768 | claro | Renderização fluída sem conflito com as bordas da janela. | Sim |
| Tema e destaque alternativos | 360×320 | 100% | ampla | claro/escuro | Cores de status e destaque #8B5CF6 aplicados com contraste legível em ambos os temas. | Sim |
| Output vazio, válido, longo e erro; cópia | 360×320 | 100% | ampla | ambos | Ícone \uE8C8 copia texto da proposta de reunião estruturada com horários locais das cidades. | Sim |

## Hierarquia e controles

- Resultado ou tarefa principal visível sem rolagem no tamanho padrão: Sim, slider de horário, status de viabilidade da reunião e lista de fusos horários sincronizados.
- Resultado/estado e ação principal visíveis no mínimo: Sim, botão Agora, controle 24h e cards de fusos visíveis sem cortes em 240×240.
- Título interno duplicado removido: Sim, título interno "Fuso Horário & Reuniões" removido.
- Controles secundários movidos, simplificados ou removidos: Ícone de cópia acessível integrado à barra de viabilidade de reunião.
- Conteúdo que o ícone copia e comportamento sem resultado: Copia proposta de reunião com o horário convertido para cada uma das cidades configuradas.

## Limitações observadas

Nenhuma limitação impeditiva detectada.
