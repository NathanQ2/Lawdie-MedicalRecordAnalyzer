import { notifications } from '@mantine/notifications'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { useEffect, useRef } from 'react'
import { api } from '../api/client'
import type { CaseDetail, JobStatus } from '../api/types'
import { isJobActive, jobKindLabels } from '../lib/format'

const POLL_INTERVAL_MS = 1500

// Several components on a page load the same case, so each finished job is announced only by the first to see it.
const announcedJobs = new Set<string>()

export const caseKeys = {
  all: ['cases'] as const,
  detail: (caseId: string) => ['cases', caseId] as const,
  analysis: (caseId: string, updatedAt: string | null) => ['cases', caseId, 'analysis', updatedAt] as const,
  draft: (caseId: string, updatedAt: string | null) => ['cases', caseId, 'draft', updatedAt] as const,
  review: (caseId: string) => ['cases', caseId, 'review'] as const,
}

export function useCases() {
  return useQuery({
    queryKey: caseKeys.all,
    queryFn: api.listCases,
    refetchInterval: (query) => (query.state.data?.some((item) => isJobActive(item.job)) ? POLL_INTERVAL_MS : false),
  })
}

/**
 * Loads a case, polling while its job is queued or running, and announces the job's outcome when it ends.
 */
export function useCase(caseId: string) {
  const queryClient = useQueryClient()
  const query = useQuery({
    queryKey: caseKeys.detail(caseId),
    queryFn: () => api.getCase(caseId),
    refetchInterval: (current) => (isJobActive(current.state.data?.job) ? POLL_INTERVAL_MS : false),
  })

  const previousJob = useRef<JobStatus | null | undefined>(undefined)
  const job = query.data?.job
  useEffect(() => {
    const previous = previousJob.current
    previousJob.current = job
    if (previous && isJobActive(previous) && job && job.id === previous.id && !isJobActive(job) && !announcedJobs.has(job.id)) {
      announcedJobs.add(job.id)
      announceFinishedJob(job)
      void queryClient.invalidateQueries({ queryKey: caseKeys.all })
    }
  }, [job, queryClient])

  return query
}

/**
 * Loads the case's analysis (null until ingest has run). The key includes the analysis timestamp from the case,
 * so a newer analysis is always fetched while an unchanged one is never refetched.
 */
export function useAnalysis(caseId: string, analysisUpdatedAt: string | null | undefined) {
  return useQuery({
    queryKey: caseKeys.analysis(caseId, analysisUpdatedAt ?? null),
    queryFn: () => api.getAnalysis(caseId),
    enabled: analysisUpdatedAt !== undefined,
    staleTime: Infinity,
  })
}

/** Loads the case's draft (null until drafted), keyed by its timestamp like the analysis. */
export function useDraft(caseId: string, draftUpdatedAt: string | null | undefined) {
  return useQuery({
    queryKey: caseKeys.draft(caseId, draftUpdatedAt ?? null),
    queryFn: () => api.getDraft(caseId),
    enabled: draftUpdatedAt !== undefined,
    staleTime: Infinity,
  })
}

/** Loads the review checklist; finished jobs invalidate every case query, which reloads it. */
export function useReview(caseId: string) {
  return useQuery({ queryKey: caseKeys.review(caseId), queryFn: () => api.getReview(caseId) })
}

/**
 * Puts a freshly started job into the cached case so the UI shows it (and starts polling) immediately.
 */
export function setCaseJob(queryClient: ReturnType<typeof useQueryClient>, caseId: string, job: JobStatus) {
  queryClient.setQueryData<CaseDetail>(caseKeys.detail(caseId), (current) => (current ? { ...current, job } : current))
}

function announceFinishedJob(job: JobStatus) {
  const name = jobKindLabels[job.kind]
  if (job.state === 'Succeeded') {
    notifications.show({ color: 'sage', title: `${name} finished`, message: job.kind === 'Ingest' ? 'The documents are analyzed.' : 'The demand letter is ready to review.' })
  } else if (job.state === 'Failed') {
    notifications.show({ color: 'brick', title: `${name} failed`, message: job.error ?? 'The job failed.', autoClose: false })
  } else if (job.state === 'Cancelled') {
    notifications.show({ color: 'amber', title: `${name} cancelled`, message: job.error ?? 'The job was cancelled.' })
  }
}
