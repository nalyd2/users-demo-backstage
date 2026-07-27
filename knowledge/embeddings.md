# Configuracion de Incrustaciones

## Seleccion del Modelo

| Atributo | Valor |
|---|---|
| **Modelo Principal** | `text-embedding-3-large` (OpenAI) o equivalente de Azure OpenAI |
| **Dimension** | 2,048 (reducido de 3,072 para optimizacion de costos) |
| **Entrada Maxima** | 8,191 tokens |
| **Codificacion** | Tokenizador `cl100k_base` |

## Por Que Este Modelo

- **2,048 dimensiones** proporcionan excelente resolucion semantica a costos manejables de base de datos vectorial
- **Soporte multilingue** -- maneja documentacion en ingles con terminos tecnicos
- **Representacion Matryoshka** -- soporta reduccion de dimensiones sin perdida significativa de calidad
- **Disponibilidad en Azure OpenAI** -- se ejecuta en el mismo inquilino de Azure que el resto de la plataforma

## Base de Datos Vectorial

| Atributo | Valor |
|---|---|
| **Base de Datos** | Azure AI Search (anteriormente Cognitive Search) |
| **Tipo de Indice** | HNSW (Hierarchical Navigable Small World) |
| **Metrica** | Similitud de coseno |
| **Compresion** | Cuantizacion escalar (int8) |

## Pipeline de Incrustacion

```
Documento (Markdown)
    │
    ▼
Divisor de fragmentos (1,024 tokens, 128 de superposicion)
    │
    ▼
Extractor de metadatos (seccion, tipo, etiquetas, prioridad)
    │
    ▼
text-embedding-3-large (Azure OpenAI)
    │
    ▼
Almacen vectorial (Azure AI Search)
    │
    ▼
Listo para recuperacion
```

## Actualizacion de Incrustaciones

| Disparador | Accion |
|---|---|
| Documento actualizado en `main` | Re-incrustar solo los fragmentos cambiados (incremental) |
| Modelo de incrustacion actualizado | Re-incrustacion completa (programada, no automatica) |
| Documentacion versionada | Incrustaciones almacenadas por version de documentacion |

## Documentos Relacionados

- [Chunking](chunking.md)
- [RAG](rag.md)
- [Metadata](metadata.md)
