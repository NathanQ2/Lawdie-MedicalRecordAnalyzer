import { fireEvent, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import type { RecordInfo } from '../api/types'
import { renderWithMantine } from '../test/render'
import { RecordsTable } from './RecordsTable'

const records: RecordInfo[] = [
  { name: '02_ED_Record.pdf', size: 4096, modifiedAt: '2026-10-10T12:00:00Z', status: 'Summarized', reason: null },
  { name: '09_Faxed_Referral.pdf', size: 790_000, modifiedAt: '2026-10-10T12:00:00Z', status: 'Skipped', reason: 'Needs OCR.' },
]

describe('RecordsTable', () => {
  it('lists records with their status and a link to the file', () => {
    renderWithMantine(<RecordsTable records={records} recordUrl={(name) => `/files/${name}`} />)

    expect(screen.getByRole('link', { name: '02_ED_Record.pdf' })).toHaveAttribute('href', '/files/02_ED_Record.pdf')
    expect(screen.getByText('Summarized')).toBeInTheDocument()
    expect(screen.getByText('Skipped')).toBeInTheDocument()
    expect(screen.getByText('771 KB')).toBeInTheDocument()
  })

  it('deletes a record through the callback, unless disabled', () => {
    const onDelete = vi.fn()
    const { rerender } = renderWithMantine(<RecordsTable records={records} recordUrl={(name) => name} onDelete={onDelete} />)

    fireEvent.click(screen.getByRole('button', { name: 'Delete 02_ED_Record.pdf' }))
    expect(onDelete).toHaveBeenCalledWith(records[0])

    rerender(<RecordsTable records={records} recordUrl={(name) => name} onDelete={onDelete} disabled />)
    expect(screen.getByRole('button', { name: 'Delete 02_ED_Record.pdf' })).toBeDisabled()
  })

  it('explains what to do when there are no records', () => {
    renderWithMantine(<RecordsTable records={[]} recordUrl={(name) => name} />)

    expect(screen.getByText(/No documents yet/)).toBeInTheDocument()
  })
})
