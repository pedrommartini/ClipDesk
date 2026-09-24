# Critérios para promover DEV → Production Main

Use estes critérios em **todo deploy**. Uma compilação sem erros, sozinha, não valida os três modos de mesa.

1. Atualize a versão no projeto do aplicativo, no instalador, no atualizador e na landing page. Escreva as novidades em `docs/releases/v<versão>.md` e use esse texto na Release.
2. Execute as verificações do núcleo, nuvem, interface e plugins. Os testes do núcleo precisam cobrir mesas Local, PersonalCloud e Shared; os de nuvem precisam cobrir dois clientes, arquivos, convites, autorização e alterações em tempo real. Confira também que cada pacote aprovado de colaborador está no feed oficial, que seu ZIP publicado corresponde ao SHA-256 anunciado e que os plugins de arquivos funcionam com o SDK do aplicativo. Corrija qualquer falha antes da promoção.
3. Siga `docs/TESTE-PRODUCTION-SUPABASE.md` com duas contas e pastas de teste isoladas no Supabase Production. Confira convite, edição e movimento nas duas direções, plugins, anexos, saída de uma mesa compartilhada e uma mesa pessoal na nuvem. Os testes automatizados locais não substituem essa revisão interativa.
4. Gere o Dev Kit e confirme que `docs/ClipDesk-Plugin-AI-DevKit.zip` e `landing/downloads/ClipDesk-Plugin-AI-DevKit.zip` têm o mesmo SHA-256. Inspecione a seção de plugins e as novidades da landing page.
5. Compile o instalador com `-Production`. Extraia com `--test-install` em uma pasta temporária isolada, confirme o executável, os plugins incluídos e o destino padrão `%ProgramFiles%\ClipDesk`. Não use a instalação de uso diário para validar o pacote.
6. Promova o commit aprovado para `main`, publique a Release com o instalador e ZIP correspondentes, e confira os links e a landing page publicada. Anuncie as novidades e qualquer limitação verificada.

Comandos das verificações automatizadas:

```powershell
dotnet run --project .\Tests\ClipDesk.Core.Checks.csproj -c Release
dotnet run --project .\Tests\Cloud\ClipDesk.Cloud.Checks.csproj -c Release
dotnet run --project .\Tests\Visual\ClipDesk.Visual.Checks.csproj -c Release
dotnet run --project .\Tests\Installer\ClipDesk.Installer.Checks.csproj -c Release
dotnet run --project .\Tests\Visual\ClipDesk.Visual.Checks.csproj -c Release -- --plugin-store
dotnet run --project .\Tests\Visual\ClipDesk.Visual.Checks.csproj -c Release -- --plugin-delivery
dotnet run --project .\Tests\Visual\ClipDesk.Visual.Checks.csproj -c Release -- --plugin-board-files
dotnet run --project .\Tests\Visual\ClipDesk.Visual.Checks.csproj -c Release -- --plugin-v2
dotnet run --project .\Tests\Visual\ClipDesk.Visual.Checks.csproj -c Release -- --collaborator-plugins
.\PluginPackages\build-ai-devkit.ps1
.\Installer\build-installer.ps1 -Production
```
