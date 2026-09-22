-- Commands requiring coordinated membership changes stay inside Postgres so
-- desktop clients never receive a privileged service key.

create or replace function public.accept_workspace_invitation(invitation_id uuid)
returns uuid language plpgsql security definer set search_path = public as $$
declare target_workspace uuid;
begin
  select workspace_id into target_workspace
  from public.workspace_invitations
  where id = invitation_id and to_user_id = auth.uid() and status = 'pending'
  for update;
  if target_workspace is null then raise exception 'Convite não encontrado'; end if;
  insert into public.workspace_members(workspace_id, user_id, role)
  values (target_workspace, auth.uid(), 'editor')
  on conflict (workspace_id, user_id) do nothing;
  update public.workspace_invitations set status = 'accepted' where id = invitation_id;
  update public.workspaces set mode = 'shared' where id = target_workspace;
  return target_workspace;
end;
$$;

create or replace function public.remove_workspace_member(target_workspace uuid, target_user uuid)
returns void language plpgsql security definer set search_path = public as $$
begin
  if not public.is_workspace_owner(target_workspace) then raise exception 'Sem permissão'; end if;
  if target_user = auth.uid() then raise exception 'O proprietário não pode ser removido'; end if;
  delete from public.workspace_members where workspace_id = target_workspace and user_id = target_user;
  update public.workspace_invitations set status = 'cancelled'
  where workspace_id = target_workspace and to_user_id = target_user and status = 'pending';
end;
$$;

create or replace function public.make_workspace_personal(target_workspace uuid)
returns void language plpgsql security definer set search_path = public as $$
begin
  if not public.is_workspace_owner(target_workspace) then raise exception 'Sem permissão'; end if;
  delete from public.workspace_members where workspace_id = target_workspace and user_id <> auth.uid();
  update public.workspace_invitations set status = 'cancelled' where workspace_id = target_workspace;
  update public.workspaces set mode = 'personal' where id = target_workspace;
end;
$$;

grant execute on function public.accept_workspace_invitation(uuid) to authenticated;
grant execute on function public.remove_workspace_member(uuid, uuid) to authenticated;
grant execute on function public.make_workspace_personal(uuid) to authenticated;
