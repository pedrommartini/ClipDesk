# ClipDesk

ClipDesk é um aplicativo desktop Windows em C# e WPF para guardar itens temporários em uma mesa visual. Ele permite colar conteúdo, arrastar arquivos e pastas, mover cards livremente, copiar de volta para a área de transferência e salvar tudo localmente.

## Experimente a versão beta

- [Baixar o ClipDesk para Windows](https://github.com/pedrommartini/ClipDesk/releases/latest/download/ClipDesk-Windows-x64.zip)
- [Conhecer o produto](https://pedrommartini.github.io/ClipDesk/)

Depois de baixar, descompacte o arquivo e abra `ClipDesk.exe`.

## Requisitos

- Windows 10 ou superior
- .NET 8 Desktop Runtime para executar a versao compilada
- .NET 8 SDK com workload Windows Desktop para compilar

O atalho `Executar ClipDesk.lnk` aponta para a versao compilada em `bin\Release\net8.0-windows\ClipDesk.exe`.

## Rodar em modo desenvolvimento

```powershell
dotnet run --project .\ClipDesk.csproj
```

## Compilar

```powershell
dotnet build .\ClipDesk.csproj -c Release
```

## Publicar executavel

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

Os dados ficam em:

```text
%LocalAppData%\ClipDesk
```

- `items.json`: dados e posicoes dos cards.
- `Assets`: imagens copiadas da área de transferência.
- `settings.json`: preferências locais, incluindo o tema.

## Estrutura para Android

Os modelos e as regras de transformação de itens ficam em `Core/ClipDesk.Core.csproj`, uma biblioteca C# sem dependências do Windows. Ela pode ser reutilizada por um futuro aplicativo Android em .NET MAUI. A interface WPF e as integrações exclusivas do Windows continuam no projeto principal.

O plano técnico detalhado está em `ARCHITECTURE.md`.

Um exemplo de dados esta em `Resources/example-data.json`.
