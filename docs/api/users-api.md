# Referencia de la API de Usuarios

## Descripción General

La API de Usuarios proporciona operaciones CRUD para la gestión de perfiles de usuario. Todos los endpoints (excepto las comprobaciones de salud) requieren un token de acceso JWT válido emitido por el Servicio de Autenticación.

**URL Base (Producción):** `https://users.internal.platform`

## Autenticación

Incluye el JWT en el encabezado `Authorization`:

```
Authorization: Bearer <access-token>
```

El JWT debe incluir:
- `sub` — el UUID del usuario solicitante
- `roles` — arreglo de cadenas de roles (ej., `["admin"]`)
- `tid` — UUID del inquilino (aplicado en todas las consultas)

## Control de Acceso Basado en Roles

| Acción | `admin` | `operator` | `user` |
|---|---|---|---|
| `GET /api/users` | ✅ | ✅ | ❌ |
| `GET /api/users/{id}` | ✅ Cualquiera | ✅ Cualquiera | ✅ Solo propio |
| `POST /api/users` | ✅ | ❌ | ❌ |
| `PUT /api/users/{id}` | ✅ Cualquiera | ✅ Limitado | ✅ Propio (campos limitados) |
| `DELETE /api/users/{id}` | ✅ | ❌ | ❌ |

## Endpoints

### `GET /api/users`

Lista usuarios con paginación y filtrado.

**Rol requerido:** `admin` o `operator`

**Parámetros de Consulta:**

| Parámetro | Tipo | Valor por defecto | Descripción |
|---|---|---|---|
| `pageSize` | integer | 20 | Usuarios por página (1-100) |
| `continuationToken` | string | — | Cursor opaco de la respuesta anterior |
| `search` | string | — | Búsqueda de texto completo en nombre de usuario, correo electrónico, nombre mostrado |
| `department` | string | — | Filtrar por departamento (coincidencia exacta) |
| `role` | string | — | Filtrar por rol asignado |
| `includeDeleted` | boolean | false | Incluir usuarios eliminados de forma lógica (soft-delete) |

**Respuesta `200 OK`:**

```json
{
  "items": [
    {
      "id": "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
      "username": "john.doe",
      "email": "john.doe@contoso.com",
      "displayName": "John Doe",
      "department": "Engineering",
      "jobTitle": "Senior Software Engineer",
      "roles": ["developer", "project:alpha:read"],
      "lastLoginAt": "2026-07-26T09:45:00Z",
      "createdAt": "2026-01-15T09:30:00Z",
      "updatedAt": "2026-07-26T10:15:00Z"
    }
  ],
  "pagination": {
    "pageSize": 20,
    "continuationToken": "eyJwYWdlIjogMiwgInRpbWVzdGFtcCI6ICIyMDI2LTA3LTI2VDEwOjAwOjAwWiJ9",
    "hasMore": true
  },
  "totalCount": 156
}
```

---

### `GET /api/users/{userId}`

Obtiene un usuario por su ID. Acceso propio permitido para el rol `user`.

**Respuesta `200 OK`:** Misma estructura que los elementos en la respuesta de lista.

**Respuestas de Error:**

| Estado | Condición |
|---|---|
| `401` | JWT faltante o inválido |
| `403` | Rol no autorizado, o solicitando otro usuario con rol `user` |
| `404` | Usuario no encontrado en el inquilino (o eliminado lógicamente, a menos que el actor sea admin) |

---

### `POST /api/users`

Crea un nuevo perfil de usuario.

**Rol requerido:** `admin`

**Solicitud:**

```json
{
  "username": "john.doe",
  "email": "john.doe@contoso.com",
  "displayName": "John Doe",
  "department": "Engineering",
  "jobTitle": "Senior Software Engineer",
  "roles": ["developer"]
}
```

**Reglas de Validación:**

