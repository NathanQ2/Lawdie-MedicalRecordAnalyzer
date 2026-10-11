import { fireEvent, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import type { JobStatus } from '../api/types'
import { renderWithMantine } from '../test/render'
import { JobStatusCard } from './JobStatusCard'

const NOW = Date.parse('2026-10-10T12:01:05Z')

const running: JobStatus = {
  id: 'job',
  kind: 'Ingest',
  state: 'Running',
  stage: 'SummarizingDocuments',
  completed: 3,
  total: 23,
  currentFile: '04_PCP_Visit.txt',
  error: null,
  queuedAt: '2026-10-10T12:00:00Z',
  startedAt: '2026-10-10T12:00:00Z',
  finishedAt: null,
}

describe('JobStatusCard', () => {
  it('shows the stage, per-record progress, and elapsed time while running', () => {
    renderWithMantine(<JobStatusCard job={running} now={NOW} />)

    expect(screen.getByText('Summarizing documents')).toBeInTheDocument()
    expect(screen.getByText('3 of 23 documents — 04_PCP_Visit.txt')).toBeInTheDocument()
    expect(screen.getByText(/running for 1m 5s/)).toBeInTheDocument()
  })

  it('offers cancel only while the job is active', () => {
    const onCancel = vi.fn()
    const { rerender } = renderWithMantine(<JobStatusCard job={running} onCancel={onCancel} now={NOW} />)

    fireEvent.click(screen.getByRole('button', { name: 'Cancel' }))
    expect(onCancel).toHaveBeenCalledOnce()

    rerender(<JobStatusCard job={{ ...running, state: 'Succeeded', finishedAt: '2026-10-10T12:04:00Z' }} onCancel={onCancel} now={NOW} />)
    expect(screen.queryByRole('button', { name: 'Cancel' })).not.toBeInTheDocument()
  })

  it('shows the error when the job failed', () => {
    renderWithMantine(<JobStatusCard job={{ ...running, state: 'Failed', error: 'No Anthropic API key is configured.' }} now={NOW} />)

    expect(screen.getByText('No Anthropic API key is configured.')).toBeInTheDocument()
  })
})
