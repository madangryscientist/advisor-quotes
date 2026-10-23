# AGENTS.md

**Advisor Quotes**: an adviser quote service (C#/.NET 10, PostgreSQL, and a TanStack Start BFF on `@hackcove/ui`), built as a tech demo of domain modelling, idempotent commands, optimistic concurrency, tracing and testing at every layer.

## Layout

| Path | What | Nx project |
|---|---|---|
| `apps/api` | ASP.NET Core API with controllers (.NET 10) | `Api` |
| `apps/api-tests` | xUnit tests for the API (`WebApplicationFactory`) | `Api.Tests` |
| `apps/api-integration-tests` | xUnit tests against a real Postgres (`quotes_it`); skip when `ConnectionStrings__QuotesIntegration` is unset | `Api.IntegrationTests` |
| `apps/web` | TanStack Start app on Bun, a BFF for the API; UI from `@hackcove/ui` | `web` |
| `apps/web-e2e` | Playwright (Chromium) against the real stack and `quotes_e2e`; `bunx nx e2e web-e2e`, outside the gate | `web-e2e` |

The .NET projects are found by the official `@nx/dotnet` plugin, which infers targets from each
`.csproj` and names the project after it. `Directory.Build.props` applies Nullable,
TreatWarningsAsErrors and the latest recommended analyzers to every C# project, so for C#
`build` is also the typecheck and the lint. A new project (for example a Pricing library) is a new
`.csproj` under `apps/` or `libs/`, added to `AdvisorQuotes.slnx`; Nx picks it up with no config.

## Naming (C#)

One type per file, the file named after the type, the namespace matching the folder. Each feature (`Quotes`, `Advisers`) has the same folders:

| Folder | What goes there | Name ends with |
|---|---|---|
| `Domain/` | The model and its rules, no I/O | none (`Quote`, `QuoteStatus`, `QuoteTerms`); a rule's result is `…Outcome` |
| `Commands/` | A request that changes state, and what it returns | `…Command`, `…Result` |
| `Queries/` | A request that reads, and what it returns | `…Query`, `…Details`, `…Summary` |
| `Handlers/` | The one class that runs one command or query | `…Handler` |
| `Services/` | A helper that handlers share and that is not itself a command or query | `…Changer`, `…Seeder`, a noun |

HTTP is not per feature: `Api/Controllers/` holds the controllers (`QuoteCommandsController`, `QuoteQueriesController`, `AdviserQueriesController`), each handler a constructor parameter, all derived from `ApiControllerBase`, which runs a handler in a trace span and turns a failure into ProblemDetails. `X-Adviser-Id` is checked by `[RequireAdviser]` on each controller that needs an adviser, and `/health*` is mapped in `Program.cs`. A method that returns a `Task` ends in `Async`, except `Handle`. A test double
is `Fake…`; a test class is `<Type>Tests`, in the folder that mirrors the code it tests.

## The gate

From a fresh checkout (no `node_modules`, and the SDK may not be installed yet):

```bash
mise install          # .NET SDK pinned in mise.toml and global.json
bun install
bunx nx run-many -t build test typecheck lint
```

All green before a change is pushed. `web:lint` runs oxlint (type-aware, warnings deny), the
anti-slop check and `oxfmt --check`. The .NET side also builds and tests with only the SDK:
`dotnet build AdvisorQuotes.slnx` and `dotnet test AdvisorQuotes.slnx`; Nx is a convenience, never a requirement.

**Test invariant.** Unit tests use no database and no network. The only exceptions are the integration tests
(`apps/api-integration-tests`) and e2e, and they run only against the local Postgres from `docker compose up -d`.
Unset `ConnectionStrings__QuotesIntegration` and every integration test skips with the reason, so the gate touches no
database. To run them:

```bash
docker compose up -d
export ConnectionStrings__QuotesIntegration='Host=localhost;Port=5432;Database=quotes_it;Username=quotes;Password=quotes'
bunx nx test Api.IntegrationTests     # or: dotnet test apps/api-integration-tests
```

New integration tests derive from `DatabaseTest` (a transaction per test, rolled back) and use
`[FactRequiresDatabase]` instead of `[Fact]`.

## Invariants

- Configuration comes from the environment (`ConnectionStrings__Quotes` and friends), never from a
  committed secret. `.env` is gitignored; `.env.example` is the template.
- Nothing runs against a real external database. Local Postgres is `docker compose up -d`.

## The web app is a BFF

The browser never calls the .NET API, and the API has no CORS. Server functions call it through
`apps/web/src/server/quotes-api.ts`, the only module that knows `QUOTES_API_URL` (server env, never a
`VITE_` variable). Each call sends `X-Adviser-Id` from the `adviser-id` cookie (default `ADV-001`).
Tests inject a fake `fetch` there.
`createQuotesApi` has one function per command and query, typed from `src/server/api.gen.ts`, and a failure
comes back as `{ ok: false, error: { kind, message, fieldErrors } }` (`api-error.ts`), never a raw response.
After changing an endpoint, run `bun run generate:api` in `apps/web` (it starts the API, needs only the .NET SDK)
and commit the regenerated `api.gen.ts`. UI comes from `@hackcove/ui` with the Catppuccin Mocha theme; do not
write a component Hack Cove already has.
