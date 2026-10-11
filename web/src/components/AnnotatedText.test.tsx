import { fireEvent, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import type { Citation } from '../api/types'
import { citationNumbering } from '../lib/citations'
import { renderWithMantine } from '../test/render'
import { AnnotatedText } from './AnnotatedText'

const denial: Citation = {
  filePath: '/cases/abc/records/02_ED_Record.pdf',
  citedText: 'Denies loss of consciousness.',
  startPosition: 657,
  citationLength: 29,
  claim: 'The ED record states that she denies loss of consciousness.',
  pageNumber: 2,
  endPageNumber: null,
}

describe('AnnotatedText', () => {
  const text = 'She was rear-ended on 01/09/2026. The ED record states that she denies loss of consciousness.'

  it('renders numbered source markers that select their citation', () => {
    const onSelect = vi.fn()
    renderWithMantine(
      <AnnotatedText text={text} citations={[denial]} numberOf={citationNumbering([denial])} selectedKey={null} onSelect={onSelect} />,
    )

    const marker = screen.getByRole('button', { name: 'Source 1: 02_ED_Record.pdf, p. 2' })
    fireEvent.click(marker)

    expect(onSelect).toHaveBeenCalledWith(denial)
    expect(screen.getByText(/She was rear-ended/)).toBeInTheDocument()
  })

  it('highlights uncited sentences', () => {
    renderWithMantine(
      <AnnotatedText
        text={text}
        citations={[denial]}
        unsupportedStatements={['She was rear-ended on 01/09/2026.']}
        numberOf={citationNumbering([denial])}
        selectedKey={null}
        onSelect={() => {}}
      />,
    )

    expect(screen.getByText('She was rear-ended on 01/09/2026.').tagName).toBe('MARK')
  })

  it('highlights placeholders only when asked', () => {
    const withPlaceholder = 'The demand is [DEMAND AMOUNT] for claim [CLAIM NUMBER].'
    const { rerender } = renderWithMantine(
      <AnnotatedText text={withPlaceholder} citations={[]} numberOf={() => 0} selectedKey={null} onSelect={() => {}} letter />,
    )

    expect(screen.getByText('[DEMAND AMOUNT]').tagName).toBe('MARK')
    expect(screen.getByText('[CLAIM NUMBER]').tagName).toBe('MARK')

    rerender(<AnnotatedText text={withPlaceholder} citations={[]} numberOf={() => 0} selectedKey={null} onSelect={() => {}} />)
    expect(screen.queryByText('[DEMAND AMOUNT]')).not.toBeInTheDocument()
  })

  it('shows "## " lines as section headings in a letter', () => {
    renderWithMantine(
      <AnnotatedText text={'Dear [ADJUSTER NAME]:\n\n## Medical Expenses\n\nTotal charges.'} citations={[]} numberOf={() => 0} selectedKey={null} onSelect={() => {}} letter />,
    )

    expect(screen.getByText('Medical Expenses').tagName).toBe('SPAN')
    expect(screen.queryByText(/## /)).not.toBeInTheDocument()
  })
})
