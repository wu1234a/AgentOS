# AgentOS Repository Instructions

These instructions apply to the entire AgentOS repository.

## Required reading

Before implementing any AOS task, read: - `README.md` - `ROADMAP.md` -
`docs/ARCHITECTURE.md` - `docs/DATABASE-ER.md` -
`docs/EVENT-SPECIFICATION.md` - `docs/SECURITY-AND-OPERATIONS.md` -
`docs/CODEX-IMPLEMENTATION-GUIDE.md` - The single task file explicitly
assigned for the current work.

## Frontend language policy

- Frontend is Vue 3 + JavaScript.
- Do not use TypeScript.
- Do not create `.ts` or `.tsx` files.
- Do not use `<script lang="ts">`.
- Use `.js` and `.vue`.
- JSDoc is allowed for documentation but must not introduce a TypeScript
  build pipeline.

## Backend policy

- Use ASP.NET Core Minimal API, .NET 10, EF Core, and SQL Server.
- Keep Domain independent from Infrastructure.
- Keep business logic out of endpoint lambdas.
- Use DTOs for API contracts.
- Use UTC timestamps and structured logs.

## Execution policy

- Work on only one AOS task at a time.
- Inspect repository state before editing.
- Do not overwrite or delete user data.
- Do not run production operations.
- Do not expose secrets in code, logs, prompts, artifacts, or commits.
- Do not bypass sandbox or approval controls.
- Do not mark a task complete unless its acceptance criteria and tests
  pass.

## Delivery policy

For each completed task, create
`docs/reports/AOS-XXX-DELIVERY-REPORT.md` with changed files,
migrations, APIs, test commands/results, security checks, known
limitations, and follow-up work.
