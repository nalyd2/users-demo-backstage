# Metadata Schema

## Overview

Every indexed chunk carries structured metadata that powers filtering, faceting, and priority-aware retrieval.

## Metadata Schema Definition

```json
{
  "chunk_id": {
    "type": "string",
    "format": "{service}-{doc_type}-{section}-{index}",
    "example": "users-service-architecture-security-003"
  },
  "source_file": {
    "type": "string",
    "description": "Path relative to repo root",
    "example": "docs/architecture/security.md"
  },
  "source_repo": {
    "type": "string",
    "enum": ["users-service", "auth-service", "api-gateway", "notification-service", "..."],
    "description": "The repository this document belongs to"
  },
  "document_type": {
    "type": "string",
    "enum": ["architecture", "api-reference", "runbook", "adr", "onboarding", "decision", "readme", "openapi"],
    "description": "Top-level document category"
  },
  "subcategory": {
    "type": "string",
    "description": "More specific categorization",
    "examples": ["security", "deployment", "user-crud", "event-consumption"]
  },
  "section_title": {
    "type": "string",
    "description": "The markdown heading of this chunk's section"
  },
  "priority": {
    "type": "string",
    "enum": ["P0", "P1", "P2", "P3"],
    "description": "Retrieval priority level"
  },
  "audience": {
    "type": "array",
    "items": { "type": "string", "enum": ["developers", "sre", "architects", "security", "operators"] },
    "description": "Target audience for this content"
  },
  "tags": {
    "type": "array",
    "items": { "type": "string" },
    "description": "Free-form tags extracted from document content and frontmatter"
  },
  "last_updated": {
    "type": "string",
    "format": "date-time",
    "description": "Last modification date of the source file (from git)"
  },
  "version": {
    "type": "string",
    "description": "Documentation version (git tag or branch)"
  },
  "chunk_index": {
    "type": "integer",
    "description": "0-based position of this chunk within the document"
  },
  "total_chunks": {
    "type": "integer",
    "description": "Total number of chunks for this document"
  },
  "prev_chunk_id": {
    "type": "string",
    "nullable": true,
    "description": "Previous chunk in the document sequence"
  },
  "next_chunk_id": {
    "type": "string",
    "nullable": true,
    "description": "Next chunk in the document sequence"
  },
  "related_documents": {
    "type": "array",
    "items": { "type": "string" },
    "description": "Cross-referenced documents extracted from markdown links"
  }
}
```

## Filtering Use Cases

| Use Case | Filter |
|---|---|
| "Show me only Users API docs" | `document_type = "api-reference"` |
| "Runbook for on-call SRE" | `document_type = "runbook" AND audience CONTAINS "sre"` |
| "Security-related content for Users Service" | `tags CONTAINS "tenant-isolation" OR tags CONTAINS "rbac"` |
| "Latest version only" | `version = "latest"` |
| "Architecture docs for review" | `document_type = "architecture" AND last_updated > "2026-01-01"` |
| "Event consumption docs" | `subcategory = "event-consumption"` |
| "Auth Service dependency docs" | `tags CONTAINS "auth-service"` |

## Related Documents

- [Chunking](chunking.md)
- [Embeddings](embeddings.md)
- [RAG](rag.md)
