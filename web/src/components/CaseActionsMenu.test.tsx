import { fireEvent, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import type { CaseListItem } from '../api/types'
import { renderWithMantine } from '../test/render'
import { CaseActionsMenu, CaseContextMenu } from './CaseActionsMenu'

const item: CaseListItem = {
  id: 'abc123def456',
  name: 'Ellis v. Cascade Freight',
  dateOfLoss: '2026-01-09',
  createdAt: '2026-10-10T12:00:00Z',
  updatedAt: '2026-10-10T12:00:00Z',
  recordCount: 24,
  hasAnalysis: true,
  hasDraft: false,
  job: null,
}

const open = (overrides: Partial<CaseListItem> = {}, onAction = vi.fn()) => {
  renderWithMantine(<CaseActionsMenu item={{ ...item, ...overrides }} opened onAction={onAction} target={<button>menu</button>} />)
  return onAction
}

describe('CaseActionsMenu', () => {
  it('runs the chosen action', () => {
    const onAction = open()

    fireEvent.click(screen.getByRole('menuitem', { name: 'Open' }))
    fireEvent.click(screen.getByRole('menuitem', { name: 'Rename…' }))
    fireEvent.click(screen.getByRole('menuitem', { name: 'Delete…' }))

    expect(onAction.mock.calls.map(([action]) => action)).toEqual(['open', 'rename', 'delete'])
  })

  it('only offers the letter and its download once the case is drafted', () => {
    open()

    expect(screen.getByRole('menuitem', { name: 'Chronology' })).not.toBeDisabled()
    expect(screen.getByRole('menuitem', { name: 'Issues' })).not.toBeDisabled()
    expect(screen.getByRole('menuitem', { name: 'Letter' })).toBeDisabled()
    expect(screen.queryByRole('menuitem', { name: 'Download letter (Word)' })).not.toBeInTheDocument()
  })

  it('links the Word download for a drafted case', () => {
    open({ hasDraft: true })

    expect(screen.getByRole('menuitem', { name: 'Download letter (Word)' })).toHaveAttribute('href', '/api/cases/abc123def456/draft/export?format=docx')
  })

  it('disables delete while a job is running', () => {
    open({
      job: { id: 'j', kind: 'Ingest', state: 'Running', stage: null, completed: 0, total: 0, currentFile: null, error: null, queuedAt: '', startedAt: null, finishedAt: null },
    })

    expect(screen.getByRole('menuitem', { name: 'Delete…' })).toBeDisabled()
  })
})

describe('CaseContextMenu', () => {
  it('runs the chosen action and closes', () => {
    const onClose = vi.fn()
    const onAction = vi.fn()
    renderWithMantine(<CaseContextMenu point={{ item, x: 40, y: 200 }} onClose={onClose} onAction={onAction} />)

    fireEvent.click(screen.getByRole('menuitem', { name: 'Issues' }))

    expect(onAction).toHaveBeenCalledWith('issues')
    expect(onClose).toHaveBeenCalled()
  })
})
