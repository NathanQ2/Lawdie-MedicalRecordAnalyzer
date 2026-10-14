import { describe, expect, it } from 'vitest'
import type { Citation, DraftSegment } from '../api/types'
import { citationsIn, isMultiline, isSentenceEnd, paragraphs, placeholderLabel, rangesIn, textForEditing } from './letter'

describe('placeholderLabel', () => {
  it('turns a placeholder into a sentence-case label', () => {
    expect(placeholderLabel('[CLAIM NUMBER]')).toBe('Claim number')
    expect(placeholderLabel('[LAW FIRM NAME]')).toBe('Law firm name')
  })

  it('names the letter date plainly', () => {
    expect(placeholderLabel('[DATE]')).toBe('Date of the letter')
  })
})

describe('isMultiline', () => {
  it('gives addresses and lists several lines', () => {
    expect(isMultiline('[INSURER ADDRESS]')).toBe(true)
    expect(isMultiline('[LIST OF ENCLOSURES]')).toBe(true)
    expect(isMultiline('[CLAIM NUMBER]')).toBe(false)
  })
})

const er: Citation = {
  filePath: '/records/er.txt',
  citedText: 'Denies loss of consciousness.',
  startPosition: 657,
  citationLength: 29,
  claim: 'denied loss of consciousness',
  pageNumber: 2,
  endPageNumber: null,
}
const pt: Citation = { ...er, filePath: '/records/pt.txt', citedText: 'Briefly lost consciousness.', startPosition: 319, citationLength: 27 }

const segments: DraftSegment[] = [
  { text: 'Dear [ADJUSTER NAME]:\n\nThe ER record states she ', citations: [] },
  { text: 'denied loss of consciousness', citations: [er, er] },
  { text: ', but PT notes say she ', citations: [] },
  { text: 'briefly lost consciousness ', citations: [pt] },
  { text: 'at the scene.\n\n\nSincerely,', citations: [] },
]
const letter = segments.map((segment) => segment.text).join('')
const numberOf = (citation: Citation) => (citation === er ? 1 : 2)

describe('paragraphs', () => {
  it('splits the letter at blank lines', () => {
    expect(paragraphs(letter).map(({ start, end }) => letter.slice(start, end))).toEqual([
      'Dear [ADJUSTER NAME]:',
      'The ER record states she denied loss of consciousness, but PT notes say she briefly lost consciousness at the scene.',
      'Sincerely,',
    ])
  })
})

describe('textForEditing', () => {
  it('puts each source number right after the statement it supports', () => {
    const [, body] = paragraphs(letter)

    expect(textForEditing(segments, body, numberOf)).toBe(
      'The ER record states she denied loss of consciousness[1], but PT notes say she briefly lost consciousness[2] at the scene.',
    )
    expect(citationsIn(segments, body)).toEqual([er, er, pt])
  })
})

describe('textForEditing with a source that covers several sentences', () => {
  it('numbers each sentence the source covers, but not abbreviations', () => {
    const cited: DraftSegment[] = [
      { text: 'Dispatch was for a collision. Dr. Osei saw her at 9 a.m. She was alert!\nShe went home', citations: [er] },
      { text: '.', citations: [] },
    ]

    expect(textForEditing(cited, { start: 0, end: 200 }, numberOf)).toBe(
      'Dispatch was for a collision.[1] Dr. Osei saw her at 9 a.m.[1] She was alert![1]\nShe went home[1].',
    )
  })
})

describe('isSentenceEnd', () => {
  it('ends sentences at punctuation followed by a space, except after abbreviations', () => {
    expect(isSentenceEnd('Seen today. Next', 10)).toBe(true)
    expect(isSentenceEnd('Seen by Dr. Osei', 10)).toBe(false)
    expect(isSentenceEnd('Value 3.5 units', 7)).toBe(false)
  })
})

describe('rangesIn', () => {
  it('keeps the parts of ranges inside a paragraph, relative to its start', () => {
    expect(rangesIn([[0, 4], [20, 30], [200, 210]], { start: 23, end: 140 })).toEqual([[0, 7]])
  })
})
