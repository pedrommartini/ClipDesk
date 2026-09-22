-- Existing personal boards can be promoted to shared boards immediately after
-- login.  The original policies required an owner-membership row for that
-- promotion, which made the transition brittle if the membership trigger had
-- not yet become visible to the same client transaction.  Ownership is the
-- authoritative immutable relationship for these two operations.

drop policy if exists "owners update workspaces" on public.workspaces;
create policy "owners update workspaces" on public.workspaces
  for update to authenticated
  using (owner_id = auth.uid())
  with check (owner_id = auth.uid());

drop policy if exists "owners create invitations" on public.workspace_invitations;
create policy "owners create invitations" on public.workspace_invitations
  for insert to authenticated
  with check (
    from_user_id = auth.uid()
    and exists (
      select 1 from public.workspaces
      where id = workspace_id and owner_id = auth.uid() and deleted = false
    )
  );

-- Repair the membership invariant for pre-existing owner rows.  It is
-- idempotent and never grants a non-owner any additional access.
insert into public.workspace_members(workspace_id, user_id, role)
select id, owner_id, 'owner' from public.workspaces
on conflict (workspace_id, user_id) do update set role = 'owner';
