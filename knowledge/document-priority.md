# Document Priority for AI Retrieval

## Priority Levels

| Priority | Description | Use in RAG |
|---|---|---|
| **P0 — Critical** | Must be retrieved first for relevant queries | Always included in top results |
| **P1 — High** | Highly relevant for specific domains | Included when domain matches |
| **P2 — Medium** | Useful context, not urgent | Included when Top-K > 3 |
| **P3 — Low** | Background information | Included only on explicit query match |

## Document Priority Matrix

| Document | Priority | Primary Query Types |
|---|---|---|
| `docs/architecture/overview.md` | P1 | "What is the Users Service?", "Architecture overview" |
| `docs/architecture/context.md` | P1 | "How does Users Service fit into the platform?", "What does Users Service depend on?" |
| `docs/architecture/containers.md` | P1 | "What databases does Users Service use?", "Containers and runtimes" |
| `docs/architecture/components.md` | P1 | "How is the Users Service structured internally?", "What components does it have?" |
| `docs/architecture/deployment-view.md` | P2 | "Where is Users Service deployed?" |
| `docs/architecture/security.md` | P0 | "How is JWT validated?", "What is the RBAC model?", "How is tenant isolation enforced?", "PII handling" |
| `docs/architecture/technology-stack.md` | P2 | "What version of .NET?", "Technology inventory", "Dependency versions" |
| `docs/api/users-api.md` | P0 | "How do I create/update/delete users?", "API reference", "What fields are required?" |
| `docs/api/events.md` | P1 | "What events does Users Service publish?", "Auth events consumed", "Event schema" |
| `docs/api/variables.md` | P1 | "How do I configure the Users Service?", "Environment variables" |
| `docs/runbooks/incident-response.md` | P0 | "Users Service is down — what do I do?" |
| `docs/runbooks/restart-service.md` | P0 | "How do I restart the service?" |
| `docs/runbooks/deployment.md` | P1 | "How do I deploy changes?" |
| `docs/runbooks/rollback.md` | P0 | "How do I rollback a bad deployment?" |
| `docs/runbooks/operations.md` | P2 | "How do I rotate database credentials?", "Routine maintenance", "Profile sync troubleshooting" |
| `docs/adr/*.md` | P2 | "Why did we choose PostgreSQL over MongoDB?", "Architecture rationale", "ADR-002 JWT validation" |
| `docs/onboarding/*.md` | P2 | "How do I set up local dev?", "New developer questions" |
| `docs/decisions/security-guidelines.md` | P1 | "Security requirements", "What is our security policy?" |
| `docs/decisions/dependencies.md` | P1 | "What does Users Service depend on?", "Auth Service dependency" |
| `docs/decisions/*` (other) | P3 | General policy questions |
| `README.md` | P1 | Entry-level questions, endpoint summary, platform relationships |
| `openapi.yaml` | P0 | API specification queries, endpoint schema, request/response shapes |
| `mkdocs.yml` | P3 | Documentation structure queries |

## Priority in Retrieval Pipeline

The priority score is combined with the vector similarity score:

```
final_score = (0.7 × cosine_similarity) + (0.3 × normalized_priority_score)
```

Where `normalized_priority_score` = P0:1.0, P1:0.75, P2:0.5, P3:0.25

## Related Documents

- [Sources of Truth](sources-of-truth.md)
- [RAG](rag.md)
- [Indexing Strategy](indexing-strategy.md)
