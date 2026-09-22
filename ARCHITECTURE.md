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

A implementação DEV de setembro de 2026 usa identificadores estáveis, versões, exclusão lógica, fila SQLite e mesclagem por campo. Conflitos no mesmo campo ficam preservados para revisão. Mesas escolhem local, nuvem pessoal ou compartilhada; o histórico é particular por conta.

DEV e Production usam Supabase diretamente: autenticação Google de identidade, Postgres protegido por RLS e Realtime para mesas, elementos, convites e presença. Os dados locais e a identidade da instalação são separados por ambiente. O servidor ASP.NET Core permanece apenas para testes históricos automatizados; o aplicativo e os instaladores não dependem dele. Veja [a arquitetura Supabase](docs/SUPABASE-PRODUCTION.md) e [o registro do antigo ambiente de desenvolvimento](docs/CLOUD-DEVELOPMENT.md).

## Próximas etapas

1. Extrair persistência e comandos de itens do `MainWindow.xaml.cs` para serviços e modelos de apresentação.
2. Evoluir a sincronização já implementada para um log incremental paginado, reduzindo consultas completas em contas grandes.
3. Criar um protótipo Android em .NET MAUI que consuma `ClipDesk.Core` e receba conteúdo pelo compartilhamento do Android.
4. Homologar renovação de tokens, permissões do Drive e uploads interrompidos com contas reais e PostgreSQL.
5. Medir carga prolongada, definir retenção e exclusão de conta na infraestrutura antes de produção.

## Pontos técnicos observados

- `MainWindow.xaml.cs` ainda concentra muitas responsabilidades e deve ser dividido de forma incremental.
- O histórico persiste localmente e pode sincronizar por conta, separado das permissões das mesas. Retenção e exclusão completa na infraestrutura ainda precisam de definição para produção.
- O reconhecimento de imagens usa ONNX local e está acoplado a tipos WPF; uma abstração de imagem será necessária antes de compartilhá-lo com Android.
- `FolderWindow` parece ser uma implementação anterior ao overlay atual e pode ser removido depois de confirmar que nenhuma jornada externa ainda o utiliza.
- A revisão de 14/09/2026 habilitou virtualização do histórico agrupado, animação sem redimensionamento por quadro, gravações sem efeito evitadas, consultas por ID e proteção contra rajadas HTTP. As decisões, testes e limites estão em [desempenho e instaladores](docs/PERFORMANCE-INSTALLER.md).
