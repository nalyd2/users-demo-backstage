# Generated Resource Inventories

Cloud resource metadata produced by CI/CD for the Users Service.

| Artifact | Description |
|---|---|
| `azure-resources.json` | Full Azure resource inventory via Resource Graph — Azure Database for PostgreSQL (Flexible Server), Azure Service Bus (Premium), Azure Key Vault (Standard), AKS (Standard_D4s_v5), Azure Container Registry, Azure Traffic Manager |
| `cost-estimate.json` | Monthly cost estimate based on SKU configuration — PostgreSQL General Purpose (4 vCores, 16 GB RAM), Service Bus Premium, Key Vault Standard, AKS node pool |
| `resource-tags.csv` | Tag compliance report — validates that all Users Service resources carry mandatory tags: `service: users-service`, `system: user-management-system`, `domain: identity`, `owner: platform-engineering`, `environment`, `cost-center` |

**Not committed.**
