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
