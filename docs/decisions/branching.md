# Branching Strategy — Users Service

## Model: Trunk-Based Development

The Users Service follows a trunk-based development model with short-lived feature branches, identical to the Auth Service. This ensures consistent developer experience across the platform.

## Branch Naming Convention

| Branch Type | Pattern | Example |
|---|---|---|
| Feature | `feature/<issue-number>-<kebab-description>` | `feature/142-graph-profile-enrichment` |
| Bug fix | `fix/<issue-number>-<kebab-description>` | `fix/89-tenant-filter-missing` |
| Hotfix (prod) | `hotfix/<issue-number>-<kebab-description>` | `hotfix/201-soft-delete-data-loss` |
| Chore | `chore/<issue-number>-<kebab-description>` | `chore/305-upgrade-npgsql` |
| Refactor | `refactor/<issue-number>-<kebab-description>` | `refactor/178-extract-event-consumer` |

All branches branch from `main` and are merged via squash-merge pull requests. Maximum branch lifetime: 3 days.

## Pull Request Requirements

- **Title:** Conventional commit format: `type(scope): description` (e.g., `feat(user): add Graph API profile enrichment`).
- **Description:** Summary of changes, testing instructions, related issues.
- **Size:** Maximum 400 lines changed (excluding generated files and tests).
- **Checklist:** All items from coding standards code review checklist must be satisfied.
- **Labels:** At minimum `area/<component>` (e.g., `area/user-service`, `area/event-consumer`).
- **Labels:** Include `rbac-review` if endpoint permissions changed, `db-migration` if schema changes.

## Branch Protection Rules (main)

- Require pull request before merging.
- Require at least 2 approvals (one must be from a senior engineer).
- Dismiss stale reviews on new commits.
- Require status checks: build, unit tests, integration tests, coverage (>= 80%), SonarQube quality gate.
- Require branches to be up to date before merging.
- Restrict push access to Platform Engineering team.

## CI/CD Pipeline

```
Push to feature branch → Build → Unit tests → Integration tests (Testcontainers) → Coverage → SonarQube → Mend scan
Merge to main → Build → Tests → Coverage → SonarQube → Mend scan → Deploy to staging
Tag release → Deploy to production (manual approval gate)
```

## Release Tags

- Format: `v<major>.<minor>.<patch>` (see versioning.md).
- Tags are created from `main` after release commit is merged.
- Tags are immutable once pushed.

```bash
git tag -a v1.2.3 -m "Release v1.2.3"
git push origin v1.2.3
```

## Hotfix Process

1. Branch from the affected release tag: `git checkout -b hotfix/description v<version>`.
2. Fix with the minimal possible change.
3. Open PR targeting `main` (single reviewer approval sufficient for hotfixes).
4. After squash-merge to `main`, tag the new patch version.
5. Deploy the patch tag to production.
6. If the hotfix addresses a security vulnerability, follow the accelerated timeline (same-day deploy).

## Squash Merge Policy

All feature, fix, and chore branches MUST be squash-merged. Commit messages on `main` follow:

```
type(scope): description (#PR-number)
```

Examples:
```
feat(user): add Microsoft Graph profile enrichment (#142)
fix(tenant): correct tenant isolation in bulk query (#89)
chore(deps): update Npgsql to 9.0.1 (#305)
```

## Conventional Commit Types

| Type | Usage |
|---|---|
| `feat` | New feature or endpoint |
| `fix` | Bug fix |
| `chore` | Maintenance, dependencies, tooling |
| `refactor` | Code restructuring without behavior change |
| `test` | Test additions or changes |
| `docs` | Documentation changes |
| `perf` | Performance improvements |
| `ci` | CI/CD pipeline changes |
| `db` | Database migration or schema change |

## Emergency Change Process

For critical security vulnerabilities or production outages:

1. Engineering lead authorizes bypass of standard review (minimum one senior engineer must still review).
2. Deployment monitored by on-call engineer for 30 minutes post-deploy.
3. Post-mortem created within 24 hours.
