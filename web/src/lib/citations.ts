import type { Citation } from '../api/types'

/** The record's file name: the last segment of the API server's file path. */
export function recordName(filePath: string): string {
  const parts = filePath.split(/[\\/]/)
  return parts[parts.length - 1] || filePath
}

/** A stable identity for a citation, for de-duplication and selection. */
export function citationKey(citation: Citation): string {
  return `${citation.filePath}\u0000${citation.startPosition}\u0000${citation.citationLength}`
}

/**
 * Numbers citations 1, 2, 3… in order of first appearance, giving repeated sources the same number.
 */
export function citationNumbering(citations: Citation[]): (citation: Citation) => number {
  const numbers = new Map<string, number>()
  for (const citation of citations) {
    const key = citationKey(citation)
    if (!numbers.has(key)) {
      numbers.set(key, numbers.size + 1)
    }
  }
  return (citation) => numbers.get(citationKey(citation)) ?? 0
}

/** "p. 3" or "pp. 3–4"; empty when the page is unknown. */
export function pageLabel(citation: Citation): string {
  if (citation.pageNumber === null) {
    return ''
  }
  return citation.endPageNumber !== null ? `pp. ${citation.pageNumber}–${citation.endPageNumber}` : `p. ${citation.pageNumber}`
}

export function describeCitation(citation: Citation): string {
  const page = pageLabel(citation)
  return page ? `${recordName(citation.filePath)}, ${page}` : recordName(citation.filePath)
}

/** A run of text, with the citations that support it (placed after it) and whether it is an uncited sentence. */
export interface AnnotatedSegment {
  text: string
  citations: Citation[]
  unsupported: boolean
}

/**
 * Splits generated text into segments so each cited claim is followed by its citations and uncited sentences can
 * be highlighted. Each citation's claim is the exact text it supports; claims are located in order, so repeated
 * wording attaches to the right occurrence. Claims or statements that cannot be found are left out.
 */
export function annotate(text: string, citations: Citation[], unsupportedStatements: string[] = []): AnnotatedSegment[] {
  // Group citations by claim, keeping first-appearance order and dropping duplicate citations.
  const claims: { claim: string; citations: Citation[] }[] = []
  for (const citation of citations) {
    const claim = citation.claim?.trim()
    if (!claim) {
      continue
    }
    let group = claims.find((existing) => existing.claim === claim)
    if (!group) {
      group = { claim, citations: [] }
      claims.push(group)
    }
    if (!group.citations.some((existing) => citationKey(existing) === citationKey(citation))) {
      group.citations.push(citation)
    }
  }

  const citedAt = new Map<number, Citation[]>()
  let cursor = 0
  for (const { claim, citations: claimCitations } of claims) {
    let index = text.indexOf(claim, cursor)
    if (index < 0) {
      index = text.indexOf(claim)
    }
    if (index < 0) {
      continue
    }
    const end = index + claim.length
    citedAt.set(end, [...(citedAt.get(end) ?? []), ...claimCitations])
    cursor = end
  }

  const unsupportedRanges: [number, number][] = []
  for (const statement of unsupportedStatements) {
    const index = text.indexOf(statement.trim())
    if (index >= 0) {
      unsupportedRanges.push([index, index + statement.trim().length])
    }
  }

  const boundaries = new Set<number>([0, text.length, ...citedAt.keys()])
  unsupportedRanges.forEach(([start, end]) => {
    boundaries.add(start)
    boundaries.add(end)
  })
  const points = [...boundaries].filter((point) => point >= 0 && point <= text.length).sort((a, b) => a - b)

  const segments: AnnotatedSegment[] = []
  for (let i = 0; i < points.length - 1; i++) {
    const [start, end] = [points[i], points[i + 1]]
    segments.push({
      text: text.slice(start, end),
      citations: citedAt.get(end) ?? [],
      unsupported: unsupportedRanges.some(([from, to]) => start >= from && end <= to),
    })
  }
  return segments
}

/** Where a quote sits in a sequence of text runs: from run `first` at `firstOffset` to run `last` before `lastEnd`. */
export interface RunRange {
  first: number
  firstOffset: number
  last: number
  lastEnd: number
}

/**
 * Finds a quote in a sequence of text runs (e.g. a PDF page's text items), ignoring whitespace and case. If the
 * whole quote is not found, its start and end (60 characters each) are matched separately, since text extraction
 * can differ slightly between tools.
 */
export function findQuoteRange(runs: string[], quote: string): RunRange | null {
  // One entry per non-whitespace character: which run it came from and where in that run.
  let page = ''
  const runOf: number[] = []
  const offsetOf: number[] = []
  runs.forEach((run, runIndex) => {
    let offset = 0
    for (const char of run) {
      if (!/\s/.test(char)) {
        // A few characters lowercase to two; keep those as they are so positions stay aligned.
        const lower = char.toLowerCase()
        page += lower.length === char.length ? lower : char
        for (let i = 0; i < char.length; i++) {
          runOf.push(runIndex)
          offsetOf.push(offset + i)
        }
      }
      offset += char.length
    }
  })

  const target = [...quote.replace(/\s+/g, '')].map((char) => (char.toLowerCase().length === char.length ? char.toLowerCase() : char)).join('')
  if (!target) {
    return null
  }

  const range = (start: number, endInclusive: number): RunRange => ({
    first: runOf[start],
    firstOffset: offsetOf[start],
    last: runOf[endInclusive],
    lastEnd: offsetOf[endInclusive] + 1,
  })

  const whole = page.indexOf(target)
  if (whole >= 0) {
    return range(whole, whole + target.length - 1)
  }

  const head = target.slice(0, 60)
  const tail = target.slice(-60)
  const start = page.indexOf(head)
  if (start < 0) {
    return null
  }
  const end = page.indexOf(tail, start)
  return range(start, end >= 0 ? end + tail.length - 1 : start + head.length - 1)
}

/**
 * The quote's range in a text file's contents: at its recorded position when the text matches there, otherwise
 * at its first occurrence.
 */
export function locateInText(text: string, citation: Citation): { start: number; end: number } | null {
  const { startPosition, citedText } = citation
  if (text.slice(startPosition, startPosition + citedText.length) === citedText) {
    return { start: startPosition, end: startPosition + citedText.length }
  }
  const index = text.indexOf(citedText)
  return index >= 0 ? { start: index, end: index + citedText.length } : null
}
