import { describe, expect, it } from 'vitest'
import type { CaseDetail, RecordInfo } from '../api/types'
import { adjacentSteps, caseSteps, firstOpenStep, stepFromPath } from './steps'

const record: RecordInfo = { name: 'ems.pdf', size: 100, modifiedAt: '2026-10-10T12:00:00Z', status: 'Summarized', reason: null }

const newCase: CaseDetail = {
  id: 'abc123def456',
  name: 'Ellis v. Cascade Freight',
  dateOfLoss: null,
  jurisdiction: null,
  county: null,
  createdAt: '2026-10-10T12:00:00Z',
  updatedAt: '2026-10-10T12:00:00Z',
  records: [],
  analysisUpdatedAt: null,
  analysisOutOfDate: false,
  analysisReviewed: false,
  draftUpdatedAt: null,
  draftOutOfDate: false,
  job: null,
}

const states = (detail: CaseDetail) => caseSteps(detail).map((step) => step.state)

describe('caseSteps', () => {
  it('locks analysis until there are documents, and review and the letter until there is an analysis', () => {
    const steps = caseSteps(newCase)

    expect(states(newCase)).toEqual(['todo', 'todo', 'locked', 'locked', 'locked'])
    expect(steps[2].lockedReason).toBe('Add documents first.')
    expect(steps[0].lockedReason).toBeNull()
  })

  it('marks each step done as the case moves through them', () => {
    const drafted = {
      ...newCase,
      dateOfLoss: '2026-01-09',
      records: [record],
      analysisUpdatedAt: '2026-10-10T13:00:00Z',
      draftUpdatedAt: '2026-10-10T14:00:00Z',
    }

    expect(states({ ...newCase, dateOfLoss: '2026-01-09', records: [record] })).toEqual(['done', 'done', 'todo', 'locked', 'locked'])
    expect(states(drafted)).toEqual(['done', 'done', 'done', 'done', 'done'])
    expect(states({ ...drafted, analysisOutOfDate: true, draftOutOfDate: true })).toEqual(['done', 'done', 'todo', 'todo', 'todo'])
  })

  it('counts the review as done once the attorney moves on from it, until the analysis is redone', () => {
    const analyzed = { ...newCase, dateOfLoss: '2026-01-09', records: [record], analysisUpdatedAt: '2026-10-10T13:00:00Z' }

    expect(states(analyzed)[3]).toBe('todo')
    expect(states({ ...analyzed, analysisReviewed: true })[3]).toBe('done')
  })
})

describe('firstOpenStep', () => {
  it('opens on the first step still to do', () => {
    expect(firstOpenStep(caseSteps(newCase))).toBe('matter')
    expect(firstOpenStep(caseSteps({ ...newCase, dateOfLoss: '2026-01-09', records: [record] }))).toBe('analyze')
  })

  it('opens on the letter once everything is done', () => {
    const done = { ...newCase, dateOfLoss: '2026-01-09', records: [record], analysisUpdatedAt: 'x', draftUpdatedAt: 'y' }

    expect(firstOpenStep(caseSteps(done))).toBe('letter')
  })
})

describe('adjacentSteps', () => {
  it('returns the neighbors of the current step, locked or not', () => {
    const steps = caseSteps(newCase)

    expect(adjacentSteps(steps, 'matter')).toEqual({ previous: null, next: steps[1] })
    expect(adjacentSteps(steps, 'records').next?.state).toBe('locked')
    expect(adjacentSteps(steps, 'letter')).toEqual({ previous: steps[3], next: null })
    expect(adjacentSteps(steps, null)).toEqual({ previous: null, next: null })
  })
})

describe('stepFromPath', () => {
  it('reads the step from the end of the path', () => {
    expect(stepFromPath('/cases/abc123def456/review')).toBe('review')
    expect(stepFromPath('/cases/abc123def456')).toBeNull()
  })
})
