# Estrategia de Versionado — Users Service

- **Estado:** Aprobado
- **Propietario:** Equipo de Platform Engineering
- **Última actualización:** 2026-07-20

## Visión General

Este documento define la estrategia de versionado para todos los artefactos producidos por el Users Service. Sigue Semantic Versioning 2.0.0 con adaptaciones para el dominio de usuarios, incluyendo versionado de API para endpoints de gestión de usuarios.

## Reglas MAJOR.MINOR.PATCH

### PATCH — Se Incrementa Cuando

- Correcciones de errores compatibles hacia atrás en operaciones CRUD de usuario, gestión de inquilinos o procesamiento de eventos.
- Parches de seguridad para acceso a datos o integración con Graph API.
- Actualizaciones de dependencias (paquetes NuGet, imágenes base de contenedor).
- Mejoras de observabilidad (logs, métricas, trazabilidad).

### MINOR — Se Incrementa Cuando

- Nuevos endpoints de gestión de usuarios (compatibles hacia atrás).
- Nuevos tipos de eventos publicados o consumidos.
- Campos de perfil adicionales en respuestas de usuario.
- Nuevos roles RBAC o ámbitos de permiso.
- Advertencias de obsolescencia para características existentes.
- Opciones de configuración con valores predeterminados seguros (deshabilitados por defecto).

### MAJOR — Se Incrementa Cuando

- Cambios disruptivos en el esquema de usuario o formato de respuesta.
- Eliminación de endpoints o campos obsoletos.
- Cambios en la semántica de soft-delete.
- Cambios disruptivos en esquemas de eventos publicados.
- Migraciones de base de datos que no son compatibles hacia atrás.
- Dejar de soportar una versión de API previamente compatible.

### Etiquetas de Pre-lanzamiento

| Etiqueta | Uso |
|---|---|
| `-alpha.N` | Desarrollo interno, API inestable |
| `-beta.N` | Característica completa para funcionalidad específica, solo correcciones de errores antes de GA |
| `-rc.N` | Candidato de release para validación de QA |

## Versionado de API

La API del Users Service utiliza versionado en la ruta URL:

```
https://users.example.com/api/v1/users
https://users.example.com/api/v2/users
```

### Reglas

- El prefijo de versión aplica a toda la superficie de API (`/api/v1/`, `/api/v2/`).
- Soporte de como máximo dos versiones MAJOR simultáneamente.
- La versión anterior recibe parches de seguridad por mínimo 6 meses después de la obsolescencia.
- Los endpoints internos (salud, métricas, sondas) no tienen versionado.

### Ciclo de Vida de la Versión

| Fase | Comportamiento |
|---|---|
| **Activa** | Soporte completo, correcciones de errores, parches de seguridad |
| **Obsoleta** | Aún servida con encabezado `Sunset`, se anima a los consumidores a migrar |
| **Retirada** | Devuelve `410 Gone`, la guía de migración permanece disponible |

## Registro de Cambios (Changelog)

Cada release DEBE incluir una entrada en `CHANGELOG.md` siguiendo el formato Keep a Changelog:

```markdown
## [v2.3.0] - 2026-06-15

### Añadido
- Endpoint de enriquecimiento de perfil de Microsoft Graph: `GET /api/v2/users/{id}/graph-profile`. (#142)
- Métrica de retraso de procesamiento de eventos para consumidores de eventos de auth. (#155)

### Cambiado
- Actualización de .NET 9 a .NET 10. (#168)

### Obsoleto
- `GET /api/v1/users` (no paginado). Usar `GET /api/v2/users` con paginación por cursor. (#150)
  El soporte se eliminará en v3.0.0.

### Corregido
- Filtro de soft-delete faltante en consultas de usuario con ámbito de inquilino. (#89)
```

## Política de Obsolescencia

- Las características se declaran obsoletas durante al menos un ciclo completo de versión MAJOR antes de su eliminación.
- Las características obsoletas devuelven encabezados `Sunset` y `Deprecation`.
- La obsolescencia se anuncia en el changelog y la documentación de referencia de API.
- Las vulnerabilidades de seguridad pueden eliminarse sin el período de obsolescencia estándar.

## Etiquetado de Imágenes de Contenedor

- `v<MAJOR>.<MINOR>.<PATCH>` — Tag de release inmutable.
- `v<MAJOR>.<MINOR>` — Modificable, actualizado con cada patch.
- `v<MAJOR>` — Modificable, actualizado con el último de esa serie MAJOR.
- `latest` — Modificable, siempre el último release estable.
- `sha-<commit-sha>` — Tag por commit inmutable.
