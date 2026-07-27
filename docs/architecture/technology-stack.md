# Stack Tecnológico

## Alcance

Inventario completo de las tecnologías utilizadas por el Users Service, incluyendo runtime, librerías, infraestructura y herramientas de desarrollo. Sirve como referencia autorizada para la gestión de dependencias.

## Política del Ciclo de Vida de las Tecnologías

| Fase | Descripción | Acción |
|---|---|---|
| **Adopt** | Recomendado para nuevos proyectos | Usar libremente |
| **Trial** | Bajo evaluación | Usar en rutas no críticas |
| **Hold** | En uso pero no para trabajo nuevo | Planificar migración |
| **Deprecate** | En fase de eliminación | Migrar hacia otra |

---

## 1. Runtime y Framework

| Tecnología | Versión | Fase | Propósito |
|---|---|---|---|
| **.NET SDK** | 10.0.100 | Adopt | Runtime y biblioteca de clases base |
| **ASP.NET Core** | 10.0 | Adopt | Framework Web API (Minimal APIs) |
| **C#** | 13 | Adopt | Lenguaje de programación principal |
| **gRPC** | 2.x | Adopt | Cliente para validación de tokens del Auth Service |

## 2. Librerías Principales

| Paquete | Versión | Fase | Propósito |
|---|---|---|---|
| `Dapper` | 2.x | Adopt | ORM ligero para PostgreSQL |
| `Npgsql` | 9.x | Adopt | Proveedor de datos .NET para PostgreSQL |
| `FluentValidation` | 11.x | Adopt | Validación de DTOs de solicitud |
| `Azure.Messaging.ServiceBus` | 7.x | Adopt | Publicador y consumidor de eventos |
| `Azure.Security.KeyVault.Secrets` | 4.x | Adopt | Recuperación de secretos de Key Vault |
| `Azure.Identity` | 1.x | Adopt | Autenticación Managed Identity |
| `Microsoft.Graph` | 5.x | Adopt | Enriquecimiento de perfiles de Entra ID |
| `Grpc.Net.Client` | 2.x | Adopt | Cliente gRPC para Auth Service |
| `Polly` | 8.x | Adopt | Políticas de resiliencia (circuit breaker, reintentos) |
| `MessagePack` | 2.x | Adopt | Serialización binaria para caché |

## 3. Observabilidad

| Tecnología | Versión | Fase | Propósito |
|---|---|---|---|
| **OpenTelemetry SDK** | 1.x | Adopt | Trazado distribuido (W3C Trace Context) |
| **Serilog** | 8.x | Adopt | Registro estructurado en JSON |
| **Prometheus.Client** | 5.x | Adopt | Exposición de métricas |

## 4. Infraestructura (Azure)

| Servicio | SKU / Nivel | Propósito |
|---|---|---|
| **Azure Kubernetes Service** | Standard_D4s_v5 | Orquestación de contenedores |
| **Azure Database for PostgreSQL** | Flexible Server, Propósito General | Almacenamiento de perfiles de usuario |
| **Azure Service Bus** | Premium, redundante entre zonas | Publicación y suscripción de eventos |
| **Azure Key Vault** | Standard | Cadenas de conexión y secretos |
| **Azure Traffic Manager** | Enrutamiento por prioridad | Conmutación por error multi-región |
| **Azure Container Registry** | Premium, con replicación geográfica | Almacenamiento de imágenes Docker |

## 5. Desarrollo y Calidad

| Tecnología | Versión | Fase | Propósito |
|---|---|---|---|
| **xUnit** | 2.x | Adopt | Pruebas unitarias y de integración |
| **FluentAssertions** | 7.x | Adopt | Afirmaciones de prueba legibles |
| **NSubstitute** | 5.x | Adopt | Framework de simulación (mocking) |
| **Testcontainers** | 4.x | Adopt | Pruebas de integración con PostgreSQL |
| **SonarQube** | Cloud | Adopt | Análisis estático de código |
| **Mend (WhiteSource)** | Cloud | Adopt | Escaneo de vulnerabilidades de código abierto |

## 6. CI/CD

| Tecnología | Propósito |
|---|---|
| **Azure DevOps Pipelines** | Orquestación CI/CD |
| **Docker BuildX** | Construcción de imágenes de contenedor multi-arquitectura |
| **Cosign** | Firma de imágenes de contenedor |

## 7. Documentación

| Tecnología | Propósito |
|---|---|
| **MkDocs** | Generador de sitios estáticos |
| **Material for MkDocs** | Tema e interfaz de usuario |
| **Mermaid** | Diagramas como código |
| **OpenAPI 3.1** | Especificación de API |
| **Swagger UI** | Exploración interactiva de API (solo desarrollo) |

## Matriz de Compatibilidad de Versiones

| Versión de .NET | Versión de C# | ASP.NET Core | Soporte Hasta |
|---|---|---|---|
| 10.0 | 13 | 10.0 | LTS — Nov 2027 |
| 9.0 | 13 | 9.0 | STS — May 2026 |
| 8.0 | 12 | 8.0 | LTS — Nov 2026 |

> **Objetivo actual:** .NET 10.0

## Política de Actualización de Dependencias

| Tipo de Actualización | Frecuencia | Aprobación |
|---|---|---|
| **Parche (seguridad)** | Dentro de 48 horas | Fusión automática si CI pasa |
| **Parche (no seguridad)** | Semanal | Fusión automática |
| **Menor** | Mensual | Revisión del líder del equipo |
| **Mayor** | Trimestral (planificada) | Revisión de arquitectura |
| **.NET SDK** | Dentro de 2 semanas | Validación CI + staging |

## Documentos Relacionados

- [Visión General de la Arquitectura](overview.md)
- [ADR-001 — PostgreSQL sobre MongoDB](../adr/ADR-001.md)
- [Estándares de Codificación](../decisions/coding-standards.md)
- [Directrices de Seguridad](../decisions/security-guidelines.md)
