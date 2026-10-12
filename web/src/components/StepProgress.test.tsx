import { screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router'
import { describe, expect, it } from 'vitest'
import type { CaseDetail, JobStatus, RecordInfo } from '../api/types'
import { caseSteps, type StepId } from '../lib/steps'
import { renderWithMantine } from '../test/render'
import { StepProgress } from './StepProgress'

const record: RecordInfo = { name: 'ems.pdf', size: 100, modifiedAt: '2026-10-10T12:00:00Z', status: 'NotIngested', reason: null }

const detail: CaseDetail = {
  id: 'abc123def456',
  name: 'Ellis v. Cascade Freight',
  dateOfLoss: '2026-01-09',
  jurisdiction: 'Washington',
  county: 'King',
  createdAt: '2026-10-10T12:00:00Z',
  updatedAt: '2026-10-10T12:00:00Z',
  records: [record],
  analysisUpdatedAt: null,
  analysisOutOfDate: false,
  analysisReviewed: false,
  draftUpdatedAt: null,
  draftOutOfDate: false,
  job: null,
}

const analysis = (overrides: Partial<JobStatus>): JobStatus => ({
  id: 'j',
  kind: 'Ingest',
  state: 'Running',
  stage: 'SummarizingDocuments',
  completed: 10,
  total: 25,
  currentFile: '11_PCP_Followup.txt',
  error: null,
  queuedAt: '2026-10-10T12:00:00Z',
  startedAt: '2026-10-10T12:00:00Z',
  finishedAt: null,
  ...overrides,
})

const render = (current: StepId, job: JobStatus | null) =>
  renderWithMantine(
    <MemoryRouter>
      <StepProgress caseId={detail.id} steps={caseSteps(detail)} current={current} job={job} />
    </MemoryRouter>,
  )

const fill = (container: HTMLElement) => container.querySelector<HTMLElement>('[class*="fill"]')

describe('StepProgress', () => {
  it('fills the current step with the running analysis progress', () => {
    const { container } = render('analyze', analysis({}))

    expect(fill(container)?.style.getPropertyValue('--progress')).toBe('40%')
    expect(fill(container)?.parentElement).toBe(screen.getByRole('link', { name: 'Analyze' }))
    expect(container.querySelector('[class*="marker"]')).toHaveAttribute('data-working')
  })

  it('fills the analysis segment while another step is open', () => {
    const { container } = render('records', analysis({}))

    expect(fill(container)?.parentElement).toBe(screen.getByRole('link', { name: 'Analyze' }))
  })

  it('shimmers when the job has no count to show, and starts empty while queued', () => {
    const running = render('analyze', analysis({ stage: 'FindingContradictions' }))
    expect(fill(running.container)).toHaveAttribute('data-indeterminate')
    running.unmount()

    const queued = render('analyze', analysis({ state: 'Queued', stage: null }))
    expect(fill(queued.container)?.style.getPropertyValue('--progress')).toBe('0%')
    expect(fill(queued.container)).not.toHaveAttribute('data-indeterminate')
  })

  it('shows no fill once the job has finished', () => {
    const { container } = render('analyze', analysis({ state: 'Succeeded' }))

    expect(fill(container)).toBeNull()
  })
})
