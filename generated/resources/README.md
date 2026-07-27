# Inventarios de Recursos Generados

Metadatos de recursos en la nube producidos por CI/CD para el Servicio de Usuarios.

| Artefacto | Descripción |
|---|---|
| `azure-resources.json` | Inventario completo de recursos de Azure vía Resource Graph — Azure Database for PostgreSQL (Servidor Flexible), Azure Service Bus (Premium), Azure Key Vault (Estándar), AKS (Standard_D4s_v5), Azure Container Registry, Azure Traffic Manager |
| `cost-estimate.json` | Estimación de costo mensual basada en la configuración de SKU — PostgreSQL Propósito General (4 vCores, 16 GB RAM), Service Bus Premium, Key Vault Estándar, grupo de nodos AKS |
| `resource-tags.csv` | Informe de cumplimiento de etiquetas — valida que todos los recursos del Servicio de Usuarios tengan las etiquetas obligatorias: `service: users-service`, `system: user-management-system`, `domain: identity`, `owner: platform-engineering`, `environment`, `cost-center` |

**No confirmado en el repositorio.**
