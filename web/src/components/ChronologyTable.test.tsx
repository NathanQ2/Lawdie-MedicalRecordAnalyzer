import { fireEvent, screen, within } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import type { ChronologyEntry, Citation } from '../api/types'
import { renderWithMantine } from '../test/render'
import { ChronologyTable } from './ChronologyTable'

function quote(filePath: string, citedText: string): Citation {
  return { filePath, citedText, startPosition: 0, citationLength: citedText.length, claim: null, pageNumber: 2, endPageNumber: null }
}

const entries: ChronologyEntry[] = [
  {
    date: '2023-05-02',
    provider: 'Lakeside Chiropractic',
    visitType: 'New patient exam',
    finding: 'Neck stiffness after lifting boxes',
    citations: [quote('/r/22_Chiropractic.txt', 'Neck stiffness and upper back pain')],
    dateConfirmed: true,
    providerConfirmed: true,
  },
  {
    date: '2026-02-24',
    provider: 'Cascade Orthopedic & Spine',
    visitType: 'Spine consult',
    finding: 'Right C6 radiculopathy',
    citations: [quote('/r/10_Ortho.pdf', 'Right C6 cervical radiculopathy'), quote('/r/10_Ortho.pdf', 'Remain off work.')],
    dateConfirmed: false,
    providerConfirmed: true,
  },
]

describe('ChronologyTable', () => {
  it('lists each visit with its source and what to check', () => {
    renderWithMantine(<ChronologyTable entries={entries} contradictions={[]} dateOfLoss="2026-01-09" groupBy="date" selectedKey={null} onSelect={() => {}} />)

    const rows = screen.getAllByRole('row').slice(1)
    expect(within(rows[0]).getByText('Lakeside Chiropractic')).toBeInTheDocument()
    expect(within(rows[0]).getByText('Pre-incident')).toBeInTheDocument()
    expect(within(rows[1]).getByText('10_Ortho.pdf')).toBeInTheDocument()
    expect(within(rows[1]).getByText('p. 2 · 1 more')).toBeInTheDocument()
    expect(within(rows[1]).getByText('Check date')).toBeInTheDocument()
  })

  it('opens the first quote of the visit that is clicked', () => {
    const onSelect = vi.fn()
    renderWithMantine(<ChronologyTable entries={entries} contradictions={[]} dateOfLoss={null} groupBy="date" selectedKey={null} onSelect={onSelect} />)

    fireEvent.click(screen.getByText('Spine consult'))

    expect(onSelect).toHaveBeenCalledWith(entries[1].citations[0])
  })

  it('groups visits under their provider', () => {
    renderWithMantine(<ChronologyTable entries={entries} contradictions={[]} dateOfLoss={null} groupBy="provider" selectedKey={null} onSelect={() => {}} />)

    expect(screen.getByText('Cascade Orthopedic & Spine')).toBeInTheDocument()
    expect(screen.getAllByText('1 visit')).toHaveLength(2)
    expect(screen.queryByRole('columnheader', { name: 'Provider' })).not.toBeInTheDocument()
  })
})
