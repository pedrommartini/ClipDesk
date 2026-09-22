-- A recipient may read an invitation before becoming a workspace member, but
-- must not receive the board itself until accepting it.  Return only the small
-- amount of metadata needed by the invitation notification.
create or replace function public.list_pending_workspace_invitations()
returns table (
  id uuid,
  workspace_id uuid,
  workspace_name text,
  from_username text,
  from_display_name text,
  from_picture_url text
)
language sql
stable
security definer
set search_path = public
as $$
  select
    invitation.id,
    invitation.workspace_id,
    workspace.name,
    coalesce(sender.username, ''),
    coalesce(sender.display_name, ''),
    sender.picture_url
  from public.workspace_invitations invitation
  join public.workspaces workspace on workspace.id = invitation.workspace_id
  join public.profiles sender on sender.id = invitation.from_user_id
  where invitation.to_user_id = auth.uid()
    and invitation.status = 'pending'
    and workspace.deleted = false
  order by invitation.created_at asc;
$$;

revoke all on function public.list_pending_workspace_invitations() from public;
grant execute on function public.list_pending_workspace_invitations() to authenticated;
