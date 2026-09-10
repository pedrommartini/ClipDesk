# Arquitetura e caminho para Android

## Decisão

Continuar o produto atual e evoluir para dois aplicativos com um núcleo compartilhado:

- `ClipDesk` mantém a interface WPF e as integrações nativas do Windows, como histórico em segundo plano e ícone da bandeja.
- `ClipDesk.Android` deve ser criado em .NET MAUI quando a experiência móvel e a sincronização entrarem no escopo de implementação.
- `ClipDesk.Core` contém modelos, regras de conteúdo, serialização e, gradualmente, operações de sincronização independentes da plataforma.

Reescrever o desktop agora desperdiçaria os fluxos nativos já funcionais e aumentaria o risco sem resolver uma necessidade do usuário. WPF é exclusivo do Windows, então compartilhar a interface com Android exigiria uma migração ampla. O ganho real vem de compartilhar dados e regras, mantendo uma interface própria para cada formato de tela.

## Limitação do Android

Android 10 ou mais recente restringe a leitura do clipboard quando o aplicativo não está em foco, exceto para o teclado padrão. Portanto, o aplicativo Android não deve prometer um histórico global em segundo plano igual ao Windows.

A entrada móvel deve usar:

1. “Compartilhar com ClipDesk” para texto, links, imagens e documentos.
2. Colar dentro do aplicativo quando ele estiver em foco.
3. Sincronização dos itens que o usuário adicionou ao ClipDesk.

## Sincronização recomendada

Cada item sincronizado deve ter identificador estável, versão, data de atualização, exclusão lógica e referência aos arquivos anexos. O servidor guarda metadados em um banco e imagens/arquivos em armazenamento de objetos. O cliente mantém uma fila local para funcionar sem internet.

O primeiro recorte deve sincronizar textos, links e imagens. Arquivos e pastas do Windows precisam de uma decisão de produto: enviar o arquivo completo, manter somente uma referência local indisponível no Android ou permitir que o usuário escolha por item.

Conflitos podem começar com “última alteração vence”, mantendo versões anteriores por um período curto. Antes de produção, a autenticação, criptografia em trânsito, exclusão de conta e política de retenção precisam ser definidas.

## Próximas etapas

1. Extrair persistência e comandos de itens do `MainWindow.xaml.cs` para serviços e modelos de apresentação.
2. Definir o contrato de sincronização e os estados `LocalOnly`, `Pending`, `Synced`, `Conflict` e `Deleted`.
3. Criar um protótipo Android em .NET MAUI que consuma `ClipDesk.Core` e receba conteúdo pelo compartilhamento do Android.
4. Implementar autenticação e sincronização primeiro com texto e links.
5. Adicionar imagens e testar conflitos, uso offline e múltiplos dispositivos.

## Pontos técnicos observados

- `MainWindow.xaml.cs` ainda concentra muitas responsabilidades e deve ser dividido de forma incremental.
- O histórico atual existe durante a sessão; persistir e sincronizar o histórico bruto exige uma decisão explícita de privacidade e retenção.
- O reconhecimento de imagens usa ONNX local e está acoplado a tipos WPF; uma abstração de imagem será necessária antes de compartilhá-lo com Android.
- `FolderWindow` parece ser uma implementação anterior ao overlay atual e pode ser removido depois de confirmar que nenhuma jornada externa ainda o utiliza.
