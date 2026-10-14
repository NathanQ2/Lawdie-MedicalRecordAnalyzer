# AGENTS.md

Guidance for AI coding agents and contributors working in this repository.

## What this project is
Medical Record Analysis automates the demand-letter work for personal injury (PI) cases. It reads a case's medical documents (PDFs, text files, and scans or images through OCR), extracts cited facts, finds contradictions between documents, and drafts a demand letter to an insurance adjuster. [PROBLEM_DESCRIPTION.md](PROBLEM_DESCRIPTION.md) has the original brief, and [docs/frontend-plan.md](docs/frontend-plan.md) has the web UI plan.

**The overriding requirement is that nothing generated may be invented.** Demand letters go to insurance adjusters, so an invented injury, a wrong date, or an unsupported causal claim is a real liability for a law firm. Every change must keep generated text traceable to an exact quote and page in the source documents, and must surface conflicts and gaps instead of hiding them.

## Repository layout
| Path | What it holds |
|---|---|
| `MedicalRecordAnalysis/` | Core library (.NET 10): text extraction, LLM providers, retrieval, grounding, and rendering |
| `MedicalRecordAnalysis/OCR/` | `IOCRProvider`. `DirectTextProvider` reads text files and digital PDFs (PdfPig) page by page, and sends pages without embedded text, and images, to an optional `IPageRecognizer`. `AzureDocumentIntelligenceRecognizer` is that recognizer (Azure's Read model). `ChainedOCRProvider` falls back across providers. `TextractProvider` (AWS) is not wired in. |
| `MedicalRecordAnalysis/LLM/` | `ILLMProvider`, `AnthropicProvider`, `DummyLLMProvider`, plus `TextChunker`, `GroundingCheck`, `VerbatimCitations` and the result models |
| `MedicalRecordAnalysis/Retrieval/` | `PassageSplitter`, `PassageRetriever` (topic-based passage selection for drafting), `PageLocator` |
| `MedicalRecordAnalysis/Caching/` | Per-document ingest cache, keyed by a hash of each file's contents |
| `MedicalRecordAnalysis/*.cs` | `MedicalRecordAnalyzer` (the pipeline), `PICase`, `Citation`, `DemandLetterTemplate`, and the Markdown, text and Word renderers |
| `MedicalRecordAnalysisCLI/` | `ingest` and `draft` commands (System.CommandLine) |
| `MedicalRecordAnalysis.Api/` | Local ASP.NET Core API: cases, documents, background jobs, review checklist, export |
| `MedicalRecordAnalysis.Tests/` | xUnit tests for the library, CLI behavior and API, including fixtures in `TestData/` |
| `web/` | React + TypeScript frontend (Vite, Mantine, TanStack Query, React Router, pdf.js) |
| `cases/` | Local case storage written by the API. It is git-ignored and may contain patient data, so never commit it. |

## How the pipeline works
**Ingest** (`MedicalRecordAnalyzer.Ingest`):
1. Extract text and page boundaries from each document. Pages with no embedded text go to OCR when it is configured, and the OCR'd ranges (plus handwriting and low-confidence words) are recorded in `SourceDocument.Recognition`. Files no provider can read are recorded in `PICase.SkippedFiles`, never dropped silently.
2. Summarize each document. The text is sent as citable chunks (`TextChunker` splits it into lines, then sentences). Each citation maps back to an exact span of the source, and the API's quoted text must match the chunks we sent, or the citation is dropped.
3. Find contradictions between and within documents. Quotes are referenced by ID (`[Q12]`), never by restating their text.
4. Write a case summary that cites the extracted quotes and presents disputed facts as disputed. `GroundingCheck` lists sentences no citation covers.
5. Build the chronology: each visit, test or procedure with its date, provider and main finding, citing quotes by ID, plus the date of loss the records state. `ChronologyCheck` confirms each date and provider name against the cited document and flags what it can't find, and keeps the date of loss only when its own quote states it. The API compares that date with the intake date of loss and lists a mismatch as a review item.
6. Assign page numbers to every citation (`PageLocator`), and flag citations that quote OCR'd text (`RecognitionLocator`).

**Draft** (`MedicalRecordAnalyzer.Draft`):
1. `PassageRetriever` picks the passages most relevant to each part of the letter, within a character budget.
2. The provider writes the letter body from those passages under fixed `## ` section headings.
3. `VerbatimCitations` attaches citations to sentences copied from the documents but left uncited, where the match is unambiguous.
4. `DemandLetterTemplate` wraps the body with the letterhead, "Re:" block, salutation, demand and signature. Facts the documents can't supply are bracketed placeholders such as `[CLAIM NUMBER]`.
5. The renderers produce Markdown, plain text or Word (`.docx`, with real footnotes). Each export ends with an attorney review section listing intake details the documents contradict, contradictions, uncited statements and placeholders.

**Grounding rules to preserve:**
- Every citation must point at an exact substring of the extracted text, with its page.
- Disputed facts are attributed to each document, never resolved silently.
- Uncited sentences are reported, not hidden.
- When a value is unknown, use a placeholder; never guess.
- Quotes read by OCR are flagged and listed for review, because OCR can misread characters.
- Chronology dates and provider names that can't be found in the cited document are flagged, never trusted silently.

## Building, running and testing
**Requirements:** .NET 10 SDK, and Node.js 22.12 or newer (the developer uses Node 24 via nvm). An Anthropic API key in `ANTHROPIC_API_KEY`, or in `.env` at the repository root; the API and live tests read `.env` in development. OCR is optional: set `AZURE_DOCUMENT_INTELLIGENCE_ENDPOINT` and `AZURE_DOCUMENT_INTELLIGENCE_KEY` to enable it; without them, scans are skipped.

Build and run the tests (both suites run offline):
```bash
dotnet build
```
```bash
dotnet test
```
```bash
npm test --prefix web
```

Type check and lint the frontend:
```bash
npm run build --prefix web
```
```bash
npm run lint --prefix web
```

Run the web app in two terminals, then open http://localhost:5173. The dev server forwards `/api` to the API on port 5180.
```bash
dotnet run --project MedicalRecordAnalysis.Api
```
```bash
npm run dev --prefix web
```

**Testing rules:**
- **Live LLM calls cost money.** Live tests are skipped unless `RUN_LIVE_TESTS=1` is set. Ask the user before running live tests, ingest or draft against the real API, and state what will run and roughly what it costs. Each approval covers one run.
- **Use `DummyLLMProvider` or a fake `ILLMProvider` for offline tests,** and `FakePageRecognizer` instead of a real OCR service. Azure OCR is billed per page, so its live test needs the same approval.
- **Offline OCR-conversion tests** build Azure results with `DocumentIntelligenceModelFactory`. API tests run the real server in memory through `ApiFactory`, against a temporary cases folder.
- **`MedicalRecordAnalysis.Tests/TestData/Case_Ellis/`** is a fictional 25-document case (including a handwritten journal photo for OCR) with planted contradictions. `TestData/Case_Ellis_AnswerKey.md` lists them. Ingest the `Case_Ellis/` folder itself, not `TestData/`; otherwise the answer key is read as a document.
- **Generate PDFs in tests** with `TestFiles.WritePdf` rather than committing binary fixtures.
- **Frontend tests use Vitest and Testing Library.** Render components with `renderWithMantine` from `web/src/test/render.tsx`.

## C# code style
- **Layout:** file-scoped namespaces that match folders; Allman braces; four-space indentation; nullable reference types enabled.
- **Types:** explicit types, never `var`. Use the `string` keyword, not `String`.
- **Object creation:** `new Type() { Property = value }`, with parentheses before the initializer. Use collection expressions (`[]`, `[.. items]`) for arrays and lists.
- **Fields:** `m_` prefix for instance fields, `s_` for mutable static fields, PascalCase for constants and `static readonly` fields.
- **Constructors:** use classic constructors that assign `m_` fields, not primary constructors. Positional `record` types are fine for immutable data (DTOs, `PipelineProgress`, `RetrievedPassage`).
- **Method bodies:** expression-bodied (`=>`) members are fine for single expressions; use block bodies for anything longer.
- **Async:** async methods have no `Async` suffix (`Ingest`, `SummarizeDocument`), except framework overrides such as `ExecuteAsync`. Pass a `CancellationToken cancellationToken = default` as the last parameter and pass it through.
- **Comments:**
  - `/** ... */` doc comments explain why something is done, and how it fits the grounding rules. Don't use `///` XML comments.
  - `//` is for short inline notes.
  - Keep comments about behavior, not history.
- **Errors:** user-facing exception messages are complete sentences. Providers throw `NotSupportedException` to mean "can't read this file, try the next provider".
- **Provider independence:** keep the core library independent of any provider. Anything Anthropic-specific lives in `AnthropicProvider`, and shared logic (chunking, grounding, retrieval, rendering) lives outside it.

## TypeScript and React style (`web/`)
- **Formatting:** no semicolons, single quotes, two-space indentation.
- **TypeScript:**
  - `erasableSyntaxOnly` is on, so use string-literal unions instead of `enum` and avoid constructor parameter properties.
  - Use `import type` for type-only imports.
- **Components:** function components, Mantine UI components, CSS modules for custom styles. Pass the current time into components rather than reading it during render.
- **Data:**
  - All API calls go through `web/src/api/client.ts`, which adds the required `X-MRA-Client` header. Types in `api/types.ts` mirror the API's camelCase JSON, with enums as strings.
  - Server state uses TanStack Query with the keys in `caseKeys`. Analysis and draft queries are keyed by their `updatedAt` timestamps.
- **UI wording:** follow [docs/writing-style.md](docs/writing-style.md). In short:
  - Call uploaded files "documents", not "records" (code identifiers and URLs still say `records`), and say "analyze", not "ingest".
  - Don't mention Anthropic, LLM APIs or cost to end users.
  - Use no em dashes or curly quotes.
  - Long-running actions still ask for confirmation.

## Security and privacy
- **The API is local-only.** It listens on localhost, filters `Host` headers (`AllowedHosts`), and requires an `X-MRA-Client` header on every request that changes data, which blocks cross-site requests.
- **Paths are validated.** Case IDs and document names are checked so requests can't leave their case folder.
- **Uploaded documents are untrusted.** They're served with `nosniff`, and with a sandbox CSP for everything except PDFs, which Chrome won't display in a sandboxed page.
- **Patient data stays local.** Real patient documents must not go into a shared or cloud deployment until login and HIPAA controls exist. Never commit `cases/`, `.env`, or exported drafts.

## Git
- **Commit messages:** an imperative subject line, then a body explaining what changed and why. Keep unrelated changes in separate commits.
- **Permission:** commit only when asked, and never push without being asked.
- **Untracked files:** `.claude/launch.json` (the preview server config with a local Node path) stays uncommitted.

## Known gaps
- **OCR needs Azure credentials:** without them, scanned and image-only documents are skipped. The free (F0) tier analyzes only the first two pages of each request; a PDF missing a requested page is rejected, but a multi-page image on F0 can come back incomplete.
- **OCR'd quotes aren't highlighted in the source viewer,** because scanned PDF pages and images have no text layer to search.
- **Weak spots in the case-level checks:**
  - Contradiction detection misses counting-based conflicts (the PT visit-count discrepancy in `Case_Ellis`).
  - It occasionally flags non-conflicts.
- **Uncited sentences:** drafts still contain uncited paraphrases, which are listed for review. Haiku (the current default model, chosen for cost during testing) cites less consistently than larger models.
- **The ingest cache is keyed only on file contents.** Delete a case's `cache/` folder after changing models or prompts.
