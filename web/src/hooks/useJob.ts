import { notifications } from '@mantine/notifications'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useEffect, useState } from 'react'
import { api } from '../api/client'
import type { JobKind } from '../api/types'
import { caseKeys, setCaseJob } from './useCase'

/**
 * Starting and cancelling a case's analysis or draft job. A started job goes straight into the cached case so the
 * page shows it, and starts polling, without waiting for a refetch.
 */
export function useJobActions(caseId: string) {
  const queryClient = useQueryClient()

  const start = useMutation({
    mutationFn: (kind: JobKind) => (kind === 'Ingest' ? api.startIngest(caseId) : api.startDraft(caseId)),
    onSuccess: (job) => setCaseJob(queryClient, caseId, job),
    onError: (error) => notifications.show({ color: 'brick', title: 'Could not start', message: error.message }),
  })

  const cancel = useMutation({
    mutationFn: () => api.cancelJob(caseId),
    onSettled: () => queryClient.invalidateQueries({ queryKey: caseKeys.detail(caseId) }),
    onError: (error) => notifications.show({ color: 'brick', title: 'Could not cancel', message: error.message }),
  })

  return { start, cancel }
}

/**
 * The current time, ticking every second while active, so elapsed times update between polls.
 */
export function useNow(active: boolean): number {
  const [now, setNow] = useState(() => Date.now())
  useEffect(() => {
    if (!active) {
      return
    }
    const timer = window.setInterval(() => setNow(Date.now()), 1000)
    return () => window.clearInterval(timer)
  }, [active])
  return now
}
