import { describe, expect, it } from 'vitest'
import type { CaseDetail, JobStatus, RecordInfo, ReviewItem, ReviewItemKind } from '../api/types'
import { nextAction, openIssueCount, openLetterItemCount, sectionFromPath } from './workspace'

const record: RecordInfo = { name: 'ed.pdf', size: 100, modifiedAt: '2026-10-01T10:00:00Z', status: 'Summarized', reason: null }

function caseDetail(overrides: Partial<CaseDetail> = {}): CaseDetail {
  return {
    id: 'abc123def456',
    name: 'Ellis v. Cascade Freight',
    dateOfLoss: '2026-01-09',
    jurisdiction: null,
    county: null,
    createdAt: '2026-10-01T09:00:00Z',
    updatedAt: '2026-10-01T09:00:00Z',
    records: [record],
    analysisUpdatedAt: '2026-10-01T11:00:00Z',
    analysisOutOfDate: false,
    analysisReviewed: false,
    draftUpdatedAt: '2026-10-01T12:00:00Z',
    draftOutOfDate: false,
    job: null,
    ...overrides,
  }
}

function item(kind: ReviewItemKind, status: ReviewItem['status'] = 'Open'): ReviewItem {
  return { id: `${kind}-${status}-${Math.random()}`, kind, text: kind, citations: [], status, note: null, draftRanges: [] }
}

const runningJob: JobStatus = { id: 'j', kind: 'Ingest', state: 'Running', stage: null, completed: 0, total: 3, currentFile: null, error: null, queuedAt: '', startedAt: null, finishedAt: null }

describe('nextAction', () => {
  it('walks a case from its documents to the export', () => {
    expect(nextAction(caseDetail({ records: [], analysisUpdatedAt: null, draftUpdatedAt: null }), []).section).toBe('records')
    expect(nextAction(caseDetail({ analysisUpdatedAt: null, draftUpdatedAt: null }), []).title).toBe('Analyze the documents')
    expect(nextAction(caseDetail({ analysisOutOfDate: true }), []).title).toBe('Analyze the documents again')
    expect(nextAction(caseDetail(), [item('Contradiction'), item('IntakeMismatch')])).toMatchObject({ title: 'Check 2 issues', section: 'issues' })
    expect(nextAction(caseDetail({ draftUpdatedAt: null }), [item('Contradiction', 'Resolved')])).toMatchObject({ title: 'Draft the demand letter', section: 'letter' })
    expect(nextAction(caseDetail({ draftOutOfDate: true }), []).title).toBe('Draft the letter again')
    expect(nextAction(caseDetail(), [item('Placeholder')]).title).toBe('Clear 1 letter item')
    expect(nextAction(caseDetail(), [item('Placeholder', 'Dismissed')]).title).toBe('Export the letter')
  })

  it('points to a running job before anything else', () => {
    expect(nextAction(caseDetail({ job: runningJob }), [item('Contradiction')])).toMatchObject({ title: 'The documents are being analyzed', section: 'records' })
    expect(nextAction(caseDetail({ job: { ...runningJob, kind: 'Draft' } }), []).section).toBe('letter')
  })
})

describe('review counts', () => {
  it('counts open issues and open letter items separately', () => {
    const items = [item('Contradiction'), item('IntakeMismatch'), item('Contradiction', 'Resolved'), item('UnsupportedStatement'), item('Placeholder'), item('RecognizedQuote', 'Dismissed')]

    expect(openIssueCount(items)).toBe(2)
    expect(openLetterItemCount(items)).toBe(2)
    expect(openIssueCount(undefined)).toBe(0)
  })
})

describe('sectionFromPath', () => {
  it('reads the page from a case path', () => {
    expect(sectionFromPath('/cases/abc123def456/chronology')).toBe('chronology')
    expect(sectionFromPath('/cases/abc123def456/records')).toBe('records')
    expect(sectionFromPath('/cases/abc123def456')).toBeNull()
  })
})
