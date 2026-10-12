import type { CaseDetail } from '../api/types'

export const STEP_IDS = ['matter', 'records', 'analyze', 'review', 'letter'] as const
export type StepId = (typeof STEP_IDS)[number]
export type StepState = 'done' | 'todo' | 'locked'

export interface CaseStep {
  id: StepId
  label: string
  /** What the step is for, shown as its tooltip. */
  hint: string
  state: StepState
  /** Why a locked step can't be opened yet. */
  lockedReason: string | null
}

/**
 * The steps every case moves through, in order, with how far this case has got. A step is locked until the work
 * it depends on exists, and done once its result is current.
 */
export function caseSteps(detail: CaseDetail): CaseStep[] {
  const hasRecords = detail.records.length > 0
  const analyzed = detail.analysisUpdatedAt !== null
  const drafted = detail.draftUpdatedAt !== null
  const step = (id: StepId, label: string, hint: string, state: StepState, lockedReason: string): CaseStep => ({
    id,
    label,
    hint,
    state,
    lockedReason: state === 'locked' ? lockedReason : null,
  })

  return [
    step('matter', 'Matter', 'The case name, date of loss, jurisdiction and county. The date of loss fills in the letter.', detail.dateOfLoss ? 'done' : 'todo', ''),
    step('records', 'Documents', 'Medical records, bills and letters for this matter.', hasRecords ? 'done' : 'todo', ''),
    step(
      'analyze',
      'Analyze',
      'Reads each document, pulls out quotes with page numbers, and checks the documents against each other.',
      !hasRecords ? 'locked' : analyzed && !detail.analysisOutOfDate ? 'done' : 'todo',
      'Add documents first.',
    ),
    step(
      'review',
      'Review',
      'Check the case summary and any contradictions before drafting.',
      // A letter drafted from the current analysis means it was reviewed, including for cases older than the flag.
      !analyzed ? 'locked' : detail.analysisReviewed || (drafted && !detail.draftOutOfDate) ? 'done' : 'todo',
      'Analyze the documents first.',
    ),
    step(
      'letter',
      'Letter',
      'Draft the demand letter, work through the review items, and export it.',
      !analyzed ? 'locked' : drafted && !detail.draftOutOfDate ? 'done' : 'todo',
      'Analyze the documents first.',
    ),
  ]
}

/** The step to open a case on: the first one still to do, or the letter once everything is done. */
export function firstOpenStep(steps: CaseStep[]): StepId {
  return steps.find((step) => step.state === 'todo')?.id ?? 'letter'
}

/** The steps before and after the given one, whether or not they can be opened yet. */
export function adjacentSteps(steps: CaseStep[], current: StepId | null): { previous: CaseStep | null; next: CaseStep | null } {
  const index = steps.findIndex((step) => step.id === current)
  if (index < 0) {
    return { previous: null, next: null }
  }

  return { previous: steps[index - 1] ?? null, next: steps[index + 1] ?? null }
}

/** The step a case page path is showing, if any. */
export function stepFromPath(pathname: string): StepId | null {
  const last = pathname.split('/').filter(Boolean).at(-1)
  return STEP_IDS.find((id) => id === last) ?? null
}
