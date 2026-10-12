import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, waitFor } from '@testing-library/react'
import type { ReactNode } from 'react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import type { CaseDetail, JobStatus } from '../api/types'
import { useCase } from './useCase'

const { show, getCase } = vi.hoisted(() => ({ show: vi.fn(), getCase: vi.fn() }))
vi.mock('@mantine/notifications', () => ({ notifications: { show } }))
vi.mock('../api/client', () => ({ api: { getCase } }))

const job = (state: JobStatus['state']): JobStatus => ({
  id: 'job-1',
  kind: 'Ingest',
  state,
  stage: null,
  completed: 0,
  total: 0,
  currentFile: null,
  error: state === 'Cancelled' ? 'Cancelled by the user.' : null,
  queuedAt: '2026-10-10T12:00:00Z',
  startedAt: null,
  finishedAt: null,
})

const detail = (state: JobStatus['state']) => ({ id: 'abc123def456', job: job(state) }) as CaseDetail

function CaseReader() {
  useCase('abc123def456')
  return null
}

afterEach(() => vi.clearAllMocks())

describe('useCase', () => {
  it('announces a finished job once, however many components load the case', async () => {
    getCase.mockResolvedValue(detail('Running'))
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    const wrapper = ({ children }: { children: ReactNode }) => <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>
    render(
      <>
        <CaseReader />
        <CaseReader />
        <CaseReader />
      </>,
      { wrapper },
    )
    await waitFor(() => expect(queryClient.getQueryData<CaseDetail>(['cases', 'abc123def456'])?.job?.state).toBe('Running'))

    getCase.mockResolvedValue(detail('Cancelled'))
    await queryClient.refetchQueries()

    await waitFor(() => expect(show).toHaveBeenCalledTimes(1))
    expect(show).toHaveBeenCalledWith(expect.objectContaining({ title: 'Analysis cancelled' }))
  })
})
