# RAG Pipeline Architecture

## Overview

The Retrieval-Augmented Generation (RAG) pipeline combines semantic search over documentation with LLM generation to answer user questions about the Users Service.

## Architecture

```
User Query: "How do I assign a new role to an existing user?"
    │
    ▼
Query Rewriting (LLM)
    │  Expands: "Users Service role assignment procedure RBAC profile update"
    ▼
Vector Search (Azure AI Search)
    │  Top-K = 10 chunks, cosine similarity > 0.75
    ▼
Re-ranking (Cross-encoder)
    │  Cohere Rerank or Azure AI Search semantic ranker
    ▼
Context Assembly
    │  Top 5 chunks + their surrounding context
    ▼
LLM Generation (GPT-4o or Claude)
    │  System prompt + retrieved context + user query
    ▼
Response + Citations
    │  Answer with links to source documents
    ▼
Delivered to user (Backstage chat, Slack bot, etc.)
```

## Retrieval Parameters

| Parameter | Value | Rationale |
|---|---|---|
| **Top-K (initial)** | 10 | Broad enough to capture related concepts (RBAC, JWT validation, tenant isolation) |
| **Similarity threshold** | 0.75 | Filters irrelevant results |
| **Top-N (after rerank)** | 5 | Fits within LLM context window |
| **Context window** | 8K tokens | Matches model's effective context |

## System Prompt Template

```
You are an AI assistant for the Internal Developer Platform — Users Service domain.
Answer questions using ONLY the documentation provided below.

If the answer is not in the documentation, say:
"I don't have enough information in the documentation to answer that.
You can find more details at: [link to relevant TechDocs section]."

Documentation:
{retrieved_chunks}

User question: {query}
```

## Grounding & Citation

Every answer includes citations to source documents:

```
Answer: To assign a new role to a user, use the PUT /api/users/{userId} endpoint
with the admin role. The `roles` field replaces all current role assignments.
Only the `admin` role can modify the roles field.

Sources:
- docs/api/users-api.md#role-based-access-control
- docs/architecture/security.md#authorization-model-rbac
- docs/architecture/components.md#user-service-methods
```

## Evaluation

| Metric | Target | Measurement |
|---|---|---|
| **Answer relevance** | > 0.85 | LLM-as-judge scoring |
| **Context precision** | > 0.90 | Precision@5 of retrieved chunks |
| **Hallucination rate** | < 2% | Manual review of 100 queries/month |
| **Latency (p95)** | < 3 seconds | End-to-end from query to response |

## Related Documents

- [Embeddings](embeddings.md)
- [Sources of Truth](sources-of-truth.md)
- [Document Priority](document-priority.md)
