# Estrategia de Ramas — Users Service

## Modelo: Desarrollo Basado en Tronco (Trunk-Based Development)

El Users Service sigue un modelo de desarrollo basado en tronco con ramas de característica de corta duración, idéntico al Auth Service. Esto garantiza una experiencia de desarrollo consistente en toda la plataforma.

## Convención de Nomenclatura de Ramas

| Tipo de Rama | Patrón | Ejemplo |
|---|---|---|
| Característica | `feature/<issue-number>-<kebab-description>` | `feature/142-graph-profile-enrichment` |
| Corrección de error | `fix/<issue-number>-<kebab-description>` | `fix/89-tenant-filter-missing` |
| Hotfix (producción) | `hotfix/<issue-number>-<kebab-description>` | `hotfix/201-soft-delete-data-loss` |
| Tarea | `chore/<issue-number>-<kebab-description>` | `chore/305-upgrade-npgsql` |
| Refactorización | `refactor/<issue-number>-<kebab-description>` | `refactor/178-extract-event-consumer` |

Todas las ramas se crean a partir de `main` y se fusionan mediante squash-merge pull requests. Duración máxima de la rama: 3 días.

## Requisitos de Pull Request

- **Título:** Formato de commit convencional: `type(scope): description` (ej., `feat(user): add Graph API profile enrichment`).
- **Descripción:** Resumen de cambios, instrucciones de prueba, issues relacionados.
- **Tamaño:** Máximo 400 líneas cambiadas (excluyendo archivos generados y pruebas).
- **Lista de verificación:** Todos los elementos de la lista de verificación de revisión de código de los estándares de codificación deben cumplirse.
- **Etiquetas:** Como mínimo `area/<component>` (ej., `area/user-service`, `area/event-consumer`).
- **Etiquetas:** Incluir `rbac-review` si los permisos de endpoint cambiaron, `db-migration` si hay cambios de esquema.

## Reglas de Protección de Rama (main)

- Requerir pull request antes de fusionar.
- Requerir al menos 2 aprobaciones (una debe ser de un ingeniero senior).
- Invalidar revisiones obsoletas en nuevos commits.
- Requerir verificaciones de estado: compilación, pruebas unitarias, pruebas de integración, cobertura (>= 80%), calidad SonarQube.
- Requerir que las ramas estén actualizadas antes de fusionar.
- Restringir acceso de push al equipo de Platform Engineering.

## Pipeline CI/CD

```
Push a rama de característica → Compilación → Pruebas unitarias → Pruebas de integración (Testcontainers) → Cobertura → SonarQube → Escaneo Mend
Fusión a main → Compilación → Pruebas → Cobertura → SonarQube → Escaneo Mend → Despliegue a staging
Tag de release → Despliegue a producción (puerta de aprobación manual)
```

## Tags de Release

- Formato: `v<major>.<minor>.<patch>` (ver versioning.md).
- Los tags se crean desde `main` después de que el commit de release se fusiona.
- Los tags son inmutables una vez publicados.

```bash
git tag -a v1.2.3 -m "Release v1.2.3"
git push origin v1.2.3
```

## Proceso de Hotfix

1. Crear rama desde el tag de release afectado: `git checkout -b hotfix/description v<version>`.
2. Corregir con el cambio mínimo posible.
3. Abrir PR apuntando a `main` (aprobación de un solo revisor suficiente para hotfixes).
4. Después del squash-merge a `main`, etiquetar la nueva versión patch.
5. Desplegar el tag patch a producción.
6. Si el hotfix aborda una vulnerabilidad de seguridad, seguir el cronograma acelerado (despliegue el mismo día).

## Política de Squash Merge

Todas las ramas de característica, corrección y tarea DEBEN fusionarse con squash-merge. Los mensajes de commit en `main` siguen:

```
type(scope): description (#PR-number)
```

Ejemplos:
```
feat(user): add Microsoft Graph profile enrichment (#142)
fix(tenant): correct tenant isolation in bulk query (#89)
chore(deps): update Npgsql to 9.0.1 (#305)
```

## Tipos de Commit Convencionales

| Tipo | Uso |
|---|---|
| `feat` | Nueva característica o endpoint |
| `fix` | Corrección de error |
| `chore` | Mantenimiento, dependencias, herramientas |
| `refactor` | Reestructuración de código sin cambio de comportamiento |
| `test` | Adiciones o cambios de pruebas |
| `docs` | Cambios de documentación |
| `perf` | Mejoras de rendimiento |
| `ci` | Cambios en pipeline CI/CD |
| `db` | Migración de base de datos o cambio de esquema |

## Proceso de Cambio de Emergencia

Para vulnerabilidades de seguridad críticas o interrupciones de producción:

1. El líder de ingeniería autoriza la omisión de la revisión estándar (al menos un ingeniero senior debe revisar aún).
2. Despliegue monitoreado por el ingeniero de guardia durante 30 minutos posteriores al despliegue.
3. Post-mortem creado dentro de las 24 horas.