| Campo | Regla |
|---|---|
| `username` | 3-100 caracteres, alfanumérico en minúsculas + `.`, `-`, `_` |
| `email` | Correo electrónico válido, máximo 255 caracteres, único dentro del inquilino |
| `displayName` | Máximo 200 caracteres |
| `department` | Máximo 100 caracteres |
| `jobTitle` | Máximo 150 caracteres |
| `roles` | Máximo 20 entradas, cada una debe ser un rol conocido |

**Respuesta `201 Creado`:**

```json
{
  "id": "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
  "username": "john.doe",
  "email": "john.doe@contoso.com",
  "displayName": "John Doe",
  "department": "Engineering",
  "jobTitle": "Senior Software Engineer",
  "roles": ["developer"],
  "lastLoginAt": null,
  "createdAt": "2026-07-26T10:30:00Z",
  "updatedAt": "2026-07-26T10:30:00Z"
}
```

Incluye el encabezado `Location` con la URL del usuario creado.

**Respuestas de Error:**

| Estado | Condición |
|---|---|
| `400` | Error de validación |
| `409` | Nombre de usuario o correo electrónico ya existente en el inquilino |

---

### `PUT /api/users/{userId}`

Actualiza un perfil de usuario. Se aplican permisos a nivel de campo.

**Permisos:**

| Campo | `admin` | `operator` | `user` (propio) |
|---|---|---|---|
| `email` | ✅ | ✅ | ✅ |
| `displayName` | ✅ | ✅ | ✅ |
| `department` | ✅ | ✅ | ❌ |
| `jobTitle` | ✅ | ✅ | ❌ |
| `roles` | ✅ | ❌ | ❌ |

**Solicitud:** Todos los campos son opcionales — solo se actualizan los campos proporcionados (actualización parcial).

**Respuesta `200 OK`:** Objeto de usuario completo con los campos actualizados.

---

### `DELETE /api/users/{userId}`

Elimina lógicamente un usuario. Establece la marca de tiempo `deleted_at` sin eliminar la fila de la base de datos.

**Rol requerido:** `admin`

**Respuesta `200 OK`:**

```json
{
  "message": "User a1b2c3d4-e5f6-7890-abcd-ef1234567890 has been deleted.",
  "deletedAt": "2026-07-26T10:30:00Z"
}
```

---

### `GET /api/health/live`

Sonda de vitalidad de Kubernetes. Retorna 200 mientras el proceso está activo.

### `GET /api/health/ready`

Sonda de preparación de Kubernetes. Retorna 200 cuando PostgreSQL, el Servicio de Autenticación y Service Bus están saludables.

**Respuesta `200 OK`:**

```json
{
  "status": "Healthy",
  "checks": {
    "postgres": { "status": "Healthy", "latency_ms": 1.8 },
    "auth_service": { "status": "Healthy", "latency_ms": 4.2 },
    "service_bus": { "status": "Healthy", "latency_ms": 8.7 }
  }
}
```

## Paginación

La API utiliza **paginación basada en cursor** para una paginación eficiente y estable sobre grandes conjuntos de datos:

1. Primera solicitud: `GET /api/users?pageSize=50`
2. La respuesta incluye `pagination.continuationToken`
3. Siguiente página: `GET /api/users?pageSize=50&continuationToken=eyJwYWdlIjoy...`
4. Cuando `pagination.hasMore` es `false`, no hay más páginas

**Importante:** No construyas ni decodifiques los tokens de continuación — son opacos.

## Respuestas de Error

Todos los errores utilizan el formato `ProblemDetails` (RFC 9457):

```json
{
  "type": "https://errors.internal.platform/forbidden",
  "title": "Forbidden",
  "status": 403,
  "detail": "The 'admin' role is required to perform this action.",
  "traceId": "00-abcdef1234567890abcdef1234567890-abcdef1234567890-01"
}
```

## Documentos Relacionados

- [Especificación OpenAPI](../../openapi.yaml)
- [Arquitectura de Seguridad](../architecture/security.md)
- [Eventos](events.md)
- [Variables y Configuración](variables.md)
