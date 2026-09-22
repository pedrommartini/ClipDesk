# Fluidez e instaladores — revisão de 15/09/2026

## Escopo e resultados

Revisão local dos fluxos de histórico, mesa/plugins, persistência, sincronização, anexos, prévias e instalação. Nenhuma publicação remota ou modificação da instalação diária foi realizada. Não se trata de uma certificação de ausência de bugs ou benchmark de todas as combinações de hardware.

### Histórico e interface

- A abertura do histórico aplica sua largura final uma vez e anima somente deslocamento/opacity. Antes, animava a largura da coluna, recalculando o layout da mesa a cada quadro. Fechar aplica a largura zero ao terminar o fade; uma geração cancela conclusões de animações antigas em cliques rápidos.
- A lista recicla os componentes visuais também quando agrupada por data. No teste WPF de 1.000 entradas a 410 × 700 pixels, 7 entradas foram realizadas inicialmente. Esse teste de estresse não muda o limite normal de retenção do histórico.
- Busca com espera de 160 ms evita recalcular o filtro a cada tecla. Miniaturas decodificadas em 96 pixels usam cache limitado a 128 imagens; a primeira leitura continua local/síncrona.
- A sincronização só reconstrói o histórico quando há mudança real; uma edição local durante uma requisição é enfileirada antes de aplicar a apresentação remota.
- Plugins reutilizam a rotação e recalculam sua tipografia/layout responsivo apenas quando o tamanho ou conteúdo muda, não a cada deslocamento. A escala interna acompanha ampliações de até 10×, incluindo títulos, conteúdo, teclas e ação de edição, para continuar legível quando a mesa estiver afastada.
- Cursores remotos reutilizam seus elementos visuais, interpolam os pontos recebidos localmente e compensam o zoom para manter tamanho legível; sua expiração continua independente do intervalo de consulta da nuvem.
- A câmera acompanhada mantém um alvo contínuo e usa interpolação exponencial por quadro. Novas mensagens atualizam apenas esse alvo, evitando reiniciar curvas de aceleração e reduzindo a sensação de atraso sem ampliar o tráfego.
- O alinhamento magnético calcula os candidatos uma vez no início do arrasto, encaixa bordas/centros dentro de uma tolerância proporcional ao zoom e reutiliza duas linhas de guia, sem criar elementos visuais a cada movimento.
- Salvamentos intermediários de movimento criativo foram espaçados para 300 ms; soltar o objeto ainda salva imediatamente.
- Pré-visualizações PDF têm cache limitado e no máximo duas renderizações simultâneas. O modelo ONNX só é carregado quando uma imagem realmente precisa de classificação; texto/arquivos não carregam a sessão do modelo na abertura do app.

### Disco, rede e sincronização

- SQLite continua com WAL e confirmação durável. Gravar um documento ou estado idêntico não atualiza novamente a linha. Os JSON persistidos são compactos.
- Aceitar uma entidade busca somente seu ID, em vez de reler todos os registros e todas as operações para cada item recebido. Uma versão já recebida sem diferenças não provoca escrita.
- Registro de anexos é reutilizado durante a sessão; arquivos já enviados e disponíveis não têm seu estado consultado repetidamente. Anexos incompletos continuam sendo verificados. Permissões do Drive são reconciliadas no máximo uma vez por minuto, com atualização forçada após upload manual.
- Conferência de recuperação: 30 s sem canal de tempo real; 60 s com canal conectado. Alterações gerais continuam solicitando sincronização com debounce de 450 ms; plugins e objetos criativos usam envio direcionado agrupado por 180 ms e não aguardam uma consulta completa. Convites acionam sua própria notificação em tempo real, sem transformar atualizações de presença em consultas extras. Não há ticks periódicos sobrepostos. Consulta de membros é limitada a uma por minuto e a busca periódica de convites a uma a cada 30 s.
- HTTP 429/503 respeita `Retry-After`, com pequena dispersão aleatória; sem cabeçalho, há espera exponencial limitada. Durante essa pausa, novas requisições ao mesmo serviço são recusadas localmente. Nenhuma operação de escrita é repetida automaticamente pelo transporte. A fila SQLite permanece para uma próxima tentativa.
- O limite do servidor é de 600 requisições/minuto por usuário autenticado, e por IP para chamadas anônimas. Assim, clientes diferentes atrás do mesmo túnel não dividem indevidamente uma cota autenticada. Respostas de limite incluem `Retry-After`.

