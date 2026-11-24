# AI usage log

What I asked the coding agent (Claude Code), what it produced, and what was checked or changed.
Keep adding entries as the project grows. This is the source for the "How I used AI" section of the README.

## 2026-11-24 — Initial build from the step-by-step guide

**Asked:** build DocAgent following my build guide (Steps 1–12): solution, BDD specs first, document API,
chunking + embeddings, cosine and LSH indexes, RAG with citations, tool-calling agent, tests, CI/CD, Terraform.

**Produced:** the full solution in small commits, specs committed before feature code, 43 deterministic tests
(unit, BDD, contract), an LSH benchmark and an AI evaluation harness.

**Problems caught and how:**

| # | What went wrong | How it was caught | Fix |
|---|---|---|---|
| 1 | `dotnet new xunit` ships xunit 2.5.3, but Reqnroll.xUnit needs ≥ 2.8.1 — restore failed. The first scaffold commit was even made on a failing build because a piped `tail` hid the exit code. | Build output | Upgraded xunit / runner / Test SDK, amended the scaffold commit |
| 2 | The guide's fake embedding used `string.GetHashCode()`, which .NET randomises per process. Word-slot collisions change between runs, so the "refuse to guess" scenario could fail at random. | Reading the code and reasoning about the hash | Deterministic FNV-1a hash in `FakeEmbeddingService` |
| 3 | **A request missing its project returned 500, not 400, when no AI key was set.** The endpoint asked for `DocumentService` as a parameter, so ASP.NET built the AI client *before* the validation code ran. The in-memory tests could not see this because they always register fakes. | Smoke test of the real API with `curl` and no keys | Validate first, resolve AI services inside the handler; map `AiNotConfiguredException` to a clear 503. Added `ApiWithoutAiTests` as a regression test |
| 4 | The Terraform sets SQLite to `/home/data/docagent.db`, but SQLite does not create missing folders, so the first start on App Service would crash. | Reading the Terraform against the code | Create the folder from the connection string on startup |
| 5 | The guide's rate-limiter snippet does not compile without `using Microsoft.AspNetCore.RateLimiting;`. | Compiler error | Added the using |
| 6 | Reqnroll's generated `*.feature.cs` files were committed. | `git show --stat` review | Added to `.gitignore`, removed from the index |
| 7 | The guide suggests 20,000 *purely random* vectors for the LSH benchmark. In 256 dimensions random vectors are almost all orthogonal, so "nearest neighbour" is close to meaningless and recall says little. | Reasoning about the data | Clustered synthetic data (cluster centre + noise), queries are noisy copies of stored vectors |

**Design changes from the guide (deliberate):**

- Agent tools moved from `DocAgentRunner` into `DocAgentTools`, so the access rules are tested directly, without an LLM
  (`AgentAccess.feature`, `DocAgentToolsTests`).
- One `OpenAiClientFactory` decides Azure OpenAI vs OpenAI, so the fallback is a config switch, not a code change.
- `AnswerService.MinScore` is a settable property (config `Rag:MinScore`), so the eval can sweep it.
- Model output is parsed defensively: malformed JSON or a missing answer → "not found" (fail closed).
- Prompt-injection line added to both system prompts: sources/tool results are data, never instructions.

**Not done yet (needs accounts/keys):** running the AI evaluation, `terraform apply`, GitHub push and deploy,
the demo GIF, the App Insights screenshot, a real agent trace for the README.
