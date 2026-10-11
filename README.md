# Medical Record Analysis

This project turns a personal injury case's medical records into structured facts and a draft demand letter, with every claim cited back to a page of the source records. See [PROBLEM_DESCRIPTION.md](PROBLEM_DESCRIPTION.md) for the goals and [docs/frontend-plan.md](docs/frontend-plan.md) for the web UI plan.

## Requirements
- .NET 10 SDK
- Node.js 22.12 or newer (for the web UI)
- An Anthropic API key in `ANTHROPIC_API_KEY`, or in a `.env` file at the repository root containing `ANTHROPIC_API_KEY=...`

## Web UI (local)
Start the API and the web dev server in two terminals:

```bash
dotnet run --project MedicalRecordAnalysis.Api
```

```bash
npm install --prefix web && npm run dev --prefix web
```

Then open http://localhost:5173. The dev server forwards `/api` requests to the API at http://localhost:5180.

Cases are stored in `cases/` at the repository root, which git ignores. Both servers listen on this machine only and have no login, so don't expose them to a network or load real patient records into a shared deployment.

## Command line
```bash
dotnet run --project MedicalRecordAnalysisCLI -- ingest <records-folder> [-o PICaseData.json] [--cache <folder>]
```

```bash
dotnet run --project MedicalRecordAnalysisCLI -- draft PICaseData.json [-o Draft.md]
```

Name the draft output `.txt` to get plain text instead of Markdown.

## Tests
```bash
dotnet test
```

```bash
npm test --prefix web
```

Both suites run offline. Tests that call the real Anthropic API are skipped unless `RUN_LIVE_TESTS=1` is set, and they cost money.
