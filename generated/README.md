# Artefactos Generados

## Visión General

Este directorio contiene artefactos **generados automáticamente** por el pipeline CI/CD. Estos archivos nunca se confirman en el repositorio — se producen durante la compilación y se publican como artefactos del pipeline o se despliegan en Backstage.

## Estructura del Directorio

```
generated/
├── README.md           # Este archivo
├── openapi/            # Paquetes OpenAPI generados y SDKs de cliente
├── quality/            # Informes de calidad de código y resultados de pruebas
├── metadata/           # Enriquecimiento de metadatos del catálogo
├── topology/           # Topología de infraestructura y dependencias
└── resources/          # Inventarios de recursos en la nube
```

## Pipeline de Generación

Las siguientes etapas del pipeline de Azure DevOps generan estos artefactos:

```yaml
# Excerpt from azure-pipelines.yml

stages:
  # Stage 1: Build & Test
  - stage: Build
    jobs:
      - job: BuildAndTest
        steps:
          - task: DotNetCoreCLI@2
            displayName: "dotnet restore"
            # ...

  # Stage 2: Generate Artifacts
  - stage: GenerateArtifacts
    dependsOn: Build
    jobs:
      - job: GenerateOpenAPI
        # Generates: generated/openapi/
      - job: GenerateQualityReports
        # Generates: generated/quality/
      - job: GenerateMetadata
        # Generates: generated/metadata/
      - job: GenerateTopology
        # Generates: generated/topology/

  # Stage 3: Publish
  - stage: Publish
    dependsOn: GenerateArtifacts
    jobs:
      - job: PublishArtifacts
        # Publishes to Azure Artifacts and Backstage
```

## Catálogo de Artefactos

### `generated/openapi/`

| Artefacto | Formato | Consumidor | Descripción |
|---|---|---|---|
| `users-service-openapi.yaml` | OpenAPI 3.1 | Backstage, API Gateway | Especificación OpenAPI empaquetada con todas las `$ref` resueltas |
| `users-service-openapi.json` | OpenAPI 3.1 (JSON) | Swagger UI, Power Platform | Versión JSON para herramientas que no soportan YAML |
| `users-service-client-csharp/` | SDK C# | Consumidores .NET internos | Cliente HTTP C# generado automáticamente (Kiota/NSwag) |
| `users-service-client-typescript/` | SDK TypeScript | Aplicaciones frontend | Cliente TypeScript generado automáticamente |

### `generated/quality/`

| Artefacto | Formato | Consumidor | Descripción |
|---|---|---|---|
| `test-results.xml` | XML NUnit | Informes de Pruebas Azure DevOps | Resultados de pruebas unitarias y de integración |
| `code-coverage.xml` | XML Cobertura | SonarQube | Informe de cobertura de líneas y ramas |
| `sonarqube-report.json` | JSON | SonarQube Cloud | Resultados completos de análisis estático |
| `dependency-scan-results.json` | JSON | Panel Mend | Resultados de escaneo de vulnerabilidades conocidas |
| `benchmark-results.md` | Markdown | TechDocs | Comparativa de rendimiento de benchmarks (CRUD de perfiles, paginación, respaldo de validación JWT) |
| `lint-results.json` | JSON | Puerta PR | Resultados del analizador EditorConfig y Roslyn |

### `generated/metadata/`

| Artefacto | Formato | Consumidor | Descripción |
|---|---|---|---|
| `catalog-enriched.yaml` | Catálogo Backstage | Backstage | `catalog-info.yaml` enriquecido con datos dinámicos (números de compilación, marcas de tiempo de despliegue) |
| `api-versions.json` | JSON | Plugin API de Backstage | Metadatos de versiones activas de la API |
| `component-owners.json` | JSON | Plugin de Organización de Backstage | Datos de propiedad validados |

### `generated/topology/`

| Artefacto | Formato | Consumidor | Descripción |
|---|---|---|---|
| `dependency-graph.json` | JSON | TechDocs de Backstage | Grafo de dependencias del servicio para visualización (depende de auth-service, PostgreSQL, Service Bus) |
| `network-policy-report.json` | JSON | InfoSec | Auditoría de políticas de red de Kubernetes |
| `component-map.svg` | SVG | TechDocs | Diagrama de componentes C4 generado automáticamente |

### `generated/resources/`

