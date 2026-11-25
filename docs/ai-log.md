# AI usage log

What I asked the coding agent (Claude Code), what it produced, and what was checked or changed.
Keep adding entries as the project grows. This is the source for the "How I used AI" section of the README.

## 2026-11-24 — Initial build from the build plan

**Asked:** build DocAgent following my build plan (Steps 1–12; drafted with an AI assistant, then reviewed and adjusted by me): solution, BDD specs first, document API,
chunking + embeddings, cosine and LSH indexes, RAG with citations, tool-calling agent, tests, CI/CD, Terraform.

**Produced:** the full solution in small commits, specs committed before feature code, 43 deterministic tests
(unit, BDD, contract), an LSH benchmark and an AI evaluation harness.

**Problems caught and how:**

| # | What went wrong | How it was caught | Fix |
|---|---|---|---|
| 1 | `dotnet new xunit` ships xunit 2.5.3, but Reqnroll.xUnit needs ≥ 2.8.1 — restore failed. The first scaffold commit was even made on a failing build because a piped `tail` hid the exit code. | Build output | Upgraded xunit / runner / Test SDK, amended the scaffold commit |
| 2 | The plan's fake embedding used `string.GetHashCode()`, which .NET randomises per process. Word-slot collisions change between runs, so the "refuse to guess" scenario could fail at random. | Reading the code and reasoning about the hash | Deterministic FNV-1a hash in `FakeEmbeddingService` |
| 3 | **A request missing its project returned 500, not 400, when no AI key was set.** The endpoint asked for `DocumentService` as a parameter, so ASP.NET built the AI client *before* the validation code ran. The in-memory tests could not see this because they always register fakes. | Smoke test of the real API with `curl` and no keys | Validate first, resolve AI services inside the handler; map `AiNotConfiguredException` to a clear 503. Added `ApiWithoutAiTests` as a regression test |
| 4 | The Terraform sets SQLite to `/home/data/docagent.db`, but SQLite does not create missing folders, so the first start on App Service would crash. | Reading the Terraform against the code | Create the folder from the connection string on startup |
| 5 | The plan's rate-limiter snippet does not compile without `using Microsoft.AspNetCore.RateLimiting;`. | Compiler error | Added the using |
| 6 | Reqnroll's generated `*.feature.cs` files were committed. | `git show --stat` review | Added to `.gitignore`, removed from the index |
| 7 | The plan suggests 20,000 *purely random* vectors for the LSH benchmark. In 256 dimensions random vectors are almost all orthogonal, so "nearest neighbour" is close to meaningless and recall says little. | Reasoning about the data | Clustered synthetic data (cluster centre + noise), queries are noisy copies of stored vectors |

**Design changes from the plan (deliberate):**

- Agent tools moved from `DocAgentRunner` into `DocAgentTools`, so the access rules are tested directly, without an LLM
  (`AgentAccess.feature`, `DocAgentToolsTests`).
- One `OpenAiClientFactory` decides Azure OpenAI vs OpenAI, so the fallback is a config switch, not a code change.
- `AnswerService.MinScore` is a settable property (config `Rag:MinScore`), so the eval can sweep it.
- Model output is parsed defensively: malformed JSON or a missing answer → "not found" (fail closed).
- Prompt-injection line added to both system prompts: sources/tool results are data, never instructions.

**Not done yet (needs accounts/keys):** running the AI evaluation, `terraform apply`, GitHub push and deploy,
the demo GIF, the App Insights screenshot, a real agent trace for the README.

## 2026-11-24 — No Azure OpenAI key: provider-neutral AI

**Asked:** I don't have an Azure OpenAI key, so make it work without one.

**Produced:** one `AI` config section (`Endpoint`, `Key`, `ChatModel`, `EmbeddingModel`) for any OpenAI-compatible provider.
Ollama (free, local, no key) is the recommended default. Azure OpenAI is detected from the endpoint host, and OpenAI is used when only a key is set.
The LSH index now takes its vector size from the first vector, so switching models (1536 vs 768 dims) needs no config change.

