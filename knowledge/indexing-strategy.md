# Indexing Strategy

## What to Index

The following document types are **included** in the AI search index:

| Document Type | Priority | Rationale |
|---|---|---|
| Architecture docs (`docs/architecture/`) | **High** | Core knowledge — system design, context, containers, JWT dependency on Auth Service |
| API Reference (`docs/api/`) | **High** | Frequently queried — endpoint specs for user CRUD, events, configuration variables |
| Runbooks (`docs/runbooks/`) | **High** | Incident response — critical for operational AI when Users Service degrades |
| ADRs (`docs/adr/`) | **Medium** | Decision context — useful for understanding why PostgreSQL was chosen over MongoDB, JWT dual validation design |
| Onboarding (`docs/onboarding/`) | **Medium** | Developer enablement — important for new team members setting up the service |
| Decisions (`docs/decisions/`) | **Medium** | Policies and standards — RBAC definition, dependency policies, security guidelines |
| `README.md` (root) | **High** | Entry point — overview, endpoint summary, and quick links for the Users Service |
| `openapi.yaml` | **Medium** | Structured API spec — queryable by endpoint for user management operations |
| `catalog-info.yaml` | **Low** | Backstage metadata — ownership, system/domain binding, dependency declarations |

## What NOT to Index

| Exclusion | Reason |
|---|---|
| `generated/` directory | Already auto-generated; index the source, not the output |
| `.gitignore`, `.editorconfig` | Tool configuration, not documentation |
| `LICENSE` | Legal text — no AI retrieval value |
| `Dockerfile`, `azure-pipelines.yml` | Code-like; can be referenced but not chunked |
| Source code (`src/`) | Excluded from doc index; code search is a separate system |
| Test code (`tests/`) | Excluded; not relevant for documentation Q&A |

## Index Freshness

| Trigger | Action |
|---|---|
| Push to `main` branch | Full re-index of changed files |
| PR merge | Incremental index update |
| Nightly (02:00 UTC) | Full re-index (catch-up for any missed updates) |
| Manual trigger | Full re-index via Azure DevOps pipeline |

## Related Documents

- [Chunking](chunking.md)
- [Document Priority](document-priority.md)
- [Sources of Truth](sources-of-truth.md)
