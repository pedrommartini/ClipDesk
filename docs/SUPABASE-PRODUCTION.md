# Nuvem de produção — ClipDesk

Atualizado em 21/09/2026.

DEV e Production usam o Supabase diretamente e mantêm dados locais em pastas distintas. Não há servidor ClipDesk, chave de serviço, banco administrativo ou segredo OAuth distribuído nos instaladores.

## Componentes

- **Autenticação:** Google OAuth hospedado no Supabase, com PKCE. O aplicativo pede somente `openid`, `email` e `profile`; não pede Drive nem recebe o segredo OAuth do Google.
- **Banco e acesso:** Postgres do Supabase, com Row Level Security. Cada chamada leva a chave pública do projeto e a sessão do usuário; a chave pública não ultrapassa as regras do banco.
- **Tempo real:** Postgres Changes entrega alterações persistidas de mesas, elementos e convites. O cursor usa um canal Broadcast privado por mesa, autorizado por RLS, com agrupamento do movimento mais recente e canal preparado ao abrir a mesa. Um heartbeat de 3 segundos mantém cursor, seleção, centro da visão e zoom disponíveis durante períodos sem movimento. Alterações de câmera e zoom também atualizam a presença imediatamente para a função **Acompanhar visão**.
- **Anexos:** imagens e arquivos de até 30 MB usam o bucket privado `clipdesk-assets`. A chave do objeto começa pelo ID da mesa e as políticas permitem leitura somente aos membros. Arquivos maiores continuam no fluxo explícito do Google Drive.
- **Dados locais:** sessão protegida pelo Windows (DPAPI) e diário SQLite por perfil. Fechar ou reiniciar o aplicativo preserva a sessão; somente a ação explícita **Sair** a revoga e apaga. Alterações offline são mantidas localmente e enviadas quando a conexão retornar.

## Migrações

As migrações em `supabase/migrations/` devem ser aplicadas na ordem numérica:

1. `202609150001_clipdesk_core.sql` cria perfis, mesas, membros, entidades, convites, presença, RLS, versões e publicação Realtime.
2. `202609150002_workspace_commands.sql` cria os comandos protegidos para aceitar convite, remover membro e tornar uma mesa pessoal.
3. `202609150003_owner_share_policies.sql` reforça as operações exclusivas do proprietário e repara o vínculo de membro dos donos.
4. `202609160004_share_workspace.sql` adiciona o comando de convite de mesas já existentes.
5. `202609160005_create_and_share_workspace.sql` torna criação e convite de uma mesa local uma operação atômica e repetível.
6. `202609160006_pending_invitation_details.sql` expõe ao destinatário somente os metadados seguros do convite enquanto ele ainda não é membro da mesa.
7. `202609200007_workspace_assets.sql` cria o bucket privado de anexos e autoriza os canais privados de cursor conforme a participação na mesa.

No painel **Authentication → URL Configuration** do Supabase, inclua `http://127.0.0.1:48173/callback` em **Redirect URLs**. Esse é o retorno local fixo do aplicativo Windows.

Nunca colocar a `service_role` do Supabase, o segredo do cliente Google ou tokens de usuário no repositório, instalador ou landing page.

## Limites atuais deliberados

O histórico completo do clipboard permanece local. Somente itens colocados explicitamente em uma mesa na nuvem publicam seus dados e anexos. Caminhos locais do Windows nunca entram na projeção remota; cada dispositivo mantém sua própria cópia gerenciada depois de verificar tamanho e SHA-256.

## Antes de publicar

1. Criar a tela de consentimento/branding de produção no Google Cloud com a política de privacidade e os contatos de suporte do ClipDesk.
2. Concluir o teste manual com duas contas Google: entrar, definir usernames, convidar, aceitar, mover/editar plugins, acompanhar presença, remover membro e excluir mesa.
3. Compilar explicitamente com `-p:ClipDeskEnvironment=Production` somente após o teste local aprovado.
