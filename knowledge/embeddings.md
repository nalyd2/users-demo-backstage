# Embeddings Configuration

## Model Selection

| Attribute | Value |
|---|---|
| **Primary Model** | `text-embedding-3-large` (OpenAI) or Azure OpenAI equivalent |
| **Dimension** | 2,048 (reduced from 3,072 for cost optimization) |
| **Max Input** | 8,191 tokens |
| **Encoding** | `cl100k_base` tokenizer |

## Why This Model

- **2,048 dimensions** provide excellent semantic resolution at manageable vector DB costs
- **Multilingual support** — handles English documentation with technical terms
- **Matryoshka representation** — supports dimension reduction without significant quality loss
- **Azure OpenAI availability** — runs in the same Azure tenant as the rest of the platform

## Vector Database

| Attribute | Value |
|---|---|
| **Database** | Azure AI Search (formerly Cognitive Search) |
| **Index Type** | HNSW (Hierarchical Navigable Small World) |
| **Metric** | Cosine similarity |
| **Compression** | Scalar quantization (int8) |

## Embedding Pipeline

```
Document (Markdown)
    │
    ▼
Chunk splitter (1,024 tokens, 128 overlap)
    │
    ▼
Metadata extractor (section, type, tags, priority)
    │
    ▼
text-embedding-3-large (Azure OpenAI)
    │
    ▼
Vector store (Azure AI Search)
    │
    ▼
Ready for retrieval
```

## Embedding Refresh

| Trigger | Action |
|---|---|
| Document updated in `main` | Re-embed changed chunks only (incremental) |
| Embedding model upgraded | Full re-embedding (scheduled, not automatic) |
| Versioned documentation | Embeddings stored per documentation version |

## Related Documents

- [Chunking](chunking.md)
- [RAG](rag.md)
- [Metadata](metadata.md)
