# Generated Artifacts

## Overview

This directory contains artifacts **automatically generated** by the CI/CD pipeline. These files are never committed to the repository — they are produced during the build and published as pipeline artifacts or deployed to Backstage.

## Directory Structure

```
generated/
├── README.md           # This file
├── openapi/            # Generated OpenAPI bundles and client SDKs
├── quality/            # Code quality reports and test results
├── metadata/           # Catalog metadata enrichment
├── topology/           # Infrastructure and dependency topology
└── resources/          # Cloud resource inventories
```

## Generation Pipeline

The following Azure DevOps pipeline stages generate these artifacts:

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

## Artifact Catalog

### `generated/openapi/`

| Artifact | Format | Consumer | Description |
|---|---|---|---|
| `users-service-openapi.yaml` | OpenAPI 3.1 | Backstage, API Gateway | Bundled OpenAPI spec with all `$ref` resolved |
| `users-service-openapi.json` | OpenAPI 3.1 (JSON) | Swagger UI, Power Platform | JSON version for tools that don't support YAML |
| `users-service-client-csharp/` | C# SDK | Internal .NET consumers | Auto-generated C# HTTP client (Kiota/NSwag) |
| `users-service-client-typescript/` | TypeScript SDK | Frontend apps | Auto-generated TypeScript client |

### `generated/quality/`

| Artifact | Format | Consumer | Description |
|---|---|---|---|
| `test-results.xml` | NUnit XML | Azure DevOps Test Reports | Unit + integration test results |
| `code-coverage.xml` | Cobertura XML | SonarQube | Line and branch coverage report |
| `sonarqube-report.json` | JSON | SonarQube Cloud | Full static analysis results |
| `dependency-scan-results.json` | JSON | Mend Dashboard | Known vulnerability scan results |
| `benchmark-results.md` | Markdown | TechDocs | Performance benchmark comparison (profile CRUD, pagination, JWT validation fallback) |
| `lint-results.json` | JSON | PR gate | EditorConfig and Roslyn analyzer results |

### `generated/metadata/`

| Artifact | Format | Consumer | Description |
|---|---|---|---|
| `catalog-enriched.yaml` | Backstage Catalog | Backstage | `catalog-info.yaml` enriched with dynamic data (build numbers, deployment timestamps) |
| `api-versions.json` | JSON | Backstage API plugin | Active API version metadata |
| `component-owners.json` | JSON | Backstage Org plugin | Validated ownership data |

### `generated/topology/`

| Artifact | Format | Consumer | Description |
|---|---|---|---|
| `dependency-graph.json` | JSON | Backstage TechDocs | Service dependency graph for visualization (dependsOn auth-service, PostgreSQL, Service Bus) |
| `network-policy-report.json` | JSON | InfoSec | Kubernetes network policy audit |
| `component-map.svg` | SVG | TechDocs | Auto-generated C4 component diagram |

### `generated/resources/`

| Artifact | Format | Consumer | Description |
|---|---|---|---|
| `azure-resources.json` | JSON | Azure Resource Graph | Inventory of all Azure resources for this service (PostgreSQL, Service Bus, Key Vault, AKS, ACR) |
| `cost-estimate.json` | JSON | FinOps Dashboard | Monthly cost estimate based on current SKUs |
| `resource-tags.csv` | CSV | Azure Policy | Resource tag compliance report |

## Pipeline Examples (Azure DevOps)

### Generating OpenAPI Bundle

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

### Generating C# Client SDK

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

### Generating Quality Reports

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

## Integration with Backstage

### TechDocs

The generated OpenAPI bundles are rendered in Backstage via the API plugin:

```yaml
# catalog-info.yaml — API entity
spec:
  type: openapi
  definition:
    $text: ./generated/openapi/users-service-openapi.yaml
```

### Catalog Enrichment

The pipeline enriches `catalog-info.yaml` with dynamic metadata before registration:

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

## Related Documents

- [Azure Pipelines Configuration](../azure-pipelines.yml)
- [TechDocs Configuration](../mkdocs.yml)
- [Operations Runbook](../docs/runbooks/operations.md)
