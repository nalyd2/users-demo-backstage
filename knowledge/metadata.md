# Esquema de Metadatos

## Descripcion General

Cada fragmento indexado lleva metadatos estructurados que impulsan el filtrado, la categorizacion y la recuperacion consciente de prioridad.

## Definicion del Esquema de Metadatos

```json
{
  "chunk_id": {
    "type": "string",
    "format": "{service}-{doc_type}-{section}-{index}",
    "example": "users-service-architecture-security-003"
  },
  "source_file": {
    "type": "string",
    "description": "Ruta relativa a la raiz del repositorio",
    "example": "docs/architecture/security.md"
  },
  "source_repo": {
    "type": "string",
    "enum": ["users-service", "auth-service", "api-gateway", "notification-service", "..."],
    "description": "El repositorio al que pertenece este documento"
  },
  "document_type": {
    "type": "string",
    "enum": ["architecture", "api-reference", "runbook", "adr", "onboarding", "decision", "readme", "openapi"],
    "description": "Categoria principal del documento"
  },
  "subcategory": {
    "type": "string",
    "description": "Categorizacion mas especifica",
    "examples": ["security", "deployment", "user-crud", "event-consumption"]
  },
  "section_title": {
    "type": "string",
    "description": "El encabezado markdown de la seccion de este fragmento"
  },
  "priority": {
    "type": "string",
    "enum": ["P0", "P1", "P2", "P3"],
    "description": "Nivel de prioridad de recuperacion"
  },
  "audience": {
    "type": "array",
    "items": { "type": "string", "enum": ["developers", "sre", "architects", "security", "operators"] },
    "description": "Audiencia objetivo para este contenido"
  },
  "tags": {
    "type": "array",
    "items": { "type": "string" },
    "description": "Etiquetas de formato libre extraidas del contenido del documento y del frontmatter"
  },
  "last_updated": {
    "type": "string",
    "format": "date-time",
    "description": "Fecha de ultima modificacion del archivo fuente (de git)"
  },
  "version": {
    "type": "string",
    "description": "Version de la documentacion (etiqueta git o rama)"
  },
  "chunk_index": {
    "type": "integer",
    "description": "Posicion basada en 0 de este fragmento dentro del documento"
  },
  "total_chunks": {
    "type": "integer",
    "description": "Numero total de fragmentos para este documento"
  },
  "prev_chunk_id": {
    "type": "string",
    "nullable": true,
    "description": "Fragmento anterior en la secuencia del documento"
  },
  "next_chunk_id": {
    "type": "string",
    "nullable": true,
    "description": "Siguiente fragmento en la secuencia del documento"
  },
  "related_documents": {
    "type": "array",
    "items": { "type": "string" },
    "description": "Documentos referenciados cruzadamente extraidos de enlaces markdown"
  }
}
```

## Casos de Uso de Filtrado

| Caso de Uso | Filtro |
|---|---|
| "Muestrame solo documentos de API de Usuarios" | `document_type = "api-reference"` |
| "Runbook para SRE de guardia" | `document_type = "runbook" AND audience CONTAINS "sre"` |
| "Contenido relacionado con seguridad para el Servicio de Usuarios" | `tags CONTAINS "tenant-isolation" OR tags CONTAINS "rbac"` |
| "Solo la version mas reciente" | `version = "latest"` |
| "Documentos de arquitectura para revision" | `document_type = "architecture" AND last_updated > "2026-01-01"` |
| "Documentos de consumo de eventos" | `subcategory = "event-consumption"` |
| "Documentos de dependencia del Servicio de Autenticacion" | `tags CONTAINS "auth-service"` |

## Documentos Relacionados

- [Chunking](chunking.md)
- [Embeddings](embeddings.md)
- [RAG](rag.md)
