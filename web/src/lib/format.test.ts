import { describe, expect, it } from 'vitest'
import type { JobStatus, RecordInfo } from '../api/types'
import { formatBytes, formatDateOnly, formatDuration, isJobActive, jobProgress, recordsNeedingSummaries, stageLabel } from './format'

const job = (overrides: Partial<JobStatus>): JobStatus => ({
  id: 'job',
  kind: 'Ingest',
  state: 'Running',
  stage: null,
  completed: 0,
  total: 0,
  currentFile: null,
  error: null,
  queuedAt: '2026-10-10T12:00:00Z',
  startedAt: null,
  finishedAt: null,
  ...overrides,
})

describe('formatBytes', () => {
  it.each([
    [512, '512 B'],
    [2048, '2.0 KB'],
    [150 * 1024, '150 KB'],
    [5 * 1024 * 1024, '5.0 MB'],
  ])('formats %d bytes as %s', (bytes, expected) => expect(formatBytes(bytes)).toBe(expected))
})

describe('formatDuration', () => {
  it('shows seconds, then minutes and seconds', () => {
    expect(formatDuration(42_000)).toBe('42s')
    expect(formatDuration(229_000)).toBe('3m 49s')
    expect(formatDuration(-5)).toBe('0s')
  })
})

describe('jobs', () => {
  it('treats queued and running jobs as active', () => {
    expect(isJobActive(job({ state: 'Queued' }))).toBe(true)
    expect(isJobActive(job({ state: 'Running' }))).toBe(true)
    expect(isJobActive(job({ state: 'Succeeded' }))).toBe(false)
    expect(isJobActive(null)).toBe(false)
  })

  it('reports per-record progress while summarizing', () => {
    const progress = jobProgress(job({ stage: 'SummarizingDocuments', completed: 7, total: 23, currentFile: 'ed.pdf' }))
    expect(progress).toEqual({ percent: 30, detail: '7 of 23 documents · ed.pdf' })
  })

  it('shows indeterminate progress for single-call stages', () => {
    expect(jobProgress(job({ stage: 'FindingContradictions', total: 1 }))).toEqual({ percent: null, detail: '' })
    expect(stageLabel('FindingContradictions')).toBe('Looking for contradictions')
  })
})

describe('recordsNeedingSummaries', () => {
  it('counts records that are new or changed', () => {
    const record = (status: RecordInfo['status']): RecordInfo => ({ name: status, size: 1, modifiedAt: '', status, reason: null })
    expect(recordsNeedingSummaries([record('NotIngested'), record('Changed'), record('Summarized'), record('Skipped')])).toBe(2)
  })
})

describe('formatDateOnly', () => {
  it('shows the calendar date without shifting it across time zones', () => {
    expect(formatDateOnly('2026-01-09')).toBe(new Date(2026, 0, 9).toLocaleDateString(undefined, { dateStyle: 'medium' }))
    expect(formatDateOnly(null)).toBe('Not entered')
  })
})
