---
paths:
  - "**/*.cs"
  - "**/*.csx"
---
# C# Patterns

> Origem: ECC (MIT), adaptado. Guia geral de .NET — alguns padrões (Repository/API response)
> são de backend e podem não se aplicar a este plugin desktop AutoCAD.

## Options Pattern

Use strongly typed options for config instead of reading raw strings throughout the codebase.
(This repo references `Microsoft.Extensions.Options`.)

```csharp
public sealed class ExportOptions
{
    public const string SectionName = "Export";
    public required string OutputDir { get; init; }
}
```

## Dependency Injection / composition

- Depend on interfaces at service boundaries
- Keep constructors focused; if a type needs too many dependencies, split responsibilities
- Register lifetimes intentionally when a DI container is in use

## API Response Pattern (backend only — usually N/A here)

```csharp
public sealed record ApiResponse<T>(
    bool Success,
    T? Data = default,
    string? Error = null);
```

## Repository Pattern (backend only — usually N/A here)

```csharp
public interface IRepository<T>
{
    Task<T?> FindByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<T> CreateAsync(T entity, CancellationToken cancellationToken);
}
```