**Problem caught and how:**

| # | What went wrong | How it was caught | Fix |
|---|---|---|---|
| 8 | The OpenAI .NET SDK's `GenerateEmbeddingAsync` always requests base64 and can *only* parse base64. A server that returns plain float arrays makes it throw `FormatException`. | A new test that runs the real client classes against a small fake OpenAI-compatible server | Use the SDK's protocol method with `"encoding_format": "float"` and parse either format |

**Not verified:** no real Ollama or OpenAI run yet (neither was available on this machine). The fake-server tests cover
request shape and parsing, not model quality. Run the evaluation against Ollama to get real numbers.

## 2026-11-24 — First run against real local models (Ollama, qwen2.5:3b + nomic-embed-text)

**Asked:** install Ollama and run DocAgent for real.

**Problems caught and how:**

| # | What went wrong | How it was caught | Fix |
|---|---|---|---|
| 9 | Answers took minutes and came back as **garbage** (random multilingual tokens). Ollama ran the model on this laptop's old AMD R7 M370 GPU through Vulkan, which produced corrupted output. | Calling the model directly; the Ollama server log showed `library=Vulkan`, 13/13 layers offloaded | Hid the GPU from Ollama (`GGML_VK_VISIBLE_DEVICES=-1`, user env var) → 100% CPU, clean output, ~5 s per answer. Re-embedded every document, since the old vectors also came from the broken GPU |
| 10 | The model **kept generating**: 2,300+ tokens of whitespace after the JSON. Nothing capped output length, which with a paid API would also be a cost risk. | Ollama log `n_gen = 2320` for a one-line answer | `MaxOutputTokenCount` = 500 for answers, 1,000 per agent step. A test checks that the request carries the cap |
| 11 | **Correct answers were returned as "I could not find this".** qwen2.5:3b cites source *numbers* (`"citations": [1]`), not file names. The strict parser threw on an integer, and the fail-closed path turned a right answer into a refusal. | Comparing the raw model reply with the API response | Citations are matched to the sources actually sent, as a file name, a number or `"[n]"`. Anything else is still dropped. The prompt now shows an example. 10 new unit tests |
| 12 | An off-topic question ("capital of Australia?") was **answered** ("Canberra"). `MinScore = 0.30` was tuned for OpenAI embeddings. With nomic-embed-text, unrelated text scores 0.39–0.42 while real matches score 0.65–0.79. | Printing the similarity of each question to each document | The threshold is per model: `Rag:MinScore` is set to fit the eval results, the eval sweeps a wider range, and only sources above the threshold are sent to the model |

**Lesson:** the deterministic tests all passed before this run. None of these four problems can show up with fakes.
They are exactly why the plan keeps a separate evaluation against the real model.

## 2026-11-25 — Recording the demo: the agent on a 3B model

**Asked:** record a demo GIF.

**Problem caught and how:**

| # | What went wrong | How it was caught | Fix |
|---|---|---|---|
| 13 | The agent almost never answered document questions. qwen2.5:3b calls `list_documents` with empty filters (`{"project":"","type":""}`) or lowercase values (`"contract"`). The tool treated `""` as "project equals empty" and compared case-sensitively, so it returned nothing and the model gave up. The model also often stopped after listing (metadata only) or asked the user which project they meant. | Reading the traces of real agent runs while recording the demo | Blank filters are ignored and filters ignore case. Access control stays an exact match, and a test proves `"zeus"` does not widen access. The prompt now says listing is metadata only, and to call a tool instead of asking. 4 new tests. Measured: 2/5 correct before, 4/6 after |

**Still true:** a 3B model is unreliable at multi-step tool use. The README states the measured rate instead of hiding it.
The GIF is rendered from the real responses saved in `docs/demo/`.
