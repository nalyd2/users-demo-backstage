# Artefactos OpenAPI Generados

Este directorio es poblado por el pipeline CI/CD durante la etapa **Generar OpenAPI**.

## Archivos Generados

| Artefacto | Descripción |
|---|---|
| `users-service-openapi.yaml` | Especificación OpenAPI 3.1 empaquetada con todas las `$ref` resueltas |
| `users-service-openapi.json` | Versión JSON para compatibilidad con herramientas |
| `users-service-client-csharp/` | SDK de cliente C# generado automáticamente (vía Kiota) |
| `users-service-client-typescript/` | SDK de cliente TypeScript generado automáticamente |

## Pipeline

Consulte [`generated/README.md`](../README.md) para la definición completa del pipeline.

```yaml
# Azure DevOps task excerpt
- script: |
    dotnet tool install -g Microsoft.dotnet-openapi
    dotnet openapi bundle \
      --input openapi.yaml \
      --output generated/openapi/users-service-openapi.yaml \
      --resolve-external-references
  displayName: "Bundle OpenAPI spec"
```

**Estos archivos no están confirmados en el repositorio.**
