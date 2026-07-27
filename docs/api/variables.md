# Variables y Configuración

## Descripción General

El Servicio de Usuarios sigue los principios de **aplicación de 12 factores**. Toda la configuración se proporciona a través de variables de entorno o referencias de Azure Key Vault.

## Variables de Entorno

### Requeridas

| Variable | Descripción | Ejemplo |
|---|---|---|
| `ConnectionStrings__UsersDb` | Cadena de conexión de PostgreSQL | `Host=...;Database=users;Username=...` |
| `AuthService__Endpoint` | Endpoint gRPC del Servicio de Autenticación | `https://auth-service.platform.svc.cluster.local:5103` |
| `ServiceBus__ConnectionString` | Cadena de conexión de Azure Service Bus | `Endpoint=sb://...` |
| `ServiceBus__AuthEventsSubscription` | Nombre de la suscripción para eventos de autenticación | `users-service` |
| `ServiceBus__UsersEventsTopic` | Tema para publicar eventos de usuario | `users-events` |
| `KeyVault__Uri` | URI de Azure Key Vault | `https://platform-kv-we.vault.azure.net/` |

### Opcionales

| Variable | Valor por defecto | Descripción |
|---|---|---|
| `Users__DefaultPageSize` | `20` | Tamaño de página predeterminado para endpoints de lista |
| `Users__MaxPageSize` | `100` | Tamaño máximo de página permitido |
| `Users__SoftDeleteRetentionDays` | `30` | Días antes de purgar usuarios eliminados lógicamente |
| `Auth__JWKSCacheTtlMinutes` | `5` | TTL de caché local de JWKS |
| `Auth__GrpcTimeoutMs` | `500` | Tiempo de espera de llamada gRPC para validación de tokens |
| `Auth__CircuitBreakerThreshold` | `5` | Fallos consecutivos antes de abrir el circuito |
| `Auth__CircuitBreakerDurationSeconds` | `30` | Duración del circuito abierto |
| `GraphApi__SyncEnabled` | `false` | Habilitar sincronización de perfiles de Entra ID |
| `GraphApi__SyncSchedule` | `0 2 * * *` | Expresión cron para sincronización nocturna |
| `Logging__MinimumLevel` | `Information` | Nivel mínimo de registro |

## Secretos de Azure Key Vault

| Nombre del Secreto | Descripción | Rotación |
|---|---|---|
| `users-db-connection-string` | Cadena de conexión de PostgreSQL | 180 días |
| `users-service-bus-connection` | Cadena de conexión de Service Bus | 180 días |
| `auth-service-grpc-cert` | Certificado de cliente para mTLS hacia el Servicio de Autenticación | 365 días |

## Banderas de Funcionalidad

| Bandera | Valor por defecto | Descripción |
|---|---|---|
| `GraphApiSync.Enabled` | `false` | Habilitar enriquecimiento de perfiles de Entra ID |
| `EventPublishing.Enabled` | `true` | Publicar eventos del ciclo de vida del usuario |
| `StrictRoleValidation.Enabled` | `true` | Rechazar roles desconocidos en las solicitudes |
| `SelfServiceDelete.Enabled` | `false` | Permitir que los usuarios eliminen sus propias cuentas |

## Configuración Específica por Entorno

| Configuración | dev | qa | staging | production |
|---|---|---|---|---|
| `Users__DefaultPageSize` | 10 | 20 | 20 | 20 |
| `Users__SoftDeleteRetentionDays` | 7 | 14 | 30 | 30 |
| `Auth__JWKSCacheTtlMinutes` | 1 | 5 | 5 | 5 |
| `Auth__GrpcTimeoutMs` | 2000 | 500 | 500 | 500 |
| `Logging__MinimumLevel` | Debug | Information | Information | Warning |
| `GraphApiSync.Enabled` | false | true | true | true |

## Documentos Relacionados

- [Stack Tecnológico](../architecture/technology-stack.md)
- [Arquitectura de Seguridad](../architecture/security.md)
- [Desarrollo Local](../onboarding/local-development.md)
