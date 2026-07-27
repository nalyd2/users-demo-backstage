# Generated OpenAPI Artifacts

This directory is populated by the CI/CD pipeline during the **Generate OpenAPI** stage.

## Generated Files

| Artifact | Description |
|---|---|
| `users-service-openapi.yaml` | Bundled OpenAPI 3.1 spec with all `$ref` resolved |
| `users-service-openapi.json` | JSON version for tool compatibility |
| `users-service-client-csharp/` | Auto-generated C# client SDK (via Kiota) |
| `users-service-client-typescript/` | Auto-generated TypeScript client SDK |

## Pipeline

See [`generated/README.md`](../README.md) for the full pipeline definition.

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

**These files are not committed to the repository.**
