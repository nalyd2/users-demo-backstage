# Informes de Calidad Generados

Poblado por CI/CD. Contiene resultados de pruebas, cobertura de código e informes de análisis estático para el Servicio de Usuarios.

| Artefacto | Formato | Herramienta |
|---|---|---|
| `test-results.xml` | XML NUnit | xUnit vía `dotnet test --logger trx` |
| `code-coverage.xml` | XML Cobertura | Coverlet + ReportGenerator |
| `sonarqube-report.json` | JSON | SonarQube Cloud |
| `dependency-scan-results.json` | JSON | Mend (WhiteSource) |
| `benchmark-results.md` | Markdown | BenchmarkDotNet — rendimiento de CRUD de perfiles, paginación, respaldo de validación JWT |
| `lint-results.json` | JSON | Analizadores Roslyn |

**No confirmado en el repositorio.**
