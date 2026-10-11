import type { JobState, JobStatus, PipelineStage, RecordInfo, RecordStatus } from '../api/types'

export function formatBytes(bytes: number): string {
  if (bytes < 1024) {
    return `${bytes} B`
  }

  const units = ['KB', 'MB', 'GB']
  let value = bytes / 1024
  let unit = 0
  while (value >= 1024 && unit < units.length - 1) {
    value /= 1024
    unit++
  }

  return `${value < 10 ? value.toFixed(1) : Math.round(value)} ${units[unit]}`
}

export function formatDateTime(iso: string | null): string {
  return iso ? new Date(iso).toLocaleString(undefined, { dateStyle: 'medium', timeStyle: 'short' }) : '—'
}

export function formatDuration(milliseconds: number): string {
  const seconds = Math.max(0, Math.round(milliseconds / 1000))
  const minutes = Math.floor(seconds / 60)
  return minutes > 0 ? `${minutes}m ${seconds % 60}s` : `${seconds}s`
}

export function isJobActive(job: JobStatus | null | undefined): boolean {
  return job?.state === 'Queued' || job?.state === 'Running'
}

const stageLabels: Record<PipelineStage, string> = {
  SummarizingDocuments: 'Summarizing documents',
  FindingContradictions: 'Checking documents for contradictions',
  SummarizingCase: 'Writing the case summary',
  Drafting: 'Drafting the demand letter',
}

export function stageLabel(stage: PipelineStage | null): string {
  return stage ? stageLabels[stage] : 'Starting'
}

export const jobStateColors: Record<JobState, string> = {
  Queued: 'gray',
  Running: 'blue',
  Succeeded: 'green',
  Failed: 'red',
  Cancelled: 'orange',
}

/**
 * Determinate progress while records are summarized (one step per record); the other stages are single calls.
 */
export function jobProgress(job: JobStatus): { percent: number | null; detail: string } {
  if (job.state !== 'Running') {
    return { percent: job.state === 'Succeeded' ? 100 : null, detail: job.state === 'Queued' ? 'Waiting to start' : '' }
  }

  if (job.stage === 'SummarizingDocuments' && job.total > 0) {
    const current = job.completed < job.total ? ` — ${job.currentFile ?? ''}` : ''
    return {
      percent: Math.round((job.completed / job.total) * 100),
      detail: `${job.completed} of ${job.total} documents${current}`,
    }
  }

  return { percent: null, detail: '' }
}

export const recordStatusLabels: Record<RecordStatus, string> = {
  NotIngested: 'Not ingested',
  Summarized: 'Summarized',
  Skipped: 'Skipped',
  Changed: 'Changed',
}

export const recordStatusColors: Record<RecordStatus, string> = {
  NotIngested: 'gray',
  Summarized: 'green',
  Skipped: 'orange',
  Changed: 'yellow',
}

/**
 * Records ingest will send to the LLM: everything not summarized by the last ingest, except skipped records,
 * which fail again without any API call until an OCR provider exists.
 */
export function recordsNeedingSummaries(records: RecordInfo[]): number {
  return records.filter((record) => record.status === 'NotIngested' || record.status === 'Changed').length
}
