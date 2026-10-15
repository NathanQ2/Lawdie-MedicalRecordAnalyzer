import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter, Route, Routes, useLocation } from 'react-router'
import { describe, expect, it, vi } from 'vitest'
import type { CaseListItem } from '../api/types'
import { useCaseActions } from './useCaseActions'

const { deleteCase, show } = vi.hoisted(() => ({ deleteCase: vi.fn(), show: vi.fn() }))
vi.mock('../api/client', () => ({ api: { deleteCase } }))
vi.mock('@mantine/notifications', () => ({ notifications: { show } }))
// Confirm every prompt straight away.
vi.mock('@mantine/modals', () => ({ modals: { openConfirmModal: (options: { onConfirm: () => void }) => options.onConfirm() } }))

const item = (id: string, name: string): CaseListItem => ({
  id,
  name,
  dateOfLoss: '2026-01-09',
  createdAt: '2026-10-10T12:00:00Z',
  updatedAt: '2026-10-10T12:00:00Z',
  recordCount: 0,
  hasAnalysis: false,
  hasDraft: false,
  job: null,
})

const open = item('abc123def456', 'Ellis v. Cascade Freight')
const other = item('fed654cba321', 'Other case')

function Actions({ onNavigate }: { onNavigate?: () => void }) {
  const { run } = useCaseActions(onNavigate)
  const location = useLocation()
  return (
    <>
      <p>At {location.pathname}</p>
      <button onClick={() => run(open, 'issues')}>Issues</button>
      <button onClick={() => run(open, 'delete')}>Delete open</button>
      <button onClick={() => run(other, 'delete')}>Delete other</button>
    </>
  )
}

const renderAt = (path: string, onNavigate?: () => void) =>
  render(
    <QueryClientProvider client={new QueryClient()}>
      <MemoryRouter initialEntries={[path]}>
        <Routes>
          <Route path="*" element={<Actions onNavigate={onNavigate} />} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  )

describe('useCaseActions', () => {
  it('opens a case page and reports the navigation', () => {
    const onNavigate = vi.fn()
    renderAt('/', onNavigate)

    fireEvent.click(screen.getByRole('button', { name: 'Issues' }))

    expect(screen.getByText('At /cases/abc123def456/issues')).toBeInTheDocument()
    expect(onNavigate).toHaveBeenCalledOnce()
  })

  it('leaves the case pages after deleting the open case', async () => {
    deleteCase.mockResolvedValue(undefined)
    renderAt('/cases/abc123def456/overview')

    fireEvent.click(screen.getByRole('button', { name: 'Delete open' }))

    await waitFor(() => expect(screen.getByText('At /')).toBeInTheDocument())
    expect(deleteCase).toHaveBeenCalledWith('abc123def456')
    await waitFor(() => expect(show).toHaveBeenCalledWith(expect.objectContaining({ message: 'Deleted "Ellis v. Cascade Freight".' })))
  })

  it('stays on the open case after deleting another one', async () => {
    deleteCase.mockResolvedValue(undefined)
    show.mockClear()
    renderAt('/cases/abc123def456/overview')

    fireEvent.click(screen.getByRole('button', { name: 'Delete other' }))

    await waitFor(() => expect(show).toHaveBeenCalledWith(expect.objectContaining({ message: 'Deleted "Other case".' })))
    expect(screen.getByText('At /cases/abc123def456/overview')).toBeInTheDocument()
  })
})