Não foi medido um percentual universal de economia de CPU/RAM. Sincronização ainda consulta o conjunto autorizado completo; paginação incremental, carga prolongada e testes reais em hardware modesto seguem como evolução. Serviços de tradução/câmbio mantêm seus próprios limites; tratamento de 429 não elimina quotas externas.

## Regras dos instaladores

As regras são compartilhadas pelo código das edições ClipDesk e ClipDesk DEV. Nesta entrega foi gerado somente o pacote **Development**.

| Ação | Opção inicial | Efeito |
| --- | --- | --- |
| Substituir/reinstalar | “Preservar mesas, histórico e preferências” marcada | Atualiza os binários, sem apagar a pasta privada. |
| Substituir com opção desmarcada | Instalação local limpa | Remove os dados privados de todos os perfis da edição, incluindo contas salvas, configurações e caches. |
| Desinstalar | “Apagar mesas, histórico e caches locais” desmarcada | Remove aplicativo, atalhos e registro de inicialização; mantém dados para reinstalação. |
| Desinstalar com opção marcada | Limpeza local completa da edição | Remove também a pasta privada dessa edição. |

As pastas privadas são `%LocalAppData%\ClipDesk-Dev` e `%LocalAppData%\ClipDesk`. A opção não remove a outra edição, a cópia do código-fonte, documentos originais, downloads exportados em Documentos nem conteúdo do servidor/Google Drive. Ao entrar novamente, conteúdo sincronizado pode retornar. Portanto, “instalação limpa” é local, não exclusão de conta na nuvem.

Feche a edição escolhida, inclusive na bandeja, antes de limpar seus dados. A proteção usa o mutex da edição, incluindo a cópia de desenvolvimento. Antes de confirmar a instalação, os dados antigos são movidos para um backup irmão temporário: falhas nessa fase restauram a pasta. Após começar a exclusão definitiva, um eventual erro informa a localização remanescente; não restaura silenciosamente um banco parcialmente apagado. O novo executável não é removido por uma falha na limpeza final.

O instalador não aceita usar a pasta de dados como destino de binários. Raízes amplas e a pasta de dados de outra edição são recusadas pela rotina de limpeza. O teste de pacote nunca pode limpar dados pessoais.

## Compilar e verificar

```powershell
dotnet build .\ClipDesk.csproj -c Release -p:ClipDeskEnvironment=Development
dotnet run --project .\Tests\ClipDesk.Core.Checks.csproj -c Release
dotnet run --project .\Tests\Cloud\ClipDesk.Cloud.Checks.csproj -c Release
dotnet run --project .\Tests\Visual\ClipDesk.Visual.Checks.csproj -c Release
dotnet run --project .\Tests\Installer\ClipDesk.Installer.Checks.csproj -c Release
.\scripts\Start-ClipDeskDev.ps1 -SkipBuild -OpenApp
.\Installer\build-installer.ps1 -Development
```

O pacote DEV usa a mesma configuração pública do Supabase que a edição Production; não precisa de servidor local, túnel nem segredo OAuth Desktop. Os dados locais continuam separados por edição.

Saídas: `dist\ClipDesk-DEV-Setup.exe`, `dist\ClipDesk-DEV-Windows-x64.zip` e a Release local em `bin\Release\net8.0-windows\ClipDesk.exe`.

O instalador aceita `--test-install <pasta-temporária-isolada>`: extrai o pacote e gera `install-test-result.json`, sem criar atalhos, registrar desinstalador ou alterar os dados pessoais. A limpeza real não é executada durante a validação: os testes de descarte/rollback usam somente arquivos sintéticos em pastas temporárias próprias.

Validação desta revisão: 24 verificações do núcleo, 64 de nuvem/limites, verificações visuais WPF (incluindo histórico e opções do instalador), e testes de preservação/rollback/descarte isolado. A instalação/desinstalação real na máquina de uso diário não foi executada.
