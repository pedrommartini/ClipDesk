# Revisão visual do plugin

| Cenário | Instância (largura×altura) | Zoom | Janela | Tema/destaque | Resultado observado e correção | Aprovado? |
| --- | --- | --- | --- | --- | --- | --- |
| Mínimo quadrado | 240×240 | 100% | ampla | escuro | Entrada e controles principais visíveis; sem scroll horizontal | Sim |
| Padrão | 360×300 | 100% | ampla | claro | Layout espaçoso; player, seleção de voz e ações acessíveis sem corte | Sim |
| Estreito e alto | 240×420 | 60% | ampla | escuro | Disposição vertical limpa; controles secundários rolam confortavelmente | Sim |
| Largo e baixo | 460×240 | 60% | ampla | claro | Organização em colunas responsivas; ações principais ao alcance | Sim |
| Mínimo e padrão à distância | 240×240 e 360×300 | 40% | ampla | escuro | Textos e estado do player legíveis; botões clicáveis | Sim |
| Janela estreita | 360×300 | 60% | ~1024×768 | claro | Popups de dropdown e player operam perfeitamente sem corte de tela | Sim |
| Tema e destaque alternativos | 360×300 | 100% | ampla | claro/escuro | Contraste de texto e botões com cor de destaque atendem acessibilidade | Sim |
| Output vazio, válido, longo e erro; cópia | 360×300 | 100% | ampla | ambos | Ícone copia texto gerado; desabilitado em vazio/erro; áudio longo toca fluido | Sim |

## Hierarquia e controles

- Resultado ou tarefa principal visível sem rolagem no tamanho padrão: Sim, caixa de texto, seleção de voz, player e botão Falar visíveis sem rolagem.
- Resultado/estado e ação principal visíveis no mínimo: Sim, no modo 240×240 a entrada de texto e a ação Falar permanecem no topo e na base sem corte.
- Título interno duplicado removido: Sim, nenhum título do plugin é renderizado no corpo do widget.
- Controles secundários movidos, simplificados ou removidos: Sliders de volume e velocidade integrados em barra compacta.
- Conteúdo que o ícone copia e comportamento sem resultado: O ícone copia o texto falado do GetClipboardText; fica desabilitado quando vazio.

## Limitações observadas
- A síntese neural requer conectividade com a internet; na ausência de rede, o fallback local SAPI assume automaticamente a geração em formato WAV.
