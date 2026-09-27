# Revisão visual do plugin Gravador de Áudio

Ficha de validação visual para o plugin ClipDesk Gravador de Áudio (clipdesk.audiorecorder) conforme requisitos do DevKit v2.

| Cenário | Instância (largura×altura) | Zoom | Janela | Tema/destaque | Resultado observado e correção | Aprovado? |
| --- | --- | --- | --- | --- | --- | --- |
| Mínimo quadrado | 160×160 | 100% | ampla | escuro | Layout adapta para pilha vertical: monitor OLED, botões de transporte e toggles de entrada acessíveis sem rolagem. | Sim |
| Padrão | 540×180 | 100% | ampla | claro | Disposição horizontal em 3 seções elegantes: controles de microfone/sistema, transporte e monitor FFT com cronômetro. | Sim |
| Estreito e alto | 200×320 | 60% | ampla | escuro | Monitor superior e botões de gravação mantêm excelente proporção e legibilidade. | Sim |
| Largo e baixo | 540×160 | 60% | ampla | claro | Formato panorâmico compacto mantém sliders e waveform fluidos. | Sim |
| Mínimo e padrão à distância | 160×160 / 540×180 | 40% | ampla | escuro | Ponto de gravação vermelho piscante e visor OLED de alta visibilidade à distância. | Sim |
| Janela estreita | 540×180 | 60% | ~1024×768 | claro | Transição suave para layout compacto quando espaço horizontal se reduz. | Sim |
| Tema e destaque alternativos | 540×180 | 100% | ampla | claro/escuro | Visual OLED escuro com gradientes de waveform e destaque vermelho vibrante #EF4444. | Sim |
| Output vazio, válido, longo e erro; cópia | 540×180 | 100% | ampla | ambos | Ícone \uE8C8 copia caminho do arquivo gerado; habilitado após término e confirmação da gravação. | Sim |

## Hierarquia e controles

- Resultado ou tarefa principal visível sem rolagem no tamanho padrão: Sim, monitor com waveform em tempo real, cronômetro e controles de gravação imediatos.
- Resultado/estado e ação principal visíveis no mínimo: Sim, no modo 160×160 o layout empilha verticalmente, mantendo o botão Gravar e o visor OLED visíveis.
- Título interno duplicado removido: Sim, sem títulos internos repetindo o nome do módulo; foco total nas fontes e controles.
- Controles secundários movidos, simplificados ou removidos: Sliders de volume ocultados em layout compacto mantendo toggles de ativação; ícone acessível de cópia no cabeçalho do monitor.
- Conteúdo que o ícone copia e comportamento sem resultado: Copia o caminho absoluto do último arquivo de áudio gravado e confirmado; desabilitado quando não há gravação recente.

## Limitações observadas

Nenhuma limitação impeditiva detectada.
