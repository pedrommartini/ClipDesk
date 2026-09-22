-- ClipDesk Cloud: core relational model for Supabase.
-- This migration intentionally enables RLS before granting any client role access.

create extension if not exists pgcrypto;

create table if not exists public.profiles (
  id uuid primary key references auth.users(id) on delete cascade,
  username text unique,
  display_name text not null default 'Usuário',
  picture_url text,
  created_at timestamptz not null default now(),
  constraint profiles_username_format check (
    username is null or username ~ '^[a-z0-9_]{3,24}$'
  )
);

create table if not exists public.workspaces (
  id uuid primary key default gen_random_uuid(),
  owner_id uuid not null references public.profiles(id) on delete restrict,
  name text not null check (char_length(name) between 1 and 240),
  mode text not null default 'personal' check (mode in ('personal', 'shared')),
  world_width numeric not null default 4800 check (world_width between 1 and 100000),
  world_height numeric not null default 3200 check (world_height between 1 and 100000),
  schema_version integer not null default 1 check (schema_version between 1 and 100),
  version bigint not null default 1,
  deleted boolean not null default false,
  created_at timestamptz not null default now(),
  updated_at timestamptz not null default now()
);

create table if not exists public.workspace_members (
  workspace_id uuid not null references public.workspaces(id) on delete cascade,
  user_id uuid not null references public.profiles(id) on delete cascade,
  role text not null check (role in ('owner', 'editor')),
  joined_at timestamptz not null default now(),
  primary key (workspace_id, user_id)
);

create table if not exists public.board_entities (
  id uuid primary key,
  workspace_id uuid not null references public.workspaces(id) on delete cascade,
  owner_id uuid not null references public.profiles(id) on delete restrict,
  kind text not null check (kind in ('item', 'boardObject')),
  version bigint not null default 1,
  data jsonb not null default '{}'::jsonb,
  deleted boolean not null default false,
  created_at timestamptz not null default now(),
  updated_at timestamptz not null default now(),
  constraint board_entities_data_size check (octet_length(data::text) <= 1000000)
);

create index if not exists board_entities_workspace_idx on public.board_entities(workspace_id);

create table if not exists public.workspace_invitations (
  id uuid primary key default gen_random_uuid(),
  workspace_id uuid not null references public.workspaces(id) on delete cascade,
  from_user_id uuid not null references public.profiles(id) on delete cascade,
  to_user_id uuid not null references public.profiles(id) on delete cascade,
  status text not null default 'pending' check (status in ('pending', 'accepted', 'declined', 'cancelled')),
  created_at timestamptz not null default now(),
  unique (workspace_id, to_user_id)
);

create table if not exists public.workspace_presence (
  workspace_id uuid not null references public.workspaces(id) on delete cascade,
  user_id uuid not null references public.profiles(id) on delete cascade,
  x numeric not null,
  y numeric not null,
  item_id uuid,
  view_center_x numeric not null default 0,
  view_center_y numeric not null default 0,
  zoom numeric not null default 1 check (zoom between 0.05 and 20),
  updated_at timestamptz not null default now(),
  primary key (workspace_id, user_id)
);

-- Keep the application profile and the initial owner membership in lockstep with
-- Supabase Auth. Client code never needs a service-role key for either action.
create or replace function public.create_profile_for_auth_user()
returns trigger language plpgsql security definer set search_path = public as $$
begin
  insert into public.profiles(id, display_name, picture_url)
  values (
    new.id,
    coalesce(new.raw_user_meta_data ->> 'full_name', new.raw_user_meta_data ->> 'name', 'Usuário'),
    coalesce(new.raw_user_meta_data ->> 'avatar_url', new.raw_user_meta_data ->> 'picture')
  )
  on conflict (id) do nothing;
  return new;
end;
$$;

drop trigger if exists auth_user_profile on auth.users;
create trigger auth_user_profile
  after insert on auth.users for each row execute function public.create_profile_for_auth_user();

create or replace function public.add_workspace_owner()
returns trigger language plpgsql security definer set search_path = public as $$
begin
  insert into public.workspace_members(workspace_id, user_id, role)
  values (new.id, new.owner_id, 'owner')
  on conflict (workspace_id, user_id) do nothing;
  return new;
end;
$$;

drop trigger if exists workspace_owner_membership on public.workspaces;
create trigger workspace_owner_membership
  after insert on public.workspaces for each row execute function public.add_workspace_owner();

alter table public.profiles enable row level security;
alter table public.workspaces enable row level security;
alter table public.workspace_members enable row level security;
alter table public.board_entities enable row level security;
alter table public.workspace_invitations enable row level security;
alter table public.workspace_presence enable row level security;