| Artefacto | Formato | Consumidor | Descripción |
|---|---|---|---|
| `azure-resources.json` | JSON | Azure Resource Graph | Inventario de todos los recursos de Azure para este servicio (PostgreSQL, Service Bus, Key Vault, AKS, ACR) |
| `cost-estimate.json` | JSON | Panel FinOps | Estimación de costo mensual basada en las SKUs actuales |
| `resource-tags.csv` | CSV | Azure Policy | Informe de cumplimiento de etiquetas de recursos |

## Ejemplos de Pipeline (Azure DevOps)

### Generando Paquete OpenAPI

```yaml
- job: GenerateOpenAPI
  displayName: "Generate OpenAPI Artifacts"
  pool:
    vmImage: ubuntu-latest
  steps:
    - task: UseDotNet@2
      inputs:
        version: "10.x"

    - script: |
        dotnet tool install -g Microsoft.dotnet-openapi
        dotnet openapi bundle \
          --input openapi.yaml \
          --output generated/openapi/users-service-openapi.yaml \
          --resolve-external-references
      displayName: "Bundle OpenAPI spec"

    - script: |
        npm install -g @apidevtools/swagger-cli
        swagger-cli bundle openapi.yaml \
          --outfile generated/openapi/users-service-openapi.yaml \
          --type yaml
      displayName: "Validate and bundle with swagger-cli"

    - task: PublishPipelineArtifact@1
      inputs:
        targetPath: generated/openapi/
        artifactName: openapi-specs
```

### Generando SDK de Cliente C#

```yaml
- job: GenerateClientSDK
  displayName: "Generate C# Client SDK"
  steps:
    - script: |
        dotnet new console -n UsersService.Client -o generated-sdk
        cd generated-sdk
        dotnet add package Microsoft.Kiota.Http.HttpClientLibrary
        # Generate client from OpenAPI spec
        kiota generate \
          --openapi ../generated/openapi/users-service-openapi.yaml \
          --language CSharp \
          --namespace-name Platform.UsersService.Client \
          --output ./Generated
      displayName: "Generate C# client with Kiota"

    - task: PublishPipelineArtifact@1
      inputs:
        targetPath: generated-sdk/
        artifactName: client-sdk-csharp
```

### Generando Informes de Calidad

```yaml
- job: GenerateQualityReports
  displayName: "Generate Quality Reports"
  steps:
    - script: |
        dotnet test tests/UsersService.Tests/ \
          --logger "trx;LogFileName=test-results.xml" \
          --collect:"XPlat Code Coverage" \
          --results-directory generated/quality/
      displayName: "Run tests with coverage"

    - script: |
        dotnet tool install -g dotnet-reportgenerator-globaltool
        reportgenerator \
          -reports:generated/quality/**/coverage.cobertura.xml \
          -targetdir:generated/quality/coverage-report \
          -reporttypes:Html,Cobertura
      displayName: "Generate coverage report"

    - task: PublishPipelineArtifact@1
      inputs:
        targetPath: generated/quality/
        artifactName: quality-reports
```

## Integración con Backstage

### TechDocs

Los paquetes OpenAPI generados se renderizan en Backstage a través del plugin de API:

```yaml
# catalog-info.yaml — API entity
spec:
  type: openapi
  definition:
    $text: ./generated/openapi/users-service-openapi.yaml
```

### Enriquecimiento del Catálogo

El pipeline enriquece `catalog-info.yaml` con metadatos dinámicos antes del registro:

```yaml
- job: EnrichCatalog
  displayName: "Enrich Catalog Metadata"
  steps:
    - script: |
        # Add build information to catalog
        yq eval '.metadata.annotations."backstage.io/build-number" = "$(Build.BuildNumber)"' \
          catalog-info.yaml > generated/metadata/catalog-enriched.yaml
        yq eval '.metadata.annotations."backstage.io/last-deployed" = "$(date -u +%Y-%m-%dT%H:%M:%SZ)"' \
          generated/metadata/catalog-enriched.yaml
      displayName: "Enrich catalog with build metadata"

    - task: PublishPipelineArtifact@1
      inputs:
        targetPath: generated/metadata/
        artifactName: catalog-metadata
```

## Documentos Relacionados

- [Configuración de Azure Pipelines](../azure-pipelines.yml)
- [Configuración de TechDocs](../mkdocs.yml)
- [Runbook de Operaciones](../docs/runbooks/operations.md)
