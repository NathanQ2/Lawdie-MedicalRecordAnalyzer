import { describe, expect, it } from 'vitest'
import type { ChronologyEntry, Citation, Contradiction } from '../api/types'
import { chronologyFlags, groupByProvider, searchChronology } from './chronology'

function quote(filePath: string, startPosition: number, citedText: string, recognized = false): Citation {
  return { filePath, citedText, startPosition, citationLength: citedText.length, claim: null, pageNumber: 1, endPageNumber: null, recognized }
}

function entry(overrides: Partial<ChronologyEntry> = {}): ChronologyEntry {
  return {
    date: '2026-03-10',
    provider: 'Riverbend Pain Specialists',
    visitType: 'Pain consult',
    finding: 'Neck pain 6/10',
    citations: [quote('/r/12_pain.txt', 100, 'vehicle collision on 01/08/2026.')],
    dateConfirmed: true,
    providerConfirmed: true,
    ...overrides,
  }
}

const collisionDate: Contradiction = {
  summary: 'The collision date is 01/08/2026 in the pain consult but 01/09/2026 elsewhere.',
  citations: [quote('/r/12_pain.txt', 110, 'on 01/08/2026.'), quote('/r/04_pcp.txt', 5, 'rear-ended on 01/09/2026')],
  isVerified: true,
}

describe('chronologyFlags', () => {
  it('marks nothing on a confirmed visit after the incident', () => {
    expect(chronologyFlags(entry(), [], '2026-01-09')).toEqual([])
  })

  it('marks a visit whose quotes are part of a contradiction', () => {
    const [flag] = chronologyFlags(entry(), [collisionDate], '2026-01-09')

    expect(flag.kind).toBe('conflict')
    expect(flag.hint).toBe(collisionDate.summary)
  })

  it('marks dates and providers not found in the document, scanned quotes, and visits before the incident', () => {
    const flags = chronologyFlags(
      entry({ date: '2023-05-02', dateConfirmed: false, providerConfirmed: false, citations: [quote('/r/journal.jpeg', 0, 'Neck 7/10', true)] }),
      [collisionDate],
      '2026-01-09',
    )

    expect(flags.map((flag) => flag.kind)).toEqual(['date', 'provider', 'scanned', 'before'])
  })
})

describe('searchChronology', () => {
  it('matches the date, provider, visit type and finding, ignoring case', () => {
    const entries = [entry(), entry({ provider: 'Summit Imaging Center', visitType: 'MRI cervical', finding: 'Left C6 root contact', date: '2026-02-10' })]

    expect(searchChronology(entries, 'mri')).toHaveLength(1)
    expect(searchChronology(entries, 'NECK PAIN')).toHaveLength(1)
    expect(searchChronology(entries, '2026-02')).toHaveLength(1)
    expect(searchChronology(entries, '  ')).toHaveLength(2)
  })
})

describe('groupByProvider', () => {
  it('groups by provider name in order of first visit', () => {
    const entries = [
      entry({ date: '2026-01-13', provider: 'Northgate Family Medicine' }),
      entry({ date: '2026-03-10', provider: 'Riverbend Pain Specialists' }),
      entry({ date: '2026-04-07', provider: 'riverbend  pain specialists' }),
      entry({ date: '2026-06-16', provider: 'Northgate Family Medicine' }),
    ]

    const groups = groupByProvider(entries)

    expect(groups.map((group) => [group.provider, group.entries.length])).toEqual([
      ['Northgate Family Medicine', 2],
      ['Riverbend Pain Specialists', 2],
    ])
  })
})
