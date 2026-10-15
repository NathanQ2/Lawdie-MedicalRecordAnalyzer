import type { ChronologyEntry, Citation, Contradiction } from '../api/types'

export type FlagKind = 'conflict' | 'date' | 'provider' | 'scanned' | 'before'

/** Something about a chronology entry the attorney should check. */
export interface ChronologyFlag {
  kind: FlagKind
  label: string
  hint: string
}

/** Whether two quotes cover overlapping text of the same document. */
export function quotesOverlap(a: Citation, b: Citation): boolean {
  return (
    a.filePath === b.filePath &&
    a.startPosition < b.startPosition + Math.max(b.citationLength, 1) &&
    b.startPosition < a.startPosition + Math.max(a.citationLength, 1)
  )
}

/** The contradictions that quote any of the text this entry cites. */
export function conflictsFor(entry: ChronologyEntry, contradictions: Contradiction[]): Contradiction[] {
  return contradictions.filter((contradiction) =>
    contradiction.citations.some((quote) => entry.citations.some((citation) => quotesOverlap(quote, citation))),
  )
}

/**
 * What to check about an entry: a contradiction that involves its quotes, a date or provider name not found in the
 * cited document, a quote read by OCR, or a date before the incident (often an earlier condition). Dates are
 * YYYY-MM-DD, so they compare as text.
 */
export function chronologyFlags(entry: ChronologyEntry, contradictions: Contradiction[], dateOfLoss: string | null): ChronologyFlag[] {
  const flags: ChronologyFlag[] = []
  const conflicts = conflictsFor(entry, contradictions)
  if (conflicts.length > 0) {
    const more = conflicts.length > 1 ? ` ${conflicts.length - 1} more on the Issues page.` : ''
    flags.push({ kind: 'conflict', label: 'Conflict', hint: `${conflicts[0].summary}${more}` })
  }
  if (!entry.dateConfirmed) {
    flags.push({ kind: 'date', label: 'Check date', hint: "This date wasn't found in the cited document. Check it against the original." })
  }
  if (!entry.providerConfirmed) {
    flags.push({ kind: 'provider', label: 'Check provider', hint: "This provider name wasn't found in the cited document. Check it against the original." })
  }
  if (entry.citations.some((citation) => citation.recognized)) {
    flags.push({ kind: 'scanned', label: 'Scanned', hint: 'Read from a scan or photo, so it may contain misread characters. Check it against the original page.' })
  }
  if (dateOfLoss && entry.date < dateOfLoss) {
    flags.push({ kind: 'before', label: 'Pre-incident', hint: 'This visit is dated before the date of loss. It may show an earlier condition.' })
  }
  return flags
}

/** Entries whose date, provider, visit type or finding contains the search text, ignoring letter case. */
export function searchChronology(entries: ChronologyEntry[], query: string): ChronologyEntry[] {
  const text = query.trim().toLowerCase()
  if (!text) {
    return entries
  }
  return entries.filter((entry) => [entry.date, entry.provider, entry.visitType, entry.finding].some((field) => field.toLowerCase().includes(text)))
}

export interface ProviderGroup {
  provider: string
  entries: ChronologyEntry[]
}

/**
 * Entries grouped by provider, ignoring letter case and spacing in the name. Groups keep the order of their first
 * visit, and each group's entries stay in date order.
 */
export function groupByProvider(entries: ChronologyEntry[]): ProviderGroup[] {
  const groups = new Map<string, ProviderGroup>()
  for (const entry of entries) {
    const key = entry.provider.trim().replace(/\s+/g, ' ').toLowerCase()
    const group = groups.get(key)
    if (group) {
      group.entries.push(entry)
    } else {
      groups.set(key, { provider: entry.provider.trim(), entries: [entry] })
    }
  }
  return [...groups.values()]
}
