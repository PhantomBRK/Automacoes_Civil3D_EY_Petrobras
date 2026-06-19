# `.claude/` — Configuração compartilhada do Claude Code

Versiona **apenas** a configuração compartilhada do projeto. O `.gitignore` ignora todo o
resto do `.claude/` (estado local de sessões, worktrees, `settings.local.json`); só são
commitados: este `README.md`, `agents/` e `rules/`.

## O que tem aqui (versão enxuta do ECC)

Em vez de instalar o plugin completo **ECC — Everything Claude Code** (67 agents / 271 skills,
em sua maioria voltados a web/cloud/outras linguagens), foi vendorizado um **subconjunto curado
e adaptado** ao que este projeto realmente é: um **plugin C#/.NET 8 para AutoCAD Civil 3D 2026**
(Windows, x64). Tudo aqui é carregado automaticamente em **sessões locais e na nuvem** (sem
`/plugin install`), porque o Claude Code lê `.claude/agents/` e subagents do projeto.

### `agents/` — subagents (invoque com `@nome` ou deixe o Claude delegar)

| Agent | Para quê |
|---|---|
| `csharp-reviewer` | **Adaptado a este repo**: review de C# com MSBuild (não `dotnet`, que quebra com `MSB4803`) e checagens de AutoCAD/Civil 3D (`Transaction`/`using`, singletons `Manager.Doc*`, `[CommandMethod]`, `catch {}` que engole erro de transação). |
| `code-reviewer` | Review geral de código (agnóstico de linguagem). |
| `planner` | Planejar tarefas grandes (ex.: refatorar arquivos de 49–84 KB). |
| `code-explorer` | Navegar/entender base grande sem ler tudo. |
| `silent-failure-hunter` | Caçar falhas engolidas / `catch` vazio. |
| `refactor-cleaner` | Limpeza e refatoração segura. |
| `code-simplifier` | Simplificar código sem mudar comportamento. |

### `rules/csharp/` — guia de referência .NET (geral)

`coding-style`, `patterns`, `security`, `testing` — origem ECC (MIT), **adaptados**: removidas
partes web/backend irrelevantes (EF Core, ASP.NET, SQL injection, `appsettings`) e os hooks de
`dotnet build/format/test` (que não funcionam aqui). A fonte de verdade das convenções do
projeto continua sendo o **`CLAUDE.md`** da raiz.

> Observação: o Claude Code não auto-carrega `rules/` como faz com `agents/`. Estes arquivos
> servem de referência e são citados pelo `csharp-reviewer`. Para torná-los ativos
> globalmente, referencie-os no `CLAUDE.md` da raiz com `@.claude/rules/csharp/<arquivo>.md`.

## Por que não o plugin completo

- A maioria dos 271 skills / 67 agents do ECC é para outras stacks (React/Vue/Django/Go/Rust/
  Flutter/DeFi/SEO…) — ruído num plugin desktop AutoCAD.
- Vários hooks/agents do ECC assumem `dotnet build`/`dotnet format`/`dotnet test`, que **falham
  neste projeto** (`MSB4803`, por causa de `<COMReference>` — só MSBuild full-framework).

## Quer o plugin completo, mesmo assim?

```
/plugin marketplace add https://github.com/affaan-m/ECC
/plugin install ecc@ecc
```

(Origem dos arquivos: https://github.com/affaan-m/ECC — licença MIT.)
