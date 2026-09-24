# ClipDesk

ClipDesk é um aplicativo desktop Windows em C# e WPF para guardar itens temporários em uma mesa visual. Ele permite colar conteúdo, arrastar arquivos e pastas, mover cards livremente, copiar de volta para a área de transferência e salvar tudo localmente.

## Experimente a versão beta

- [Baixar o instalador do ClipDesk para Windows](https://github.com/pedrommartini/ClipDesk/releases/download/v0.4.1/ClipDesk-Setup.exe)
- [Conhecer o produto](https://clipdesk.pages.dev/)

Depois de baixar, abra `ClipDesk-Setup.exe` e siga o instalador. Somente o pacote ZIP precisa ser descompactado.
O instalador Production usa `%ProgramFiles%\ClipDesk` como destino padrão e solicita a confirmação de administrador do Windows. Uma instalação anterior em `%LocalAppData%\Programs\ClipDesk` é reconhecida e migrada ao atualizar, preservando mesas, histórico e preferências por padrão.

## Requisitos

- Windows 10 ou superior
- .NET 8 Desktop Runtime para executar uma compilação não autossuficiente; o instalador já inclui o runtime
- .NET 8 SDK com workload Windows Desktop para compilar

O atalho `Executar ClipDesk.lnk` aponta para a versao compilada em `bin\Release\net8.0-windows\ClipDesk.exe`.

## Rodar em modo desenvolvimento

O ambiente padrão é **Development**, com dados separados em `%LocalAppData%\ClipDesk-Dev`. Release é a configuração de compilação; não altera esse isolamento. DEV e Production usam o mesmo projeto Supabase, com perfis e instalações locais separados. Veja a [arquitetura Supabase](docs/SUPABASE-PRODUCTION.md). O [documento do antigo servidor DEV](docs/CLOUD-DEVELOPMENT.md) foi mantido como registro histórico.

Para compilar e abrir o aplicativo DEV:

```powershell
.\scripts\Start-ClipDeskDev.ps1 -OpenApp
```

Para trabalhar apenas com o aplicativo local:

```powershell
dotnet run --project .\ClipDesk.csproj
```

## Compilar

```powershell
dotnet build .\ClipDesk.csproj -c Release
```

## Publicar executavel

O comando abaixo gera a versão de desenvolvimento. Distribuição para usuários exige autorização e `-p:ClipDeskEnvironment=Production`; não substitua manualmente a instalação de uso diário.

```powershell
dotnet publish .\ClipDesk.csproj -c Release -r win-x64 --self-contained false
```

## Como usar

- `Ctrl + V`: adiciona texto, imagem, link ou arquivos da área de transferência.
- `Ctrl + Z`: desfaz a última alteração visual da área.
- `Ctrl + Y`: refaz a alteração desfeita.
- Arraste arquivos ou pastas do Explorer para dentro do ClipDesk.
- Clique em um card para copiar o conteúdo de volta para a área de transferência.
- Clique duas vezes para abrir preview ou abrir arquivo/link no app padrao.
- Clique duas vezes no titulo de um card para renomear diretamente.
- Arraste um card para a lixeira para excluir.
- Clique duas vezes na lixeira para resetar a área de trabalho depois da confirmação.
- Arraste um card sobre outro para criar uma pasta do ClipDesk.
- Arraste um card sobre uma pasta do ClipDesk para colocar o item dentro dela.
- As pastas aparecem fechadas com miniaturas internas em grade, no estilo iOS.
- Clique duas vezes em uma pasta do ClipDesk para abrir um overlay estilo iOS; arraste um item para fora do painel para tira-lo da pasta.
- Botao direito mostra uma barra minimalista com icones de copiar, duplicar, detalhes e excluir.
- Imagens recebem um titulo automatico local por reconhecimento visual, como `Paisagem 1`, `Carro 1` ou `Documento 1`.
- A barra superior esquerda tem desfazer, refazer, histórico do clipboard, histórico compacto e modo claro/escuro.
- O histórico abre ao lado da mesa, pode ser redimensionado e permite buscar, copiar ou arrastar um item para a mesa.
- O ícone do ClipDesk na bandeja do Windows abre uma janela compacta do histórico. O atalho `Ctrl + Shift + H` abre a mesma janela.
- Crie e renomeie várias mesas para separar clientes, projetos ou contextos de trabalho.
- Ative a inicialização discreta com o Windows para deixar o histórico sempre disponível pela bandeja.
- O tema claro ou escuro escolhido é restaurado na próxima execução.
- O ClipDesk verifica atualizações na inicialização, mostra um aviso quando há uma nova release e instala tudo com um clique, reiniciando já atualizado.

## Persistencia local

Os dados ficam separados por edição:

```text
%LocalAppData%\ClipDesk-Dev   (desenvolvimento)
%LocalAppData%\ClipDesk       (uso diário)
```

- `Profiles\<perfil>\clipdesk.db`: mesas, posições, histórico, preferências e fila de sincronização em SQLite. O perfil sem login é `local`.
- `Profiles\<perfil>\Assets`: imagens e anexos locais gerenciados.
- Os plugins opcionais escolhidos pelo usuário ficam em `Documentos\Clipdesk` (DEV: `Documentos\Clipdesk\Dev`). Os quatro plugins incluídos ficam junto ao programa; atualizações deles usam um cache privado em `%LocalAppData%\ClipDesk[-Dev]\Plugins`.
- JSON de versões anteriores são importados na primeira leitura e preservados como origem da migração.
- Tokens de conta são protegidos pelo Windows. Arquivos originais externos e downloads do usuário não pertencem à pasta privada do app.

Ao substituir uma instalação, **Preservar mesas, histórico e preferências** vem marcado. Desmarcar faz uma instalação local limpa da edição escolhida. Na desinstalação, **Apagar mesas, histórico e caches locais** é opcional e começa desmarcado. Nenhuma dessas opções apaga dados da conta na nuvem; entrar novamente pode recuperar mesas sincronizadas.

Veja [instaladores e validação de desempenho](docs/PERFORMANCE-INSTALLER.md) para escopo da limpeza, comandos e limitações.

## Estrutura para Android

Os modelos e as regras de transformação de itens ficam em `Core/ClipDesk.Core.csproj`, uma biblioteca C# sem dependências do Windows. Ela pode ser reutilizada por um futuro aplicativo Android em .NET MAUI. A interface WPF e as integrações exclusivas do Windows continuam no projeto principal.

O plano técnico detalhado está em `ARCHITECTURE.md`.

Para desenvolver, testar e publicar plugins sem atualizar o executável, consulte o [guia de plugins e identidade visual](docs/GUIA-DE-PLUGINS.md).

Para desenvolver plugins com outro modelo de IA sem expor ou analisar a codebase inteira, use o [Plugin AI DevKit autossuficiente](docs/PLUGIN-AI-DEVKIT.md) junto com `docs/ClipDesk-Plugin-AI-DevKit.zip`.

A branch `clipdesk-new` é um laboratório de migração e não substitui o produto principal. O plano, as decisões e o status de cada etapa ficam em [ClipDesk New — plano e status da migração](docs/CLIPDESK-NEW-MIGRATION.md).

Um exemplo de dados esta em `Resources/example-data.json`.
