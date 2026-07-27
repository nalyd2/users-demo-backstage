# Fuentes de Verdad

## Fuentes Canonicas

Las siguientes son consideradas **fuentes de verdad autoritativas** para informacion sobre el Servicio de Usuarios. Cuando surjan conflictos entre fuentes, las fuentes de mayor precedencia tienen prioridad.

## Jerarquia de Precedencia

| Prioridad | Fuente | Alcance | Autoridad |
|---|---|---|---|
| **1 (Mas alta)** | `openapi.yaml` | Contrato de API | **Normativa** -- la API ES lo que dice la especificacion |
| **2** | `catalog-info.yaml` | Metadatos de entidad | **Normativa** -- propiedad, dependencias, ciclo de vida, pertenencia a sistema/dominio |
| **3** | `docs/architecture/*.md` | Diseno del sistema | **Autoritativa** -- escrita por arquitectos, revisada por el equipo |
| **4** | `docs/adr/*.md` | Decisiones | **Autoritativa** -- registros de decisiones arquitectonicas aceptadas (ej., PostgreSQL sobre MongoDB, validacion JWT dual) |
| **5** | `docs/api/*.md` | Documentacion de API | **Informativa** -- derivada de OpenAPI, anade narrativa |
| **6** | `docs/runbooks/*.md` | Operaciones | **Autoritativa** -- escrita por SRE, validada en produccion |
| **7** | `docs/decisions/*.md` | Estandares | **Normativa** -- estas SON las reglas |
| **8** | `docs/onboarding/*.md` | Guias | **Informativa** -- util pero puede estar desactualizada respecto al codigo |
| **9** | `README.md` | Resumen general | **Informativa** -- resumen de nivel inicial |
| **10 (Mas baja)** | Codigo fuente (`src/`) | Implementacion | **Informativa** -- el codigo hace lo que el codigo hace, pero la especificacion es el contrato |

## Resolucion de Conflictos

Cuando dos fuentes discrepan:

1. La fuente de **mayor precedencia** se considera correcta
2. Reportar un incidente para reconciliar la discrepancia
3. Etiquetarlo como `documentation-gap` o `spec-implementation-gap`

## Fuentes de Verdad Externas

| Fuente | Alcance | Relacion |
|---|---|---|
| **Azure AD / Entra ID** | Identidad corporativa | Maestro para existencia de empleados, departamento, puesto de trabajo, jerarquia de gerentes. El Servicio de Usuarios enriquece perfiles desde esta fuente cada noche |
| **Servicio de Autenticacion** | Emision de JWT | Maestro para tokens de acceso, tokens de actualizacion y estado de sesion. El Servicio de Usuarios valida JWTs contra este servicio |
| **Azure Key Vault** | Secretos y llaves | Maestro para cadenas de conexion de PostgreSQL, cadenas de conexion de Service Bus y certificados de cliente gRPC |
| **PostgreSQL (BD de Usuarios)** | Perfiles de usuario | Fuente de verdad en tiempo de ejecucion para datos de perfil de usuario, asignaciones de roles y registros de auditoria. Respaldado cada noche, restauracion a un punto en el tiempo habilitada |
| **Catalogo de Backstage** | Registro de entidades consolidado | Vista agregada -- alimentada por archivos `catalog-info.yaml` individuales |
| **Microsoft Graph API** | Enriquecimiento de Entra ID | Fuente de verdad para atributos del directorio corporativo (departamento, ubicacion de oficina, gerente, foto de perfil) |

## Notas Especificas de Precedencia para el Servicio de Usuarios

| Escenario | Regla de Precedencia |
|---|---|
| Definiciones de roles RBAC | `docs/architecture/security.md` tiene prioridad sobre `docs/api/users-api.md` |
| Contrato de endpoint | `openapi.yaml` tiene prioridad sobre `docs/api/users-api.md` |
| Declaracion de dependencia | `catalog-info.yaml` `spec.dependsOn` tiene prioridad sobre el markdown de arquitectura |
| Esquema de eventos | `docs/api/events.md` tiene prioridad (fuente unica de verdad para cargas utiles de eventos) |
| Politica de retencion de datos | `docs/architecture/security.md` (seccion de Manejo de PII) es la fuente canonica |
| Versiones de tecnologia | `docs/architecture/technology-stack.md` tiene prioridad sobre `README.md` |

## Documentos Relacionados

- [Document Priority](document-priority.md)
- [Indexing Strategy](indexing-strategy.md)
