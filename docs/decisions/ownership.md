# Modelo de Propiedad del Servicio — Users Service

- **Estado:** Aprobado
- **Fecha:** 2026-07-20
- **Tomadores de decisión:** Equipo de Platform Engineering, Liderazgo de Ingeniería

## Equipo Propietario

**Platform Engineering** es el único equipo propietario del Users Service, copropietario junto con el Auth Service bajo la misma responsabilidad del equipo.

| Rol | Nombre / Handle |
|---|---|
| Propietario Principal | (TBD) |
| Propietario Secundario | (TBD) |
| Líder del Equipo | (TBD) |

**Alcance de la propiedad:**
- Ciclo de vida completo: arquitectura, implementación, pruebas, despliegue, observabilidad, planificación de capacidad.
- Todos los componentes: API, consumidores de eventos, trabajadores en segundo plano, esquema de base de datos.
- Configuración de inquilino y soporte de multi-tenencia.
- Modelo RBAC y definiciones de permisos.
- Integración con Auth Service (validación JWT) y Microsoft Graph API.

## Canales de Contacto

| Canal | Dirección | Propósito |
|---|---|---|
| GitHub Issues | `users-demo-backstage/issues` | Reportes de errores, solicitudes de características |
| Pull Requests | `users-demo-backstage/pulls` | Cambios de código |
| Slack | `#platform-engineering` | Preguntas en tiempo real, coordinación |
| Correo Electrónico | `platform-engineering@company.com` | Solicitudes formales, divulgaciones de seguridad |
| Horario de Oficina | Jueves 15:00-16:00 UTC | Soporte sin cita previa |

## Rotación de Guardia

- **Horario:** Semanal, de lunes a lunes, rotando entre miembros de Platform Engineering.
- **Cobertura:** 24x7 durante la semana de guardia.
- **Pager:** Horario de PagerDuty `plateng-users-service`. Alertas sobre:
  - Incidentes de producción P0/P1.
  - Alertas de dependencia del Auth Service (Users Service no puede funcionar sin Auth Service).
  - Alta tasa de error o retraso en el procesamiento de eventos.
- **Transferencia:** Cada lunes a las 09:00 UTC con resumen escrito en Slack.

## Ruta de Escalación

| Nivel | Respondedor | Tiempo de Respuesta | Disparador |
|---|---|---|---|
| T1 | Ingeniero de Platform de guardia | <= 15 min | Alerta de PagerDuty, incidente P0/P1 |
| T2 | Líder del Equipo de Platform Engineering | <= 30 min | T1 no resuelto en 45 min |
| T3 | Director de Ingeniería | <= 60 min | T2 no resuelto, interrupción visible para el cliente |
| T4 | VP de Ingeniería | <= 120 min | Interrupción prolongada que excede el presupuesto de errores del SLO |

## Directrices de Contribución para Equipos Externos

1. **Pull request requerido** — revisado por el propietario principal o secundario.
2. **Issue primero** — abrir un issue de GitHub describiendo la propuesta antes de codificar.
3. **Seguir convenciones** — estándares de codificación, linting, validación.
4. **Nomenclatura de ramas** — prefijos `feat/`, `fix/`, `docs/`.
5. **Mensajes de commit** — formato Conventional Commits.
6. **Sin nuevas dependencias en tiempo de ejecución** sin discusión previa.
7. **Compromiso SLO** — el equipo propietario puede revertir cambios que afecten negativamente los SLOs.

## Objetivos de Nivel de Servicio (SLO)

### Disponibilidad

| Indicador | Objetivo | Medición |
|---|---|---|
| Tiempo de actividad (endpoint de salud) | >= 99.95% | Sonda sintética externa cada 60s |
| Tasa de éxito de API | >= 99.5% | Proporción de respuestas 2xx/4xx/5xx |

### Latencia (p99)

| Endpoint | Objetivo |
|---|---|
| CRUD de usuario (individual) | <= 300 ms |
| Lista de usuarios (paginada) | <= 500 ms |
| Procesamiento de eventos | <= 100 ms por evento |
| Enriquecimiento de Graph API | <= 1000 ms (asíncrono, no bloqueante) |

### Frescura

| Indicador | Objetivo |
|---|---|
| Retraso de consumo de eventos de auth | <= 15 segundos |
| Propagación de eventos de usuario | <= 10 segundos |

## Presupuesto de Errores

- Presupuesto de errores mensual: 100% - 99.95% = ~21 minutos de tiempo de inactividad permitido.
- Presupuesto rastreado en el panel de Grafana `users-service-error-budget`.
- Cuando el presupuesto de errores cae por debajo del 20%, los cambios no críticos se congelan.
- Cuando se agota, se requiere postmortem sin culpa y plan de recuperación.

## Monitoreo y Alertas

- Todos los SLOs instrumentados mediante métricas de Prometheus (`users_*`).
- Alertas en PagerDuty sobre tasa de consumo crítico.
- Paneles en la carpeta `Users Service` en Grafana.
- Panel separado para retraso de procesamiento de eventos de auth y salud de Graph API.
