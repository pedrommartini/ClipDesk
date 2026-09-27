# ClipDesk Plugin AI DevKit v2.1

Versão do pacote: **2.1.0**  
Data: **27/09/2026**  
Host recomendado: **ClipDesk 0.4.4 ou superior**

## Novidades desta versão

- Estado compartilhável de plugins para mesas colaborativas.
- Anexos compartilhados por ID gerenciado pela mesa, sem caminhos locais no estado.
- Orientação para atualizar renderizadores quando o estado chega de outro participante.
- Diagnósticos locais de carregamento com código estável, ID do incidente e ações de recuperação.
- Validação de manifesto, capabilities, permissões e versões do contrato.

## Compatibilidade

As APIs Windows v1 e v2 existentes continuam suportadas. A versão 2.1 não torna campos privados de renderizadores antigos colaborativos automaticamente: plugins que precisam compartilhar configurações, entradas, resultados ou arquivos devem usar o estado e as APIs de anexos deste DevKit.
