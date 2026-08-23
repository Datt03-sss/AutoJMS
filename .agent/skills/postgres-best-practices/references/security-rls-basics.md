---
title: Enable Row Level Security for Multi-Tenant Data
impact: CRITICAL
impactDescription: Database-enforced tenant isolation, prevent data leaks
tags: rls, row-level-security, multi-tenant, security
---

## Enable Row Level Security for Multi-Tenant Data

> **Not the AutoJMS model.** This chapter assumes clients talk to Postgres directly, so the
> database is the only place left to enforce tenancy. AutoJMS clients never reach Postgres — they
> go through the DataHub API, which scopes every query by `siteId` from the device token. There is
> no RLS, no `CREATE POLICY`, and no `anon`/`authenticated` role in `backend/datahub/migrations/`.
> Read this for the SQL mechanics; do not add policies to the schema.

Row Level Security (RLS) enforces data access at the database level, ensuring users only see their own data.

**Incorrect (application-level filtering only):**

```sql
-- Relying only on application to filter
select * from orders where user_id = $current_user_id;

-- Bug or bypass means all data is exposed!
select * from orders;  -- Returns ALL orders
```

**Correct (database-enforced RLS):**

```sql
-- Enable RLS on the table
alter table orders enable row level security;

-- Create policy for users to see only their orders
create policy orders_user_policy on orders
  for all
  using (user_id = current_setting('app.current_user_id')::bigint);

-- Force RLS even for table owners
alter table orders force row level security;

-- Set user context and query
set app.current_user_id = '123';
select * from orders;  -- Only returns orders for user 123
```

Policy for the application role:

```sql
create policy orders_user_policy on orders
  for all
  to app_user
  using (user_id = current_setting('app.user_id', true)::uuid);
```

Reference: [Row Security Policies](https://www.postgresql.org/docs/current/ddl-rowsecurity.html)
