import type { CaseDetail, ReviewItem, ReviewItemKind } from '../api/types'
import { isJobActive } from './format'

/** The case workspace's pages, in menu order. The documents page keeps its older path, records. */
export const SECTION_IDS = ['overview', 'records', 'chronology', 'issues', 'letter'] as const
export type SectionId = (typeof SECTION_IDS)[number]

export const sectionLabels: Record<SectionId, string> = {
  overview: 'Overview',
  records: 'Documents',
  chronology: 'Chronology',
  issues: 'Issues',
  letter: 'Letter',
}

/** The workspace page a case path is showing, if any. */
export function sectionFromPath(pathname: string): SectionId | null {
  const last = pathname.split('/').filter(Boolean).at(-1)
  return SECTION_IDS.find((id) => id === last) ?? null
}

// Issues are checked before drafting; letter items are fixed in the letter itself.
const issueKinds: ReviewItemKind[] = ['IntakeMismatch', 'Contradiction']
const letterKinds: ReviewItemKind[] = ['UnsupportedStatement', 'Placeholder', 'RecognizedQuote']

export function isIssue(item: ReviewItem): boolean {
  return issueKinds.includes(item.kind)
}

/** Open contradictions and intake details the documents disagree with. */
export function openIssueCount(items: ReviewItem[] | undefined): number {
  return items?.filter((item) => isIssue(item) && item.status === 'Open').length ?? 0
}

/** Open uncited sentences, placeholders and scanned quotes in the letter. */
export function openLetterItemCount(items: ReviewItem[] | undefined): number {
  return items?.filter((item) => letterKinds.includes(item.kind) && item.status === 'Open').length ?? 0
}

export interface NextAction {
  title: string
  detail: string
  section: SectionId
  /** The button that takes the attorney there. */
  label: string
}

const plural = (count: number, one: string, many: string) => `${count} ${count === 1 ? one : many}`

/**
 * The one thing to do next on a case, worked out from how far it has got: add and analyze documents, check the
 * issues, draft the letter, clear its review items, then export.
 */
export function nextAction(detail: CaseDetail, review: ReviewItem[] | undefined): NextAction {
  const job = detail.job
  if (job && isJobActive(job)) {
    return job.kind === 'Ingest'
      ? { title: 'The documents are being analyzed', detail: 'You can leave this page. The results appear here when the analysis finishes.', section: 'records', label: 'See progress' }
      : { title: 'The letter is being drafted', detail: 'You can leave this page. The letter appears when drafting finishes.', section: 'letter', label: 'See progress' }
  }

  if (detail.records.length === 0) {
    return { title: 'Add the documents', detail: 'Upload the medical records, bills and letters for this case.', section: 'records', label: 'Add documents' }
  }

  if (!detail.analysisUpdatedAt) {
    return { title: 'Analyze the documents', detail: 'The analysis finds the quote and page behind each fact, and lists the visits by date.', section: 'records', label: 'Go to documents' }
  }

  if (detail.analysisOutOfDate) {
    return { title: 'Analyze the documents again', detail: 'You changed the documents after the last analysis.', section: 'records', label: 'Go to documents' }
  }

  const issues = openIssueCount(review)
  if (issues > 0) {
    return {
      title: `Check ${plural(issues, 'issue', 'issues')}`,
      detail: 'Decide which version is right where the documents disagree, before you draft the letter.',
      section: 'issues',
      label: 'Review issues',
    }
  }

  if (!detail.draftUpdatedAt) {
    return { title: 'Draft the demand letter', detail: 'Each fact in the letter cites a document.', section: 'letter', label: 'Go to the letter' }
  }

  if (detail.draftOutOfDate) {
    return { title: 'Draft the letter again', detail: 'The analysis changed after the letter was drafted.', section: 'letter', label: 'Go to the letter' }
  }

  const letterItems = openLetterItemCount(review)
  if (letterItems > 0) {
    return {
      title: `Clear ${plural(letterItems, 'letter item', 'letter items')}`,
      detail: 'Fix or confirm the uncited sentences and fill in the placeholders before you send the letter.',
      section: 'letter',
      label: 'Go to the letter',
    }
  }

  return { title: 'Export the letter', detail: 'Every review item is handled.', section: 'letter', label: 'Go to the letter' }
}
