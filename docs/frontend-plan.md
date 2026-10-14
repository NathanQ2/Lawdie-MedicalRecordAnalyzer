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
| Draft | Editable a paragraph at a time, with clickable source notes; placeholders filled from the firm profile and the case's letter details; exported as Word (`.docx`, with footnotes), Markdown, or plain text |
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
- `draft.json`: the draft as structured segments with their citations, including the attorney's edits. Placeholders stay in brackets here and are filled in when the letter is served. Exports are rendered from it on request, not stored.
- `meta.json`: the case name, intake details, timestamps, job status, review checklist state, and the values entered for the letter's placeholders.

The firm's profile is `firm.json` beside the case folders.
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
   - Each review item links to the sentences it affects in the letter. For a contradiction, these are the sentences whose citations overlap its quotes, so the attorney knows what to revise once it is resolved.
6. ✅ **Case management**
   - Rename and delete cases from a ⋮ or right-click menu on the case list, with confirmation before deleting.

7. ✅ **OCR**
   - Scanned PDF pages and images are read with Azure Document Intelligence when it is configured. Quotes from them are flagged in the source viewer and listed as "Scanned text" review items.

8. In progress: **Case workspace** (option B of the UX redesign report)
   - A case opens on a workspace with a page menu: Overview, Documents, Chronology, Issues and Letter, replacing the five-step flow. Old step links redirect.
   - Overview shows the next step, the key facts with their sources, the matter details and the case summary.
   - Chronology lists every visit by date or by provider, with flags for contradictions, unconfirmed dates or providers, scanned quotes and visits before the incident.
   - Issues gathers contradictions and intake details the documents disagree with, such as the date of loss.
   - A source panel docks beside every page except the letter, which keeps its own.
   - The letter can be edited a paragraph at a time. Source numbers stay attached to the sentences they follow, only the letter's own sources can be used, and sentences left without one are listed for review. Drafting again warns that it replaces the edits.
   - A firm profile fills the letterhead and signature of every letter, and a case's letter details fill its other placeholders. Placeholder review items have a "Fill in" button.
   - Next: review items shown inline beside the letter's text, and a Damages page.

Later: login and cloud storage.

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
| `GET /api/cases/{id}/draft?fill=false` | The letter as drafted, before its placeholders are filled, which edits apply to |
| `PUT /api/cases/{id}/draft/text` | Edit a stretch of the letter's text |
| `GET/PUT /api/cases/{id}/letter/placeholders` | List the letter's placeholders with what fills each, and set the case's values |
| `GET/PUT /api/firm` | Read or save the firm profile |

## Notes
- **Quote highlighting in PDFs.** Our quote positions come from PdfPig's extracted text, which does not line up exactly with how pdf.js lays text out. The viewer jumps to the cited page and searches that page's text for the quote, ignoring whitespace differences. This is reliable when a quote sits on one page and approximate otherwise.
- **Testing.** API tests run against ASP.NET's in-memory test server with the dummy providers, and frontend logic is tested with Vitest. Live API runs still need approval each time.
