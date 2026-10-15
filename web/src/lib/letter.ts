import type { Citation, DraftSegment } from '../api/types'

// Placeholders whose names read poorly as labels on their own.
const labels: Record<string, string> = {
  '[DATE]': 'Date of the letter',
}

/** A placeholder's name as a field label: "[CLAIM NUMBER]" becomes "Claim number". */
export function placeholderLabel(placeholder: string): string {
  const known = labels[placeholder]
  if (known) {
    return known
  }
  const words = placeholder.replace(/^\[|\]$/g, '').toLowerCase()
  return words.charAt(0).toUpperCase() + words.slice(1)
}

/** Placeholders the firm profile fills for every letter. */
export const firmPlaceholders = ['[LAW FIRM NAME]', '[FIRM ADDRESS]', '[FIRM PHONE]', '[FIRM EMAIL]', '[ATTORNEY NAME]']

/** Whether a placeholder usually takes several lines, such as an address or a list of enclosures. */
export function isMultiline(placeholder: string): boolean {
  return /ADDRESS|LIST/.test(placeholder)
}

/** A paragraph of the letter, as [start, end) offsets into its text. */
export interface Paragraph {
  start: number
  end: number
}

/** The letter's paragraphs: the runs of text between blank lines. */
export function paragraphs(text: string): Paragraph[] {
  const result: Paragraph[] = []
  let start = 0
  for (const match of text.matchAll(/\n{2,}/g)) {
    if (match.index > start) {
      result.push({ start, end: match.index })
    }
    start = match.index + match[0].length
  }
  if (start < text.length) {
    result.push({ start, end: text.length })
  }
  return result
}

/** The citations of the segments that overlap a paragraph. */
export function citationsIn(segments: DraftSegment[], paragraph: Paragraph): Citation[] {
  const result: Citation[] = []
  let position = 0
  for (const segment of segments) {
    const end = position + segment.text.length
    if (position < paragraph.end && end > paragraph.start) {
      result.push(...segment.citations)
    }
    position = end
  }
  return result
}

// A period after one of these words does not end a sentence. Matches the server's TextChunker.
const abbreviations = new Set(['dr', 'mr', 'mrs', 'ms', 'st', 'vs', 'no', 'approx', 'e.g', 'i.e', 'etc'])

/**
 * Whether the character at index ends a sentence: '.', '!' or '?' followed by whitespace or the end of the text,
 * and not the period of a known abbreviation such as "Dr.". The same rule as the server's TextChunker.IsSentenceEnd.
 */
export function isSentenceEnd(text: string, index: number): boolean {
  const char = text[index]
  if (!'.!?'.includes(char) || (index + 1 < text.length && !/\s/.test(text[index + 1]))) {
    return false
  }
  if (char !== '.') {
    return true
  }
  let wordStart = index
  while (wordStart > 0 && !/\s/.test(text[wordStart - 1])) {
    wordStart--
  }
  return !abbreviations.has(text.slice(wordStart, index).toLowerCase())
}

/**
 * A cited statement with its source numbers after it. The server ties a number to the sentence just before it, so
 * a statement of several sentences gets the numbers after each one; each sentence was cited, and stays cited.
 */
function withMarkers(claim: string, markers: string): string {
  let text = ''
  let pending = false
  for (let index = 0; index < claim.length; index++) {
    const char = claim[index]
    if (char === '\n' && pending) {
      text += markers
      pending = false
    }
    text += char
    if (!/\s/.test(char)) {
      pending = true
    }
    if (pending && index + 1 < claim.length && isSentenceEnd(claim, index)) {
      text += markers
      pending = false
    }
  }
  return pending ? text + markers : text
}

/**
 * A paragraph as the attorney edits it: its text with the number of each source in brackets right after the
 * statement it supports, as the letter shows them, such as "She denied any loss of consciousness.[3]".
 */
export function textForEditing(segments: DraftSegment[], paragraph: Paragraph, numberOf: (citation: Citation) => number): string {
  let text = ''
  let position = 0
  for (const segment of segments) {
    const from = Math.max(paragraph.start, position)
    const to = Math.min(paragraph.end, position + segment.text.length)
    if (from < to) {
      const piece = segment.text.slice(from - position, to - position)
      const markers = [...new Set(segment.citations.map(numberOf))].map((number) => `[${number}]`).join('')
      const claim = piece.trimEnd()
      text += markers ? withMarkers(claim, markers) + piece.slice(claim.length) : piece
    }
    position += segment.text.length
  }
  return text
}

/** Ranges of the whole letter that fall in a paragraph, relative to the paragraph's start. */
export function rangesIn(ranges: [number, number][], paragraph: Paragraph): [number, number][] {
  return ranges
    .filter(([start, end]) => start < paragraph.end && end > paragraph.start)
    .map(([start, end]): [number, number] => [Math.max(start, paragraph.start) - paragraph.start, Math.min(end, paragraph.end) - paragraph.start])
}
