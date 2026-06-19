# `.claude/` — Configuração compartilhada do Claude Code

Esta pasta versiona **apenas** a configuração compartilhada do Claude Code do projeto.
O `.gitignore` ignora todo o resto do `.claude/` (estado local de sessões, worktrees,
`settings.local.json`); somente `settings.json` e este `README.md` são commitados.

## O que está configurado

`settings.json` registra o marketplace e habilita o plugin **ECC — Everything Claude Code**
(`affaan-m/ECC`, MIT). Com isso o plugin fica disponível automaticamente:

- **Sessões locais:** ao abrir o repositório no Claude Code (v2.1.0+), ele pede para
  confiar nas configurações do projeto e instala o plugin a partir do marketplace.
- **Sessões na nuvem (Claude Code na web):** o plugin é instalado no início da sessão,
  desde que a política de rede do ambiente permita acesso ao GitHub.

```jsonc
{
  "extraKnownMarketplaces": {
    "ecc": { "source": { "source": "github", "repo": "affaan-m/ECC" } }
  },
  "enabledPlugins": { "ecc@ecc": true }
}
```

## Equivalente manual (não é necessário com o arquivo acima)

```
/plugin marketplace add https://github.com/affaan-m/ECC
/plugin install ecc@ecc
```

## Observações

- O ECC traz agents, skills, commands e **hooks** que passam a ficar ativos. Na primeira
  vez, o Claude Code local pede confirmação de confiança antes de ativar.
- **Rules** não são distribuídas pelo sistema de plugins. Se quiser as rules do ECC,
  copie manualmente (passo opcional):
  ```
  mkdir -p ~/.claude/rules/ecc && cp -r rules/common ~/.claude/rules/ecc/
  ```
- Versões/IDs conferidos no marketplace do ECC: marketplace `ecc`, plugin `ecc` (`ecc@ecc`).
