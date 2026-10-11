// Mirrors the API's JSON (camelCase properties, enums as strings).

export type JobKind = 'Ingest' | 'Draft'
export type JobState = 'Queued' | 'Running' | 'Succeeded' | 'Failed' | 'Cancelled'
export type PipelineStage = 'SummarizingDocuments' | 'FindingContradictions' | 'SummarizingCase' | 'Drafting'
export type RecordStatus = 'NotIngested' | 'Summarized' | 'Skipped' | 'Changed'

export interface JobStatus {
  id: string
  kind: JobKind
  state: JobState
  stage: PipelineStage | null
  completed: number
  total: number
  currentFile: string | null
  error: string | null
  queuedAt: string
  startedAt: string | null
  finishedAt: string | null
}

export interface RecordInfo {
  name: string
  size: number
  modifiedAt: string
  status: RecordStatus
  reason: string | null
}

export interface CaseListItem {
  id: string
  name: string
  createdAt: string
  updatedAt: string
  recordCount: number
  hasAnalysis: boolean
  hasDraft: boolean
  job: JobStatus | null
}

export interface CaseDetail {
  id: string
  name: string
  createdAt: string
  updatedAt: string
  records: RecordInfo[]
  analysisUpdatedAt: string | null
  analysisOutOfDate: boolean
  draftUpdatedAt: string | null
  draftOutOfDate: boolean
  job: JobStatus | null
}

// The ingest analysis (PICase). File paths are absolute paths on the API server; the record name is the last
// path segment.

export interface Citation {
  filePath: string
  citedText: string
  startPosition: number
  citationLength: number
  claim: string | null
  pageNumber: number | null
  endPageNumber: number | null
}

export interface DocumentSummary {
  summary: string
  citations: Citation[]
}

export interface CaseSummary {
  summary: string
  citations: Citation[]
  unsupportedStatements: string[]
}

export interface Contradiction {
  summary: string
  citations: Citation[]
}

export interface SkippedFile {
  filePath: string
  reason: string
}

export interface SourceDocument {
  filePath: string
  pageStartPositions: number[]
}

export interface CaseAnalysis {
  documentSummaries: DocumentSummary[]
  summary: CaseSummary | null
  contradictions: Contradiction[]
  sources: SourceDocument[]
  skippedFiles: SkippedFile[]
}

export interface DraftSegment {
  text: string
  citations: Citation[]
}

export interface DemandDraft {
  segments: DraftSegment[]
  unsupportedStatements: string[]
}

export type ReviewItemKind = 'Contradiction' | 'UnsupportedStatement' | 'Placeholder'
export type ReviewStatus = 'Open' | 'Resolved' | 'Dismissed'

export interface ReviewItem {
  id: string
  kind: ReviewItemKind
  text: string
  citations: Citation[]
  status: ReviewStatus
  note: string | null
}
