## Navegador padrão

Para pesquisas na web, inspeção de sites, testes de interface e qualquer tarefa
que exija um navegador, use por padrão o MCP `opera-gx-agent` e a skill
`opera-gx-browser`. Eles controlam exclusivamente o perfil isolado da IA em
`C:\Users\Pedro\AppData\Local\DSH\OperaGX-Agent-Profile`.

Não use Chrome, Edge, o Opera GX pessoal, o navegador interno do Codex ou
Computer Use como substituto silencioso. Só use outro navegador quando o
usuário pedir explicitamente ou quando `opera-gx-agent` estiver indisponível e
o usuário autorizar o fallback. Antes de abrir outra instância, consulte as
guias existentes; a ponte compartilhada já reutiliza o perfil aberto e impede
duas instâncias do mesmo perfil.

O runtime canônico é
`E:\AI\Codex\Implementação DSH\extensions\dsh-opera-gx-agent\integration\start-opera-agent-mcp.ps1`.
DSH e Codex devem continuar apontando para esse mesmo arquivo.
