import { fireEvent, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import type { Contradiction } from '../api/types'
import { renderWithMantine } from '../test/render'
import { ContradictionCard } from './AnalysisTab'

const contradiction: Contradiction = {
  summary: 'The EMS report says she may have blacked out; the ED record says she denies loss of consciousness.',
  citations: [
    { filePath: '/r/01_EMS.pdf', citedText: 'may have "blacked out for a second"', startPosition: 10, citationLength: 35, claim: null, pageNumber: 1, endPageNumber: null },
    { filePath: '/r/02_ED.pdf', citedText: 'She denies loss of consciousness.', startPosition: 657, citationLength: 33, claim: null, pageNumber: 2, endPageNumber: null },
  ],
  isVerified: true,
}

describe('ContradictionCard', () => {
  it('shows each quote with its record and page, and opens the one clicked', () => {
    const onSelect = vi.fn()
    renderWithMantine(<ContradictionCard contradiction={contradiction} selectedKey={null} onSelect={onSelect} />)

    expect(screen.getByText('01_EMS.pdf · p. 1')).toBeInTheDocument()
    fireEvent.click(screen.getByText('02_ED.pdf · p. 2'))

    expect(onSelect).toHaveBeenCalledWith(contradiction.citations[1])
    expect(screen.queryByText('Unverified')).not.toBeInTheDocument()
  })

  it('labels a contradiction no quote supports as unverified', () => {
    const unverified: Contradiction = { summary: 'The collision dates differ.', citations: [], isVerified: false }
    renderWithMantine(<ContradictionCard contradiction={unverified} selectedKey={null} onSelect={vi.fn()} />)

    expect(screen.getByText('Unverified')).toBeInTheDocument()
    expect(screen.getByText(/No quote from the documents supports this contradiction/)).toBeInTheDocument()
  })
})
