import { describe, expect, it } from 'vitest'
import type { Citation } from '../api/types'
import { annotate, citationNumbering, describeCitation, findQuoteRange, locateInText, pageLabel, recordName } from './citations'

const citation = (overrides: Partial<Citation>): Citation => ({
  filePath: '/cases/abc/records/02_ED_Record.pdf',
  citedText: 'Denies loss of consciousness.',
  startPosition: 100,
  citationLength: 29,
  claim: null,
  pageNumber: 1,
  endPageNumber: null,
  ...overrides,
})

describe('labels', () => {
  it('uses the file name and page', () => {
    expect(recordName('/cases/abc/records/02_ED_Record.pdf')).toBe('02_ED_Record.pdf')
    expect(recordName('C:\\cases\\abc\\note.txt')).toBe('note.txt')
    expect(pageLabel(citation({ pageNumber: 2 }))).toBe('p. 2')
    expect(pageLabel(citation({ pageNumber: 3, endPageNumber: 4 }))).toBe('pp. 3–4')
    expect(pageLabel(citation({ pageNumber: null }))).toBe('')
    expect(describeCitation(citation({ pageNumber: 2 }))).toBe('02_ED_Record.pdf, p. 2')
  })

  it('numbers distinct sources in order, reusing numbers for repeats', () => {
    const a = citation({ startPosition: 1 })
    const b = citation({ startPosition: 2 })
    const numberOf = citationNumbering([a, b, { ...a }])
    expect([numberOf(a), numberOf(b), numberOf({ ...a })]).toEqual([1, 2, 1])
  })
})

describe('annotate', () => {
  const text = 'She was rear-ended. She denied loss of consciousness. The date is disputed.'

  it('places citations after the claims they support, in order', () => {
    const segments = annotate(text, [
      citation({ claim: 'She was rear-ended.', startPosition: 1 }),
      citation({ claim: 'She denied loss of consciousness.', startPosition: 2 }),
      citation({ claim: 'She denied loss of consciousness.', startPosition: 3 }),
    ])

    expect(segments.map((segment) => [segment.text, segment.citations.map((c) => c.startPosition)])).toEqual([
      ['She was rear-ended.', [1]],
      [' She denied loss of consciousness.', [2, 3]],
      [' The date is disputed.', []],
    ])
    expect(segments.map((segment) => segment.text).join('')).toBe(text)
  })

  it('attaches repeated wording to the next occurrence', () => {
    const repeated = 'Pain 7/10. Later, pain 7/10.'
    const segments = annotate(repeated, [
      citation({ claim: 'Pain 7/10.', startPosition: 1 }),
      citation({ claim: 'pain 7/10.', startPosition: 2 }),
    ])
    expect(segments.filter((segment) => segment.citations.length > 0).map((segment) => segment.text)).toEqual(['Pain 7/10.', ' Later, pain 7/10.'])
  })

  it('marks uncited sentences and ignores claims that are not in the text', () => {
    const segments = annotate(text, [citation({ claim: 'Not in the text.' })], ['The date is disputed.'])

    expect(segments.find((segment) => segment.unsupported)?.text).toBe('The date is disputed.')
    expect(segments.every((segment) => segment.citations.length === 0)).toBe(true)
  })
})

describe('findQuoteRange', () => {
  const runs = ['reports her head was thrown back and then forward. She', 'denies', 'loss', ' ', 'of', ' ', 'consciousness.', 'Next line.']

  it('finds the quote across runs, ignoring whitespace and case, with offsets in the first and last run', () => {
    expect(findQuoteRange(runs, 'She denies loss of\nCONSCIOUSNESS.')).toEqual({ first: 0, firstOffset: 51, last: 6, lastEnd: 14 })
  })

  it('matches the start and end separately when the middle differs', () => {
    const page = [
      'Patient reports constant neck pain rated seven out of ten, worse in the morning hours.',
      'Notes difficulty turning the head while driving to work, and she cannot lift her young son at all.',
    ]
    // Extraction tools can differ slightly; here one word in the middle of the quote differs from the page.
    const quote = `${page[0]} ${page[1].replace('difficulty', 'trouble')}`

    expect(findQuoteRange(page, quote)).toEqual({ first: 0, firstOffset: 0, last: 1, lastEnd: page[1].length })
  })

  it('returns null when the quote is not on the page', () => {
    expect(findQuoteRange(runs, 'Patient lost consciousness.')).toBeNull()
    expect(findQuoteRange(runs, '   ')).toBeNull()
  })
})

describe('locateInText', () => {
  const text = 'HISTORY:\nDenies loss of consciousness.\n'

  it('uses the recorded position when the text matches there', () => {
    expect(locateInText(text, citation({ startPosition: 9 }))).toEqual({ start: 9, end: 38 })
  })

  it('falls back to searching, or null', () => {
    expect(locateInText(text, citation({ startPosition: 0 }))).toEqual({ start: 9, end: 38 })
    expect(locateInText(text, citation({ citedText: 'Not here.' }))).toBeNull()
  })
})
