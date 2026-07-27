# Users Service (`users-service`)

> **Parte de la Plataforma Interna de Desarrollo (IDP)** — Dominio de Gestion de Usuarios.
> Registrado en Backstage via `catalog-info.yaml`. Documentacion renderizada con TechDocs.

[![License](https://img.shields.io/badge/license-MIT-blue.svg)](./LICENSE)
[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4.svg)](https://dotnet.microsoft.com/)
[![Lifecycle](https://img.shields.io/badge/lifecycle-production-green.svg)](./catalog-info.yaml)
[![Backstage](https://img.shields.io/badge/Backstage-Registered-6868FF.svg)](./catalog-info.yaml)

---

## 📖 Descripcion General

El **Users Service** (`users-service`) gestiona el ciclo de vida completo de los usuarios para la Plataforma Interna de Desarrollo (IDP). Proporciona operaciones CRUD sobre perfiles de usuario, se integra con el [Authentication Service](https://backstage.internal/platform/component/auth-service) para la validacion de JWT, y consume eventos de autenticacion para mantener una vision actualizada de la actividad del usuario.

| Atributo | Valor |
|---|---|
| **Sistema** | `user-management-system` |
| **Dominio** | `identity` |
| **Propietario** | Platform Engineering Team |
| **Ciclo de Vida** | `production` |
| **Tecnologia** | .NET 10, ASP.NET Core Minimal APIs |
| **Autenticacion** | Validacion JWT via Auth Service |
| **Base de Datos** | PostgreSQL (Users DB) |

---

## 🧭 Enlaces Rapidos

| Recurso | Ubicacion |
|---|---|
| 📘 **TechDocs** (renderizado en Backstage) | [`docs/index.md`](./docs/index.md) |
| 🗂️ **Definicion del Catalogo** | [`catalog-info.yaml`](./catalog-info.yaml) |
| 🔌 **Especificacion OpenAPI 3.1** | [`openapi.yaml`](./openapi.yaml) |
| 📐 **Documentos de Arquitectura** | [`docs/architecture/`](./docs/architecture/) |
| 📋 **Runbooks** | [`docs/runbooks/`](./docs/runbooks/) |
| 🔧 **Onboarding** | [`docs/onboarding/`](./docs/onboarding/) |
| 📜 **ADRs** | [`docs/adr/`](./docs/adr/) |
| 🧠 **Preparacion IA / RAG** | [`knowledge/`](./knowledge/) |
| 📦 **Artefactos Generados** | [`generated/`](./generated/) |
| 🚀 **Pipeline CI/CD** | [`azure-pipelines.yml`](./azure-pipelines.yml) |

---

## 👥 Endpoints

| Metodo | Ruta | Descripcion |
|---|---|---|
| `GET`    | `/api/users`        | Listar todos los usuarios (paginado, filtrable) |
| `GET`    | `/api/users/{id}`   | Obtener un usuario por ID |
| `POST`   | `/api/users`        | Crear un nuevo perfil de usuario |
| `PUT`    | `/api/users/{id}`   | Actualizar un perfil de usuario existente |
| `DELETE` | `/api/users/{id}`   | Eliminacion logica de un perfil de usuario |
| `GET`    | `/api/health`       | Sonda de actividad y disponibilidad |

Todos los endpoints de modificacion requieren un JWT valido emitido por el Authentication Service.

Referencia completa de la API: [`docs/api/users-api.md`](./docs/api/users-api.md)

---

## 🔗 Relaciones de la Plataforma

```
                      DependsOn
users-service ─────────────────────► auth-service
                           Validacion JWT via POST /api/auth/refresh

                      ConsumesApi
users-service ─────────────────────► auth-api

                      Subscribes
users-service ─────────────────────► auth-service
               Eventos user.login, user.logout
```

Consulte [`docs/decisions/dependencies.md`](./docs/decisions/dependencies.md) para ver el mapa completo de dependencias.

---

## 🔑 Flujo de Autenticacion

```
Cliente                     Users Service              Auth Service
  │                              │                          │
  │  GET /api/users              │                          │
  │  (Authorization: Bearer JWT) │                          │
  │─────────────────────────────►│                          │
  │                              │                          │
  │                              │ Validar JWT              │
  │                              │ (clave publica RS256 local) │
  │                              │                          │
  │                              │ (si JWT expiro)          │
  │                              │ POST /api/auth/refresh   │
  │                              │─────────────────────────►│
  │                              │◄─────────────────────────│
  │                              │                          │
  │◄─────────────────────────────│                          │
  │  200 OK + [usuarios]         │                          │
```

---

## 🚀 Primeros Pasos (Local)

```bash
# Prerrequisitos: SDK .NET 10
dotnet restore src/UsersService/UsersService.csproj
dotnet run --project src/UsersService/UsersService.csproj

# El servicio escucha en https://localhost:7201
# Swagger UI: https://localhost:7201/swagger
```

Instrucciones detalladas: [`docs/onboarding/local-development.md`](./docs/onboarding/local-development.md)

---

## 🧱 Estructura del Repositorio

```
.
├── src/                    # Codigo fuente de la aplicacion (.NET 10)
├── tests/                  # Pruebas unitarias y de integracion
├── docs/                   # Toda la documentacion (TechDocs)
├── generated/              # Artefactos generados por CI/CD
├── knowledge/              # Preparacion para indexacion IA/RAG
├── catalog-info.yaml       # Registro de entidad en Backstage
├── mkdocs.yml              # Configuracion de TechDocs / MkDocs
├── openapi.yaml            # Especificacion OpenAPI 3.1
├── azure-pipelines.yml     # Pipeline CI/CD (Azure DevOps)
├── Dockerfile              # Definicion de imagen de contenedor
└── .editorconfig           # Convenciones de estilo de codigo compartidas
```

---

## 📊 Contexto de la Plataforma

Este servicio es un componente de una plataforma empresarial mas grande. La documentacion en este repositorio asume la existencia de otros servicios — particularmente el Authentication Service — infraestructura y herramientas que pueden no estar presentes en esta implementacion de referencia independiente. Para la arquitectura completa de la plataforma, consulte [`docs/architecture/context.md`](./docs/architecture/context.md).

---

## 📄 Licencia

MIT — consulte [LICENSE](./LICENSE).

---

_Mantenido por el Platform Engineering Team. Para preguntas, abra un issue o contacte a `#platform-eng` en Slack._
