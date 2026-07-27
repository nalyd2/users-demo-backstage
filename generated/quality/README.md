# Generated Quality Reports

Populated by CI/CD. Contains test results, code coverage, and static analysis reports for the Users Service.

| Artifact | Format | Tool |
|---|---|---|
| `test-results.xml` | NUnit XML | xUnit via `dotnet test --logger trx` |
| `code-coverage.xml` | Cobertura XML | Coverlet + ReportGenerator |
| `sonarqube-report.json` | JSON | SonarQube Cloud |
| `dependency-scan-results.json` | JSON | Mend (WhiteSource) |
| `benchmark-results.md` | Markdown | BenchmarkDotNet — profile CRUD, pagination, JWT validation fallback performance |
| `lint-results.json` | JSON | Roslyn Analyzers |

**Not committed.**
