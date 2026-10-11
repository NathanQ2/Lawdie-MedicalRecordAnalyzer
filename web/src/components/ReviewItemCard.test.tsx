import { fireEvent, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import type { ReviewItem } from '../api/types'
import { renderWithMantine } from '../test/render'
import { ReviewItemCard } from './ReviewItemCard'

const contradiction: ReviewItem = {
  id: 'contradiction-1',
  kind: 'Contradiction',
  text: '12_Pain_Management_Consult.txt gives the collision date as 01/08/2026.',
  citations: [
    { filePath: '/r/12_Pain.txt', citedText: 'collision on 01/08/2026', startPosition: 5, citationLength: 23, claim: null, pageNumber: 1, endPageNumber: null },
    { filePath: '/r/04_PCP.txt', citedText: 'collision on 01/09/2026', startPosition: 90, citationLength: 23, claim: null, pageNumber: 1, endPageNumber: null },
  ],
  status: 'Open',
  note: null,
  draftRanges: [
    { start: 10, length: 20 },
    { start: 80, length: 12 },
  ],
}

describe('ReviewItemCard', () => {
  it('resolves an open item with the typed note', () => {
    const onUpdate = vi.fn()
    renderWithMantine(<ReviewItemCard item={contradiction} onUpdate={onUpdate} onSelectCitation={() => {}} />)

    fireEvent.change(screen.getByLabelText('Review note'), { target: { value: '  Confirmed 01/09 with client.  ' } })
    fireEvent.click(screen.getByRole('button', { name: 'Resolve' }))

    expect(onUpdate).toHaveBeenCalledWith('Resolved', 'Confirmed 01/09 with client.')
  })

  it('opens a source from its quote', () => {
    const onSelectCitation = vi.fn()
    renderWithMantine(<ReviewItemCard item={contradiction} onUpdate={() => {}} onSelectCitation={onSelectCitation} />)

    fireEvent.click(screen.getByText(/12_Pain.txt · p. 1/))

    expect(onSelectCitation).toHaveBeenCalledWith(contradiction.citations[0])
    expect(screen.queryByText('Unverified')).not.toBeInTheDocument()
  })

  it('labels a contradiction without two different quotes as unverified', () => {
    const [quote] = contradiction.citations
    renderWithMantine(<ReviewItemCard item={{ ...contradiction, citations: [quote, { ...quote }] }} onUpdate={() => {}} onSelectCitation={() => {}} />)

    expect(screen.getByText('Unverified')).toBeInTheDocument()
  })

  it('links to the sentences it affects in the letter', () => {
    const onShowInDraft = vi.fn()
    renderWithMantine(<ReviewItemCard item={contradiction} onUpdate={() => {}} onSelectCitation={() => {}} onShowInDraft={onShowInDraft} />)

    fireEvent.click(screen.getByRole('button', { name: 'Show in letter (2 places)' }))

    expect(onShowInDraft).toHaveBeenCalledWith(contradiction)
  })

  it('says when the letter does not cite a contradiction', () => {
    renderWithMantine(
      <ReviewItemCard item={{ ...contradiction, draftRanges: [] }} onUpdate={() => {}} onSelectCitation={() => {}} onShowInDraft={() => {}} />,
    )

    expect(screen.getByText(/No cited sentence in the letter relies on these quotes/)).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /Show in letter/ })).not.toBeInTheDocument()
  })

  it('lets a handled item be reopened or its note saved', () => {
    const onUpdate = vi.fn()
    renderWithMantine(<ReviewItemCard item={{ ...contradiction, status: 'Dismissed', note: 'Typo.' }} onUpdate={onUpdate} onSelectCitation={() => {}} />)

    expect(screen.queryByRole('button', { name: 'Resolve' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Save note' })).not.toBeInTheDocument()

    fireEvent.change(screen.getByLabelText('Review note'), { target: { value: 'Typo in the record.' } })
    fireEvent.click(screen.getByRole('button', { name: 'Save note' }))
    fireEvent.click(screen.getByRole('button', { name: 'Reopen' }))

    expect(onUpdate).toHaveBeenNthCalledWith(1, 'Dismissed', 'Typo in the record.')
    expect(onUpdate).toHaveBeenNthCalledWith(2, 'Open', 'Typo in the record.')
  })
})
