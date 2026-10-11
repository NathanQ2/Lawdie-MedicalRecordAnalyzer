# Frontend plan

A browser UI for the full workflow: upload a case's records, run `ingest` and `draft` as background jobs, then review the results against the source records.

## Decisions
| Topic | Decision |
|---|---|
| Scope of v1 | Full workflow: upload, run, review, export |
| Stack | ASP.NET Core Web API wrapping the existing library, plus a React + TypeScript single-page app built with Vite |
| UI components | Mantine |
| Source viewer | pdf.js for PDFs; a text view for `.txt` and `.md` files |
| Hosting | Local only for now: the API listens on this computer only, has no login, and keeps the API key on the server |
| Case storage | `cases/` at the repository root (git-ignored), behind an `ICaseStore` interface so cloud storage can replace it later |
| Draft | Read-only in the UI, with clickable source notes; exported as Word (`.docx`, with footnotes), Markdown, or plain text |
| Adding records later | Summarize only new or changed files, then rerun verification and the case summary over the whole case |

Real patient records must not go into a shared deployment until login and HIPAA controls exist.

## Architecture
```
web/ (React + TS, Vite, Mantine)  ──HTTP──▶  MedicalRecordAnalysis.Api (ASP.NET Core)
   pdf.js source viewer                        ├─ ICaseStore  → cases/<id>/ on disk
                                               ├─ IJobQueue   → in-process background worker
                                               └─ MedicalRecordAnalysis (existing library)
```

Each case folder contains:
- `records/`: the uploaded documents.
- `case.json`: the `PICase` (the ingest result).
- `draft.json`: the draft as structured segments with their citations. Exports are rendered from it on request, not stored.
- `meta.json`: the case name, timestamps, job status, and review checklist state.
- `cache/`: a per-document summary cache, so `ingest` can resume after a failure or when documents are added.

The UI checks the status of a running job every couple of seconds. Live updates (server-sent events) can come later.

## Milestones
1. ✅ **Library refactor** (the CLI's behavior is unchanged)
   - `Ingest` and `Draft` return a `PICase` and a `DemandDraft` instead of writing files, and support progress reporting and cancellation.
   - `ingest` saves each document's summary as it finishes. Unchanged files are reused, so a failure or newly added records only cost the new work.
   - Skipped files and the reason for each are part of the result.
   - The draft is also saved as structured JSON (segments with their citations).
2. ✅ **API**
   - The case store, file uploads, serving original files, the job worker, and the endpoints listed below.
   - Integration tests use the dummy providers, so they cost nothing to run.
3. ✅ **Frontend setup**
   - A Vite + React + Mantine app with the Cases, Records and Run screens.
   - At this point the whole pipeline runs from the browser.
4. ✅ **Analysis screen and source viewer**
   - The case summary with clickable citations, uncited sentences highlighted, side-by-side contradictions, and per-record summaries.
   - Clicking a citation opens the file at the right page with the quote highlighted.
5. ✅ **Draft screen**
   - The narrative with clickable source notes, an attorney review checklist (resolve or dismiss each item, with a note), and Word, Markdown, or text export.
6. ✅ **Case management**
   - Rename and delete cases from a ⋮ or right-click menu on the case list, with confirmation before deleting.

7. ✅ **OCR**
   - Scanned PDF pages and images are read with Azure Document Intelligence when it is configured. Quotes from them are flagged in the source viewer and listed as "Scanned text" review items.

Later: login and cloud storage, and editing the draft in the browser.

## API endpoints
| Endpoint | Purpose |
|---|---|
| `POST/GET /api/cases`, `GET /api/cases/{id}` | Create, list, and open cases |
| `PATCH/DELETE /api/cases/{id}` | Rename or delete a case |
| `POST/GET /api/cases/{id}/records`, `DELETE /api/cases/{id}/records/{file}` | Upload documents, list them with their read status, and remove one |
| `GET /api/cases/{id}/records/{file}` | Serve an original document to the source viewer |
| `POST /api/cases/{id}/ingest`, `POST /api/cases/{id}/draft` | Start a job |
| `GET /api/cases/{id}/jobs/current`, `POST /api/cases/{id}/jobs/cancel` | Check job progress, or cancel it |
| `GET /api/cases/{id}/analysis`, `GET /api/cases/{id}/draft` | Get the results |
| `GET /api/cases/{id}/draft/export?format=docx\|md\|txt` | Download the draft |
| `GET /api/cases/{id}/review` | List the attorney review checklist |
| `PUT /api/cases/{id}/review/{itemId}` | Resolve or dismiss a review item, with a note |

## Notes
- **Quote highlighting in PDFs.** Our quote positions come from PdfPig's extracted text, which does not line up exactly with how pdf.js lays text out. The viewer jumps to the cited page and searches that page's text for the quote, ignoring whitespace differences. This is reliable when a quote sits on one page and approximate otherwise.
- **Testing.** API tests run against ASP.NET's in-memory test server with the dummy providers, and frontend logic is tested with Vitest. Live API runs still need approval each time.
