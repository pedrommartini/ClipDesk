-- Create (when still local) and share a workspace atomically. owner_id always
-- comes from auth.uid(); no stale desktop value can influence ownership.
create or replace function public.share_workspace_with_user(
  target_workspace uuid,
  target_username text,
  workspace_name text,
  workspace_world_width numeric,
  workspace_world_height numeric,
  workspace_schema_version integer
)
returns uuid language plpgsql security definer set search_path = public as $$
declare recipient uuid;
declare invitation uuid;
begin
  insert into public.workspaces(id, owner_id, name, mode, world_width, world_height, schema_version)
  values (target_workspace, auth.uid(), workspace_name, 'personal', workspace_world_width, workspace_world_height, workspace_schema_version)
  on conflict (id) do nothing;

  if not exists (
    select 1 from public.workspaces
    where id = target_workspace and owner_id = auth.uid() and deleted = false
  ) then
    raise exception 'Mesa não encontrada ou sem permissão';
  end if;

  select id into recipient from public.profiles
  where username = lower(trim(both from target_username));
  if recipient is null then raise exception 'Usuário não encontrado'; end if;
  if recipient = auth.uid() then raise exception 'Você já é o proprietário desta mesa'; end if;

  insert into public.workspace_members(workspace_id, user_id, role)
  values (target_workspace, auth.uid(), 'owner')
  on conflict (workspace_id, user_id) do update set role = 'owner';

  update public.workspaces set mode = 'shared' where id = target_workspace;

  insert into public.workspace_invitations(workspace_id, from_user_id, to_user_id, status)
  values (target_workspace, auth.uid(), recipient, 'pending')
  on conflict (workspace_id, to_user_id) do update
    set from_user_id = excluded.from_user_id, status = 'pending', created_at = now()
  returning id into invitation;
  return invitation;
end;
$$;

grant execute on function public.share_workspace_with_user(uuid, text, text, numeric, numeric, integer) to authenticated;
