# Users API Reference

## Overview

The Users API provides CRUD operations for user profile management. All endpoints (except health checks) require a valid JWT access token issued by the Authentication Service.

**Base URL (Production):** `https://users.internal.platform`

## Authentication

Include the JWT in the `Authorization` header:

```
Authorization: Bearer <access-token>
```

The JWT must include:
- `sub` — the requesting user's UUID
- `roles` — array of role strings (e.g., `["admin"]`)
- `tid` — tenant UUID (enforced on all queries)

## Role-Based Access Control

| Action | `admin` | `operator` | `user` |
|---|---|---|---|
| `GET /api/users` | ✅ | ✅ | ❌ |
| `GET /api/users/{id}` | ✅ Any | ✅ Any | ✅ Self only |
| `POST /api/users` | ✅ | ❌ | ❌ |
| `PUT /api/users/{id}` | ✅ Any | ✅ Limited | ✅ Self (limited fields) |
| `DELETE /api/users/{id}` | ✅ | ❌ | ❌ |

## Endpoints

### `GET /api/users`

List users with pagination and filtering.

**Required role:** `admin` or `operator`

**Query Parameters:**

| Parameter | Type | Default | Description |
|---|---|---|---|
| `pageSize` | integer | 20 | Users per page (1-100) |
| `continuationToken` | string | — | Opaque cursor from previous response |
| `search` | string | — | Full-text search on username, email, display name |
| `department` | string | — | Filter by department (exact match) |
| `role` | string | — | Filter by assigned role |
| `includeDeleted` | boolean | false | Include soft-deleted users |

**Response `200 OK`:**

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

Get a single user by ID. Self-access allowed for `user` role.

**Response `200 OK`:** Same shape as items in the list response.

**Error Responses:**

| Status | Condition |
|---|---|
| `401` | Missing or invalid JWT |
| `403` | Role not authorized, or requesting another user with `user` role |
| `404` | User not found in tenant (or soft-deleted, unless actor is admin) |

---

### `POST /api/users`

Create a new user profile.

**Required role:** `admin`

**Request:**

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

**Validation Rules:**

| Field | Rule |
|---|---|
| `username` | 3-100 chars, lowercase alphanumeric + `.`, `-`, `_` |
| `email` | Valid email, max 255 chars, unique within tenant |
| `displayName` | Max 200 chars |
| `department` | Max 100 chars |
| `jobTitle` | Max 150 chars |
| `roles` | Max 20 entries, each must be a known role |

**Response `201 Created`:**

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

Includes `Location` header with the URL of the created user.

**Error Responses:**

| Status | Condition |
|---|---|
| `400` | Validation error |
| `409` | Username or email already taken in tenant |

---

### `PUT /api/users/{userId}`

Update a user profile. Field-level permissions apply.

**Permissions:**

| Field | `admin` | `operator` | `user` (self) |
|---|---|---|---|
| `email` | ✅ | ✅ | ✅ |
| `displayName` | ✅ | ✅ | ✅ |
| `department` | ✅ | ✅ | ❌ |
| `jobTitle` | ✅ | ✅ | ❌ |
| `roles` | ✅ | ❌ | ❌ |

**Request:** All fields are optional — only provided fields are updated (partial update).

**Response `200 OK`:** Full user object with updated fields.

---

### `DELETE /api/users/{userId}`

Soft-delete a user. Sets `deleted_at` timestamp without removing the database row.

**Required role:** `admin`

**Response `200 OK`:**

```json
{
  "message": "User a1b2c3d4-e5f6-7890-abcd-ef1234567890 has been deleted.",
  "deletedAt": "2026-07-26T10:30:00Z"
}
```

---

### `GET /api/health/live`

Kubernetes liveness probe. Returns 200 while process is alive.

### `GET /api/health/ready`

Kubernetes readiness probe. Returns 200 when PostgreSQL, Auth Service, and Service Bus are healthy.

**Response `200 OK`:**

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

## Pagination

The API uses **cursor-based pagination** for efficient, stable pagination over large datasets:

1. First request: `GET /api/users?pageSize=50`
2. Response includes `pagination.continuationToken`
3. Next page: `GET /api/users?pageSize=50&continuationToken=eyJwYWdlIjoy...`
4. When `pagination.hasMore` is `false`, there are no more pages

**Important:** Do not construct or decode continuation tokens — they are opaque.

## Error Responses

All errors use the `ProblemDetails` format (RFC 9457):

```json
{
  "type": "https://errors.internal.platform/forbidden",
  "title": "Forbidden",
  "status": 403,
  "detail": "The 'admin' role is required to perform this action.",
  "traceId": "00-abcdef1234567890abcdef1234567890-abcdef1234567890-01"
}
```

## Related Documents

- [OpenAPI Specification](../../openapi.yaml)
- [Security Architecture](../architecture/security.md)
- [Events](events.md)
- [Variables & Configuration](variables.md)
