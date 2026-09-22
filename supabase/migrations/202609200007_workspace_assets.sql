-- Private attachment transport for shared and personal cloud workspaces.
-- The workspace id is the first object-key segment, so the same membership
-- rule that protects board entities also protects their binary contents.
insert into storage.buckets(id, name, public, file_size_limit)
values ('clipdesk-assets', 'clipdesk-assets', false, 30000000)
on conflict (id) do update
set public = false, file_size_limit = excluded.file_size_limit;

create or replace function public.can_access_workspace_asset(object_name text)
returns boolean language sql stable security definer set search_path = public as $$
  select case
    when coalesce((storage.foldername(object_name))[1], '') ~ '^[0-9a-fA-F]{32}$'
      then public.is_workspace_member(((storage.foldername(object_name))[1])::uuid)
    else false
  end;
$$;

revoke all on function public.can_access_workspace_asset(text) from public;
grant execute on function public.can_access_workspace_asset(text) to authenticated;

drop policy if exists "members read workspace assets" on storage.objects;
create policy "members read workspace assets" on storage.objects
  for select to authenticated
  using (bucket_id = 'clipdesk-assets' and public.can_access_workspace_asset(name));

drop policy if exists "members upload workspace assets" on storage.objects;
create policy "members upload workspace assets" on storage.objects
  for insert to authenticated
  with check (bucket_id = 'clipdesk-assets' and public.can_access_workspace_asset(name));

drop policy if exists "owners update workspace assets" on storage.objects;
create policy "owners update workspace assets" on storage.objects
  for update to authenticated
  using (bucket_id = 'clipdesk-assets' and owner_id = (select auth.uid()::text)
    and public.can_access_workspace_asset(name))
  with check (bucket_id = 'clipdesk-assets' and owner_id = (select auth.uid()::text)
    and public.can_access_workspace_asset(name));

drop policy if exists "owners delete workspace assets" on storage.objects;
create policy "owners delete workspace assets" on storage.objects
  for delete to authenticated
  using (bucket_id = 'clipdesk-assets' and owner_id = (select auth.uid()::text)
    and public.can_access_workspace_asset(name));

-- Cursor broadcasts use private channels named workspace:<compact uuid>.
-- Authorization is checked when the member joins, then the WebSocket carries
-- high-frequency pointer updates without exposing them to other accounts.
create or replace function public.can_access_workspace_realtime_topic(topic_name text)
returns boolean language sql stable security definer set search_path = public as $$
  select case
    when coalesce(topic_name, '') ~ '^workspace:[0-9a-fA-F]{32}$'
      then public.is_workspace_member(substring(topic_name from 11)::uuid)
    else false
  end;
$$;

revoke all on function public.can_access_workspace_realtime_topic(text) from public;
grant execute on function public.can_access_workspace_realtime_topic(text) to authenticated;

drop policy if exists "members receive workspace broadcasts" on realtime.messages;
create policy "members receive workspace broadcasts" on realtime.messages
  for select to authenticated
  using (realtime.messages.extension = 'broadcast'
    and public.can_access_workspace_realtime_topic((select realtime.topic())));

drop policy if exists "members send workspace broadcasts" on realtime.messages;
create policy "members send workspace broadcasts" on realtime.messages
  for insert to authenticated
  with check (realtime.messages.extension = 'broadcast'
    and public.can_access_workspace_realtime_topic((select realtime.topic())));
