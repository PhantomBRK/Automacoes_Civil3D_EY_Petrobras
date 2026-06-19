---
paths:
  - "**/*.cs"
  - "**/*.csx"
  - "**/*.csproj"
---
# C# Testing

> Origem: ECC (MIT), adaptado. **Aspiracional**: este repo não tem projeto de testes hoje,
> e `dotnet test` não roda aqui (build MSBuild-only, `MSB4803`). Se um dia houver testes,
> rodá-los via MSBuild/VS, não pelo `dotnet` CLI.

## Test Framework (se/quando adicionar testes)

- Prefer **xUnit** for unit tests
- Use **FluentAssertions** for readable assertions
- Use **Moq** or **NSubstitute** for mocking dependencies

## Test Organization

- Name tests by behavior, not implementation details
- Separate unit and integration coverage clearly

```csharp
public sealed class ExportServiceTests
{
    [Fact]
    public void Export_WritesExpectedFile_WhenInputIsValid()
    {
        // Arrange
        // Act
        // Assert
    }
}
```

## Coverage

- Focus coverage on domain logic, validation, and failure paths.
