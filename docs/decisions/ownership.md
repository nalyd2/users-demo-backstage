# Service Ownership Model — Users Service

- **Status:** Approved
- **Date:** 2026-07-20
- **Decision-makers:** Platform Engineering Team, Engineering Leadership

## Owning Team

**Platform Engineering** is the sole owning team for the Users Service, co-owned with the Auth Service under the same team's responsibility.

| Role | Name / Handle |
|---|---|
| Primary Owner | (TBD) |
| Secondary Owner | (TBD) |
| Team Lead | (TBD) |

**Scope of ownership:**
- Full lifecycle: architecture, implementation, testing, deployment, observability, capacity planning.
- All components: API, event consumers, background workers, database schema.
- Tenant configuration and multi-tenancy support.
- RBAC model and permission definitions.
- Integration with Auth Service (JWT validation) and Microsoft Graph API.

## Contact Channels

| Channel | Address | Purpose |
|---|---|---|
| GitHub Issues | `users-demo-backstage/issues` | Bug reports, feature requests |
| Pull Requests | `users-demo-backstage/pulls` | Code changes |
| Slack | `#platform-engineering` | Real-time questions, coordination |
| Email | `platform-engineering@company.com` | Formal requests, security disclosures |
| Office Hours | Thursdays 15:00-16:00 UTC | Drop-in support |

## On-Call Rotation

- **Schedule:** Weekly, Monday-to-Monday, rotating among Platform Engineering members.
- **Coverage:** 24x7 during on-call week.
- **Pager:** PagerDuty schedule `plateng-users-service`. Alerts on:
  - Production P0/P1 incidents.
  - Auth Service dependency alerts (Users Service cannot function without Auth Service).
  - High error rate or event processing lag.
- **Hand-off:** Every Monday at 09:00 UTC with written summary in Slack.

## Escalation Path

| Tier | Responder | Response Time | Trigger |
|---|---|---|---|
| T1 | On-call Platform Engineer | <= 15 min | PagerDuty alert, P0/P1 incident |
| T2 | Platform Engineering Team Lead | <= 30 min | T1 not resolved in 45 min |
| T3 | Director of Engineering | <= 60 min | T2 not resolved, customer-facing outage |
| T4 | VP Engineering | <= 120 min | Extended outage exceeding SLO error budget |

## Contribution Guidelines for External Teams

1. **Pull request required** — reviewed by primary or secondary owner.
2. **Issue first** — open a GitHub issue describing the proposal before coding.
3. **Follow conventions** — coding standards, linting, validation.
4. **Branch naming** — `feat/`, `fix/`, `docs/` prefixes.
5. **Commit messages** — Conventional Commits format.
6. **No new runtime dependencies** without prior discussion.
7. **SLO commitment** — owning team may revert changes that negatively affect SLOs.

## Service Level Objectives

### Availability

| Indicator | Target | Measurement |
|---|---|---|
| Uptime (health endpoint) | >= 99.95% | External synthetic probe every 60s |
| API success rate | >= 99.5% | Ratio of 2xx/4xx/5xx responses |

### Latency (p99)

| Endpoint | Target |
|---|---|
| User CRUD (single) | <= 300 ms |
| User list (paginated) | <= 500 ms |
| Event processing | <= 100 ms per event |
| Graph API enrichment | <= 1000 ms (async, non-blocking) |

### Freshness

| Indicator | Target |
|---|---|
| Auth event consumption lag | <= 15 seconds |
| User event propagation | <= 10 seconds |

## Error Budget

- Monthly error budget: 100% - 99.95% = ~21 minutes of allowed downtime.
- Budget tracked in Grafana dashboard `users-service-error-budget`.
- When error budget falls below 20%, non-critical changes are frozen.
- When exhausted, blameless postmortem and recovery plan required.

## Monitoring & Alerting

- All SLOs instrumented via Prometheus metrics (`users_*`).
- Alerts in PagerDuty on critical burn rate.
- Dashboards in `Users Service` folder in Grafana.
- Separate dashboard for auth event processing lag and Graph API health.
