# Fluxo de entrega

## Local antes de remoto

Toda alteração do ClipDesk deve seguir esta ordem:

1. Implementar e validar no computador local, usando a cópia em `C:\Users\Pedro\Documents\ClipDesk`.
2. Compilar a versão de Release local e atualizar `bin\Release\net8.0-windows\ClipDesk.exe`, que é o destino do atalho `Executar ClipDesk.lnk`.
3. Só criar commit, enviar mudanças ou publicar uma release no GitHub após uma autorização explícita do usuário para essa etapa remota.

Não inferir autorização para GitHub a partir de um pedido de implementação ou instalação local.

## Interface e navegação

Evitar menus, dropdowns e fluxos de navegação com aparência padrão do Windows. Sempre preferir componentes construídos para o ClipDesk, com transparência, animações suaves e linguagem visual consistente com o restante da interface.
