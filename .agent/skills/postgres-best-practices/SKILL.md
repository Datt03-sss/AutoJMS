---
name: postgres-best-practices
description: Postgres performance optimization and best practices. Use this skill when writing, reviewing, or optimizing Postgres queries, schema designs, or database configurations.
license: MIT
metadata:
  version: "1.1.1"
  upstream: supabase/agent-skills (third-party, MIT) — attribution only; AutoJMS does not use that backend
  vendored: .agent/skills/postgres-best-practices/ — locally edited, no longer CLI-managed
  abstract: Comprehensive Postgres performance optimization guide. Contains performance rules across 8 categories, prioritized by impact from critical (query performance, connection management) to incremental (advanced features). Each rule includes detailed explanations, incorrect vs. correct SQL examples, query plan analysis, and specific performance metrics to guide automated optimization and code generation.
---

# Postgres Best Practices

Comprehensive performance optimization guide for Postgres. Contains rules across 8 categories, prioritized by impact to guide automated query optimization and schema design.

Third-party MIT-licensed reference material, vendored into this repo. It is **generic Postgres
advice** — it does not describe the AutoJMS backend. Where a rule talks about Row-Level Security,
`SECURITY DEFINER` helpers, `auth.role()`, or `anon`/`authenticated` roles it is describing a
managed BaaS. AutoJMS runs plain PostgreSQL in Docker behind its own API: no RLS, no policies, no
client-callable RPC. Read those chapters for the SQL mechanics only; the architecture rules live in
[.agent/rules/05-datahub-firebase-github-rules.md](../../rules/05-datahub-firebase-github-rules.md).

## When to Apply

Reference these guidelines when:
- Writing SQL queries or designing schemas
- Implementing indexes or query optimization
- Reviewing database performance issues
- Configuring connection pooling or scaling
- Optimizing for Postgres-specific features
- Reviewing privileges and role grants

## Rule Categories by Priority

| Priority | Category | Impact | Prefix |
|----------|----------|--------|--------|
| 1 | Query Performance | CRITICAL | `query-` |
| 2 | Connection Management | CRITICAL | `conn-` |
| 3 | Security & RLS | CRITICAL | `security-` |
| 4 | Schema Design | HIGH | `schema-` |
| 5 | Concurrency & Locking | MEDIUM-HIGH | `lock-` |
| 6 | Data Access Patterns | MEDIUM | `data-` |
| 7 | Monitoring & Diagnostics | LOW-MEDIUM | `monitor-` |
| 8 | Advanced Features | LOW | `advanced-` |

## How to Use

Read individual rule files for detailed explanations and SQL examples:

```
references/query-missing-indexes.md
references/query-partial-indexes.md
references/_sections.md
```

Each rule file contains:
- Brief explanation of why it matters
- Incorrect SQL example with explanation
- Correct SQL example with explanation
- Optional EXPLAIN output or metrics
- Additional context and references
- Platform-specific notes (from the upstream author's own platform — check them against AutoJMS
  before applying)

## References

- https://www.postgresql.org/docs/current/
- https://wiki.postgresql.org/wiki/Performance_Optimization
- https://www.postgresql.org/docs/current/ddl-rowsecurity.html
- https://www.postgresql.org/docs/current/using-explain.html
