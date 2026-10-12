import { Alert, Anchor, Badge, Center, Group, Loader, Stack, Text, Title } from '@mantine/core'
import { IconBack } from '../components/icons'
import { StepProgress } from '../components/StepProgress'
import { notifications } from '@mantine/notifications'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useEffect, useRef, useState, type CSSProperties } from 'react'
import { Link, Navigate, Outlet, useLocation, useParams } from 'react-router'
import { api } from '../api/client'
import type { CaseDetail } from '../api/types'
import { caseKeys, useCase } from '../hooks/useCase'
import { formatDateOnly, isJobActive, jobKindLabels, jobStateColors } from '../lib/format'
import { caseSteps, firstOpenStep, STEP_IDS, stepFromPath, type StepId } from '../lib/steps'
import classes from './CasePage.module.css'

export function CasePage() {
  const { caseId = '' } = useParams()
  const caseQuery = useCase(caseId)
  const location = useLocation()
  const current = stepFromPath(location.pathname)
  const direction = useStepDirection(current)
  useMarkReviewedOnAdvance(caseId, current, caseQuery.data)

  if (caseQuery.isPending) {
    return (
      <Center py="xl">
        <Loader />
      </Center>
    )
  }

  if (caseQuery.isError) {
    return (
      <Stack>
        <BackLink />
        <Alert color="brick" title="Could not load the case">
          {caseQuery.error.message}
        </Alert>
      </Stack>
    )
  }

  const detail = caseQuery.data
  const jobActive = isJobActive(detail.job)
  const steps = caseSteps(detail)

  return (
    <Stack gap="md">
      <BackLink />
      <Stack gap={4}>
        <Title order={2}>{detail.name}</Title>
        <Group gap="xs">
          <Text size="sm" c="dimmed">
            {matterLine(detail)}
          </Text>
          {jobActive && detail.job && (
            <Badge color={jobStateColors[detail.job.state]} variant="light">
              {jobKindLabels[detail.job.kind]} {detail.job.state.toLowerCase()}
            </Badge>
          )}
        </Group>
      </Stack>

      {current && <StepProgress caseId={caseId} steps={steps} current={current} job={detail.job} />}

      {/* Keyed by step so each step's page plays its entrance, sliding in from the side it was reached from. */}
      <div key={current} className={classes.view} style={{ '--from': `${direction * 24}px` } as CSSProperties}>
        <Outlet />
      </div>
    </Stack>
  )
}

/**
 * Which way the last step change went: 1 forward, -1 back, or 0 when the page first opens. It is worked out while
 * rendering, by remembering the previous step in state, so the new page's first frame already slides the right way.
 */
function useStepDirection(current: StepId | null): number {
  const [previous, setPrevious] = useState({ step: current, direction: 0 })
  if (previous.step !== current) {
    const from = previous.step ? STEP_IDS.indexOf(previous.step) : -1
    const to = current ? STEP_IDS.indexOf(current) : -1
    const direction = from < 0 || to < 0 ? 0 : Math.sign(to - from)
    setPrevious({ step: current, direction })
    return direction
  }
  return previous.direction
}

/**
 * Records that the analysis was reviewed when the attorney moves on from the review step to the letter, however
 * they get there. A later analysis needs reviewing again.
 */
function useMarkReviewedOnAdvance(caseId: string, current: StepId | null, detail: CaseDetail | undefined) {
  const queryClient = useQueryClient()
  const previous = useRef(current)
  const { mutate } = useMutation({
    mutationFn: () => api.markAnalysisReviewed(caseId),
    onSuccess: (updated) => queryClient.setQueryData(caseKeys.detail(caseId), updated),
    onError: (error) => notifications.show({ color: 'brick', title: 'Could not mark the review as done', message: error.message }),
  })
  const needsReview = detail !== undefined && detail.analysisUpdatedAt !== null && !detail.analysisReviewed

  useEffect(() => {
    if (previous.current === 'review' && current === 'letter' && needsReview) {
      mutate()
    }
    previous.current = current
  }, [current, needsReview, mutate])
}

/** Opens a case on its first unfinished step. */
export function FirstStepRedirect() {
  const { caseId = '' } = useParams()
  const caseQuery = useCase(caseId)
  return caseQuery.data ? <Navigate to={`/cases/${caseId}/${firstOpenStep(caseSteps(caseQuery.data))}`} replace /> : null
}

function matterLine(detail: CaseDetail): string {
  const documents = `${detail.records.length} ${detail.records.length === 1 ? 'document' : 'documents'}`
  const place = [detail.county && `${detail.county} County`, detail.jurisdiction].filter(Boolean).join(', ')
  const loss = detail.dateOfLoss ? `Date of loss ${formatDateOnly(detail.dateOfLoss)}` : 'No date of loss yet'
  return [loss, place, documents].filter(Boolean).join(' · ')
}

function BackLink() {
  return (
    <Anchor component={Link} to="/" size="sm">
      <Group gap={4}>
        <IconBack size={14} />
        All cases
      </Group>
    </Anchor>
  )
}
