# Arquitectura del Pipeline RAG

## Descripcion General

El pipeline de Generacion Aumentada por Recuperacion (RAG) combina busqueda semantica sobre documentacion con generacion de LLM para responder preguntas de los usuarios sobre el Servicio de Usuarios.

## Arquitectura

```
Consulta del Usuario: "Como asigno un nuevo rol a un usuario existente?"
    │
    ▼
Reescritura de Consulta (LLM)
    │  Expande: "procedimiento de asignacion de roles del Servicio de Usuarios actualizacion de perfil RBAC"
    ▼
Busqueda Vectorial (Azure AI Search)
    │  Top-K = 10 fragmentos, similitud de coseno > 0.75
    ▼
Re-clasificacion (Cross-encoder)
    │  Cohere Rerank o clasificador semantico de Azure AI Search
    ▼
Ensamblaje de Contexto
    │  Top 5 fragmentos + su contexto circundante
    ▼
Generacion LLM (GPT-4o o Claude)
    │  Prompt de sistema + contexto recuperado + consulta del usuario
    ▼
Respuesta + Citas
    │  Respuesta con enlaces a los documentos fuente
    ▼
Entregada al usuario (chat de Backstage, bot de Slack, etc.)
```

## Parametros de Recuperacion

| Parametro | Valor | Justificacion |
|---|---|---|
| **Top-K (inicial)** | 10 | Suficientemente amplio para capturar conceptos relacionados (RBAC, validacion JWT, aislamiento de inquilinos) |
| **Umbral de similitud** | 0.75 | Filtra resultados irrelevantes |
| **Top-N (despues de re-clasificacion)** | 5 | Cabe dentro de la ventana de contexto del LLM |
| **Ventana de contexto** | 8K tokens | Coincide con el contexto efectivo del modelo |

## Plantilla de Prompt de Sistema

```
Eres un asistente de IA para la Plataforma Interna de Desarrolladores -- dominio del Servicio de Usuarios.
Responde preguntas usando SOLAMENTE la documentacion proporcionada a continuacion.

Si la respuesta no esta en la documentacion, di:
"No tengo suficiente informacion en la documentacion para responder eso.
Puedes encontrar mas detalles en: [enlace a la seccion de TechDocs relevante]."

Documentacion:
{retrieved_chunks}

Pregunta del usuario: {query}
```

## Fundamentacion y Citas

Cada respuesta incluye citas a los documentos fuente:

```
Respuesta: Para asignar un nuevo rol a un usuario, use el endpoint PUT /api/users/{userId}
con el rol admin. El campo `roles` reemplaza todas las asignaciones de roles actuales.
Solo el rol `admin` puede modificar el campo roles.

Fuentes:
- docs/api/users-api.md#role-based-access-control
- docs/architecture/security.md#authorization-model-rbac
- docs/architecture/components.md#user-service-methods
```

## Evaluacion

| Metrica | Objetivo | Medicion |
|---|---|---|
| **Relevancia de la respuesta** | > 0.85 | Puntuacion LLM-como-juez |
| **Precision del contexto** | > 0.90 | Precision@5 de fragmentos recuperados |
| **Tasa de alucinacion** | < 2% | Revision manual de 100 consultas/mes |
| **Latencia (p95)** | < 3 segundos | De extremo a extremo desde la consulta hasta la respuesta |

## Documentos Relacionados

- [Embeddings](embeddings.md)
- [Sources of Truth](sources-of-truth.md)
- [Document Priority](document-priority.md)
