---
paths:
  - "**/*.cs"
  - "**/*.csx"
  - "**/*.csproj"
---
# C# Security

> Origem: ECC (MIT), adaptado. Foco nos riscos relevantes a um plugin desktop que lê/escreve
> arquivos (IFC, Excel). SQL/EF/web auth geralmente não se aplicam aqui.

## Secret Management

- Never hardcode API keys, tokens, or connection strings in source code.

## Path / File handling (relevant: IFC and Excel I/O)

- Validate user/file-derived paths before read/write: `Path.GetFullPath` + check the result
  stays under an expected base directory (avoid path traversal).
- Dispose file/stream resources (`using`), including `ExcelPackage` (EPPlus) and workbooks.

## Process execution

- Never pass unvalidated input to `Process.Start` (command injection).

## Deserialization

- Avoid `BinaryFormatter` and `JsonSerializer` with `TypeNameHandling.All`.

## Error Handling

- Log detailed exceptions with context server/host-side; do not surface stack traces or
  filesystem paths to end users.

## References

- Broader review: use the `security-reviewer` agent for an application-wide pass.