create or replace function public.is_workspace_member(target_workspace uuid)
returns boolean language sql stable security definer set search_path = public as $$
  select exists (
    select 1 from public.workspace_members
    where workspace_id = target_workspace and user_id = auth.uid()
  );
$$;

create or replace function public.is_workspace_owner(target_workspace uuid)
returns boolean language sql stable security definer set search_path = public as $$
  select exists (
    select 1 from public.workspace_members
    where workspace_id = target_workspace and user_id = auth.uid() and role = 'owner'
  );
$$;

create policy "profiles are visible to signed-in users" on public.profiles
  for select to authenticated using (true);
create policy "users update their own profile" on public.profiles
  for update to authenticated using (id = auth.uid()) with check (id = auth.uid());

create policy "members read workspaces" on public.workspaces
  for select to authenticated using (public.is_workspace_member(id));
create policy "users create owned workspaces" on public.workspaces
  for insert to authenticated with check (owner_id = auth.uid());
create policy "owners update workspaces" on public.workspaces
  for update to authenticated using (public.is_workspace_owner(id)) with check (owner_id = auth.uid());

create policy "members read workspace membership" on public.workspace_members
  for select to authenticated using (public.is_workspace_member(workspace_id));
create policy "owners manage members" on public.workspace_members
  for all to authenticated using (public.is_workspace_owner(workspace_id)) with check (public.is_workspace_owner(workspace_id));

create policy "members read board entities" on public.board_entities
  for select to authenticated using (public.is_workspace_member(workspace_id));
create policy "members create board entities" on public.board_entities
  for insert to authenticated with check (public.is_workspace_member(workspace_id) and owner_id = auth.uid());
create policy "members update board entities" on public.board_entities
  for update to authenticated using (public.is_workspace_member(workspace_id)) with check (public.is_workspace_member(workspace_id));

create policy "participants read invitations" on public.workspace_invitations
  for select to authenticated using (from_user_id = auth.uid() or to_user_id = auth.uid());
create policy "owners create invitations" on public.workspace_invitations
  for insert to authenticated with check (from_user_id = auth.uid() and public.is_workspace_owner(workspace_id));
create policy "recipients answer invitations" on public.workspace_invitations
  for update to authenticated using (to_user_id = auth.uid()) with check (to_user_id = auth.uid());

create policy "members read presence" on public.workspace_presence
  for select to authenticated using (public.is_workspace_member(workspace_id));
create policy "members publish their own presence" on public.workspace_presence
  for insert to authenticated with check (public.is_workspace_member(workspace_id) and user_id = auth.uid());
create policy "members update their own presence" on public.workspace_presence
  for update to authenticated using (user_id = auth.uid() and public.is_workspace_member(workspace_id)) with check (user_id = auth.uid() and public.is_workspace_member(workspace_id));

grant usage on schema public to authenticated;
grant select, update on public.profiles to authenticated;
grant select, insert, update on public.workspaces to authenticated;
grant select, insert, update, delete on public.workspace_members to authenticated;
grant select, insert, update on public.board_entities to authenticated;
grant select, insert, update on public.workspace_invitations to authenticated;
grant select, insert, update on public.workspace_presence to authenticated;

-- Update timestamps and entity versions in the database, not on the desktop client.
create or replace function public.bump_row_version()
returns trigger language plpgsql security invoker set search_path = public as $$
begin
  new.updated_at = now();
  if tg_table_name in ('workspaces', 'board_entities') then
    new.version = old.version + 1;
  end if;
  return new;
end;
$$;

drop trigger if exists workspaces_bump_version on public.workspaces;
create trigger workspaces_bump_version before update on public.workspaces
  for each row execute function public.bump_row_version();
drop trigger if exists board_entities_bump_version on public.board_entities;
create trigger board_entities_bump_version before update on public.board_entities
  for each row execute function public.bump_row_version();
drop trigger if exists workspace_presence_updated_at on public.workspace_presence;
create trigger workspace_presence_updated_at before update on public.workspace_presence
  for each row execute function public.bump_row_version();

-- These tables drive the live board channel. Keeping the publication scoped
-- avoids streaming unrelated account data to desktop clients.
do $$
begin
  alter publication supabase_realtime add table public.workspaces;
  alter publication supabase_realtime add table public.board_entities;
  alter publication supabase_realtime add table public.workspace_invitations;
  alter publication supabase_realtime add table public.workspace_presence;
exception when duplicate_object then
  null;
end;
$$;
