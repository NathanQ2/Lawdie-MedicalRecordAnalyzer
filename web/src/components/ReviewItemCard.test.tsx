import { fireEvent, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import type { ReviewItem } from '../api/types'
import { renderWithMantine } from '../test/render'
import { ReviewItemCard } from './ReviewItemCard'

const contradiction: ReviewItem = {
  id: 'contradiction-1',
  kind: 'Contradiction',
  text: '12_Pain_Management_Consult.txt gives the collision date as 01/08/2026.',
  citations: [{ filePath: '/r/12_Pain.txt', citedText: 'collision on 01/08/2026', startPosition: 5, citationLength: 23, claim: null, pageNumber: 1, endPageNumber: null }],
  status: 'Open',
  note: null,
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

  it('labels a contradiction without quotes as unverified', () => {
    renderWithMantine(<ReviewItemCard item={{ ...contradiction, citations: [] }} onUpdate={() => {}} onSelectCitation={() => {}} />)

    expect(screen.getByText('Unverified')).toBeInTheDocument()
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
