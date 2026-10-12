import { Button, Tooltip, VisuallyHidden } from '@mantine/core'
import type { CSSProperties } from 'react'
import { Link } from 'react-router'
import type { JobStatus } from '../api/types'
import { isJobActive, jobProgress } from '../lib/format'
import { adjacentSteps, type CaseStep, type StepId } from '../lib/steps'
import { IconChevronLeft, IconChevronRight } from './icons'
import classes from './StepProgress.module.css'

interface StepProgressProps {
  caseId: string
  steps: CaseStep[]
  current: StepId
  /** The case's job; while it runs, its step's segment fills with the job's progress. */
  job: JobStatus | null
}

/** The step each kind of job works on. */
const jobSteps = { Ingest: 'analyze', Draft: 'letter' } as const

/**
 * Where the case is in its steps: one segment per step, colored by whether it is done, with Back and Next to move
 * between them. Next stays disabled, and says why, until the following step can be opened.
 */
export function StepProgress({ caseId, steps, current, job }: StepProgressProps) {
  const index = steps.findIndex((step) => step.id === current)
  const working = job && isJobActive(job) ? { step: jobSteps[job.kind], percent: jobProgress(job).percent } : null
  // While the job runs it shows the bar's progress, or a shimmer when there is no count to show (such as while
  // checking for contradictions). Queued jobs show an empty track.
  const fill = working && (
    <span
      className={classes.fill}
      data-indeterminate={(working.percent === null && job?.state === 'Running') || undefined}
      style={{ '--progress': `${job?.state === 'Queued' ? 0 : (working.percent ?? 100)}%` } as CSSProperties}
    />
  )
  const { previous, next } = adjacentSteps(steps, current)
  const path = (step: CaseStep) => `/cases/${caseId}/${step.id}`

  return (
    <nav aria-label="Case progress" className={`mra-glass ${classes.bar}`}>
      <Button
        component={Link}
        to={previous ? path(previous) : '#'}
        variant="subtle"
        leftSection={<IconChevronLeft size={16} />}
        disabled={!previous}
        aria-label={previous ? `Back to ${previous.label}` : 'Back'}
      >
        Back
      </Button>

      <div className={classes.track}>
        <div className={classes.caption} style={{ '--index': index } as CSSProperties}>
          <span className={classes.count}>
            {index + 1}/{steps.length}
          </span>
          <span className={classes.reel} aria-hidden="true">
            <span className={classes.strip}>
              {steps.map((step) => (
                <span key={step.id}>{step.label}</span>
              ))}
            </span>
          </span>
          <VisuallyHidden>{steps[index].label}</VisuallyHidden>
        </div>
        <div className={classes.segments} style={{ '--count': steps.length, '--index': index } as CSSProperties}>
          <span className={classes.marker} data-working={working?.step === current || undefined} aria-hidden="true" />
          {steps.map((step) => {
            const shared = {
              className: classes.segment,
              'data-state': step.state,
              'data-working': working?.step === step.id || undefined,
              // The fill stays in its segment, drawn above the marker, so moving between steps never carries it along.
              children: working?.step === step.id ? fill : undefined,
            }
            return (
              <Tooltip key={step.id} label={step.label}>
                {step.state === 'locked' ? (
                  <span {...shared} role="img" aria-label={`${step.label}, locked`} />
                ) : (
                  <Link {...shared} to={path(step)} aria-label={step.label} aria-current={step.id === current ? 'step' : undefined} />
                )}
              </Tooltip>
            )
          })}
        </div>
      </div>

      <Tooltip label={next?.lockedReason} disabled={!next?.lockedReason}>
        {/* Disabled buttons don't fire hover events, so the tooltip sits on a wrapper. */}
        <span>
          <Button
            component={Link}
            to={next && next.state !== 'locked' ? path(next) : '#'}
            variant={next?.state === 'locked' ? 'subtle' : 'filled'}
            rightSection={<IconChevronRight size={16} />}
            disabled={!next || next.state === 'locked'}
            aria-label={next ? `Next: ${next.label}` : 'Next'}
          >
            Next
          </Button>
        </span>
      </Tooltip>
    </nav>
  )
}
