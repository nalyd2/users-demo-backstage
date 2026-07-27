# Directrices de Seguridad — Users Service

- **Estado:** Aprobado
- **Propietario:** Equipo de Platform Engineering / Security Champion
- **Última actualización:** 2026-07-20

## Alcance

Este documento define los estándares de seguridad para el Users Service, que gestiona perfiles de usuario, datos de inquilino y roles RBAC. Como servicio de Nivel 1 que contiene Información de Identificación Personal (PII), tiene requisitos de seguridad distintos más allá del Auth Service.

## Mitigaciones de OWASP Top 10

### A01: Control de Acceso Roto

- Todos los endpoints requieren un JWT válido del Auth Service, validado mediante verificación de firma JWKS.
- RBAC (roles Admin, Operator, User) aplicado a nivel de middleware para cada endpoint.
- Aislamiento de inquilino: cada consulta incluye `tenant_id = {user_tenant}` aplicado en la capa de repositorio.
- Soft-delete previene la destrucción permanente de datos no autorizada; solo el rol Admin puede hacer hard-delete.
- Paginación por cursor previene la enumeración de recursos mediante IDs secuenciales (UUIDv4 utilizado para todos los identificadores de recursos).

### A02: Fallos Criptográficos

- Todos los campos PII (correo electrónico, teléfono, dirección) cifrados en reposo usando AES-256-GCM en la base de datos.
- Campos sensibles registrados solo como hashes (nunca en texto plano).
- Todos los datos en tránsito usan TLS 1.3.
- Las contraseñas nunca se almacenan en el Users Service; la autenticación se delega completamente al Auth Service.

### A03: Inyección

- Todas las consultas SQL usan Entity Framework Core con consultas parametrizadas.
- Las consultas a Graph API usan Microsoft Graph SDK con constructores de solicitudes parametrizadas.
- Validación de entrada en todos los DTOs usando FluentValidation.

### A04: Diseño Inseguro

- Las nuevas características requieren revisión de seguridad con enfoque en aislamiento de inquilino y manejo de PII.
- Limitación de tasa en API gateway para prevención de enumeración de usuarios.
- Operaciones por lote limitadas a 1000 registros por solicitud con límite configurable.

### A05: Configuración de Seguridad Incorrecta

- Toda la configuración desde Azure App Configuration con referencias a Key Vault.
- Imágenes de contenedor escaneadas (Mend) antes del despliegue.
- Encabezados de seguridad HTTP establecidos en todas las respuestas.
- Los permisos de Graph API siguen el mínimo privilegio: solo `User.Read.All` y `User.ReadWrite.All` según sea necesario.

### A06: Componentes Vulnerables y Desactualizados

- Escaneo Mend en cada PR; CVSS 7.0+ bloquea la fusión.
- Paquetes NuGet fijados con validación de archivo de bloqueo.
- Reconstrucción semanal de imagen base con los últimos parches del SO.
- SBOM generado para cada release.

### A07: Fallos de Identificación y Autenticación

- Autenticación completamente delegada al Auth Service mediante validación JWT.
- Información de sesión recibida mediante eventos de auth (login/logout).
- No se implementa autenticación local; no hay almacenamiento de credenciales.

### A08: Fallos de Integridad de Software y Datos

- Todos los artefactos CI/CD firmados y verificados.
- Los eventos de usuario publicados incluyen versión de esquema e ID de correlación.
- Los eventos de auth consumidos se validan para cumplimiento de esquema antes del procesamiento.

### A09: Registro de Seguridad y Monitoreo

- Todas las mutaciones de usuario registradas con estado anterior/posterior para auditoría.
- El acceso a campos PII se registra por separado en el registro de auditoría.
- Los fallos de procesamiento de eventos se registran con contexto completo para reproducción.
- Logs retenidos por mínimo 1 año para cumplimiento.

### A10: Falsificación de Solicitud del Lado del Servidor (SSRF)

- Todo HTTP saliente (Graph API) restringido a `graph.microsoft.com` y `login.microsoftonline.com`.
- Los clientes HTTP usan políticas de redirección restringidas.
- Todas las solicitudes salientes incluyen tiempo de espera y token de cancelación.

## Gestión de Secretos

- Sin secretos en código; todos los secretos en Azure Key Vault accedidos mediante Managed Identity.
- Instancias de Key Vault por entorno.
- Secreto de cliente de Graph API almacenado en Key Vault con rotación automática.
- Cadenas de conexión de base de datos almacenadas en Key Vault; nunca en archivos de configuración.

## Clasificación de Datos PII

| Campo de Datos | Clasificación | Cifrado | Registro |
|---|---|---|---|
| Correo electrónico | PII | AES-256-GCM en reposo | Solo hasheado |
| Nombre | PII | AES-256-GCM en reposo | Nunca registrado |
| Apellido | PII | AES-256-GCM en reposo | Nunca registrado |
| Número de Teléfono | PII | AES-256-GCM en reposo | Nunca registrado |
| Dirección | PII | AES-256-GCM en reposo | Nunca registrado |
| ID de Usuario (UUID) | Interno | Ninguno | Valor completo |
| ID de Inquilino (UUID) | Interno | Ninguno | Valor completo |
| Roles | Interno | Ninguno | Valor completo |

## Escaneo de Dependencias (Mend)

- Todos los PRs escaneados; CVSS 7.0+ bloquea la fusión.
- Escaneo completo diario contra todas las dependencias.
- Alertas CVSS 9.0+ desencadenan notificación inmediata.
- SLA de remediación: CVSS 9.0+ dentro de 24 horas, 7.0-8.9 dentro de 7 días, 4.0-6.9 dentro de 30 días.
- Dependencias del SDK de Graph API monitoreadas para cambios disruptivos.

## SAST (SonarQube)

- Quality Gate en cada PR: cobertura >= 80%, sin problemas críticos/bloqueantes.
- Puntos críticos de seguridad revisados por el security champion cada sprint.
- Reglas personalizadas: sin credenciales codificadas, sin PII en mensajes de log, validación de filtro de inquilino en consultas.

## Modelado de Amenazas

- **Cadencia:** Revisión trimestral completa del servicio; por característica para nuevas capacidades.
- **Áreas de enfoque:** Omisión de aislamiento de inquilino, fuga de datos PII, integridad del procesamiento de eventos, uso indebido de token de Graph API.
- **Herramienta:** OWASP Threat Dragon.
- **Salida:** Documento de modelo de amenazas almacenado en `docs/security/threat-models/`.

## Pruebas de Penetración

- **Frecuencia:** Prueba de penetración de alcance completo anual por terceros externos.
- **Alcance:** Todos los endpoints de gestión de usuarios, aislamiento de inquilino, aplicación de RBAC, manejo de datos PII.
- **Remediación:** Hallazgos críticos dentro de 48 horas, Altos dentro de 14 días, Medios dentro de 60 días.

## Cumplimiento

- El Users Service debe cumplir con SOC 2 Tipo II, ISO 27001, GDPR y CCPA.
- Retención de datos aplicada: los usuarios se eliminan con soft-delete (retenidos por 90 días) y luego se purgan.
- Derecho al olvido (Artículo 17 del GDPR) compatible mediante API de hard-delete para rol Admin.
- Portabilidad de datos (Artículo 20 del GDPR) compatible mediante endpoint de exportación de datos de usuario.
