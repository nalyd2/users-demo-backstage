# Knowledge Base Preparation for AI / RAG

## Purpose

This directory defines the **document architecture** for AI-powered features that will consume the Users Service documentation — specifically Retrieval-Augmented Generation (RAG), knowledge graphs, and semantic search. It does **not** contain AI code or models. It is the specification that an AI/ML team would use to index this repository's documentation.

## Guiding Principle

> **The documentation IS the source of truth.** The AI pipeline should index exactly what a human reads — nothing less, nothing fabricated.

## Documents

| Document | Description |
|---|---|
| [indexing-strategy.md](indexing-strategy.md) | What to index, what to exclude, and why |
| [chunking.md](chunking.md) | How documents are split into embeddable chunks |
| [metadata.md](metadata.md) | Metadata schema for each document chunk |
| [embeddings.md](embeddings.md) | Embedding model selection and configuration |
| [rag.md](rag.md) | RAG pipeline architecture and retrieval strategy |
| [knowledge-graph.md](knowledge-graph.md) | Entity extraction and graph construction rules |
| [sources-of-truth.md](sources-of-truth.md) | Canonical sources and their precedence |
| [document-priority.md](document-priority.md) | Priority ordering for indexing and retrieval ranking |

## Target AI Use Cases

| Use Case | Description | Priority |
|---|---|---|
| **User Management Q&A** | "How do I create a new user and assign roles?" -> answer from docs | P0 |
| **Permission Queries** | "What role do I need to update a user's profile?" -> RBAC matrix retrieval | P0 |
| **Incident Assist** | "Users Service shows `503` on user creation — what should I check?" -> runbook extraction | P0 |
| **Architecture Discovery** | "Show me all services that depend on Users Service" -> knowledge graph traversal | P1 |
| **Tenant Isolation Audit** | "How are users isolated across tenants?" -> security architecture retrieval | P1 |
| **Onboarding Copilot** | "I'm new — walk me through setting up local dev for the Users Service" -> guided walkthrough | P1 |
| **Dependency Impact Analysis** | "If the Auth Service goes down, how does Users Service degrade?" -> dependency and circuit-breaker analysis | P2 |
| **Compliance Audit** | "Show me the data retention policy for PII fields" -> cross-document retrieval | P2 |

## Document Taxonomy

The documentation uses implicit and explicit taxonomy markers that the AI pipeline should extract:

```yaml
# Implicit in mkdocs.yml navigation hierarchy
Architecture > Security > Authentication Flow
Runbooks > Incident Response > Users Service Unavailable
API Reference > Users API > POST /api/users

# Explicit in document frontmatter (when present)
---
category: architecture
subcategory: security
tags: [jwt, rbac, tenant-isolation, multi-tenancy]
priority: high
audience: [developers, sre, operators]
---
```

## Related Documents

- [Sources of Truth](sources-of-truth.md)
- [Document Priority](document-priority.md)
- [RAG Architecture](rag.md)
