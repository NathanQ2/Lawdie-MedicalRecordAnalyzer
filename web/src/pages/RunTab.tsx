import { Alert, Button, Card, Group, Stack, Text, Title } from '@mantine/core'
import { modals } from '@mantine/modals'
import { notifications } from '@mantine/notifications'
import { IconPlayerPlay } from '@tabler/icons-react'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useEffect, useState } from 'react'
import { useParams } from 'react-router'
import { api } from '../api/client'
import type { CaseDetail, JobKind } from '../api/types'
import { JobStatusCard } from '../components/JobStatusCard'
import { caseKeys, setCaseJob, useCase } from '../hooks/useCase'
import { formatDateTime, isJobActive, recordsNeedingSummaries } from '../lib/format'

export function RunTab() {
  const { caseId = '' } = useParams()
  const caseQuery = useCase(caseId)
  const queryClient = useQueryClient()
  const now = useNow(isJobActive(caseQuery.data?.job))

  const start = useMutation({
    mutationFn: (kind: JobKind) => (kind === 'Ingest' ? api.startIngest(caseId) : api.startDraft(caseId)),
    onSuccess: (job) => setCaseJob(queryClient, caseId, job),
    onError: (error) => notifications.show({ color: 'red', title: 'Could not start the job', message: error.message }),
  })

  const cancel = useMutation({
    mutationFn: () => api.cancelJob(caseId),
    onSettled: () => queryClient.invalidateQueries({ queryKey: caseKeys.detail(caseId) }),
    onError: (error) => notifications.show({ color: 'red', title: 'Could not cancel the job', message: error.message }),
  })

  if (!caseQuery.data) {
    return null
  }

  const detail = caseQuery.data
  const jobActive = isJobActive(detail.job)
  const confirmIngest = () => openIngestConfirmation(detail, () => start.mutate('Ingest'))
  const confirmDraft = () =>
    modals.openConfirmModal({
      title: 'Draft the demand letter?',
      children: (
        <Text size="sm">
          This writes the demand letter narrative from this case's documents. It can take a minute or two.
        </Text>
      ),
      labels: { confirm: 'Draft', cancel: 'Not now' },
      onConfirm: () => start.mutate('Draft'),
    })

  return (
    <Stack gap="md">
      <Card withBorder padding="lg" radius="md">
        <Stack gap="sm">
          <Title order={4}>1. Ingest documents</Title>
          <Text size="sm" c="dimmed">
            Reads and summarizes each document, checks the documents against each other for contradictions, and writes a case
            summary with citations. Documents that haven't changed since the last ingest are reused instead of summarized again.
          </Text>
          <Text size="sm">Last ingest: {formatDateTime(detail.analysisUpdatedAt)}</Text>
          {detail.analysisOutOfDate && <Alert color="yellow">Documents changed since the last ingest.</Alert>}
          <Group>
            <Button
              leftSection={<IconPlayerPlay size={16} />}
              onClick={confirmIngest}
              disabled={jobActive || detail.records.length === 0}
              loading={start.isPending && start.variables === 'Ingest'}
            >
              {detail.analysisUpdatedAt ? 'Run ingest again' : 'Run ingest'}
            </Button>
            {detail.records.length === 0 && (
              <Text size="sm" c="dimmed">
                Upload documents first.
              </Text>
            )}
          </Group>
        </Stack>
      </Card>

      <Card withBorder padding="lg" radius="md">
        <Stack gap="sm">
          <Title order={4}>2. Draft the demand letter</Title>
          <Text size="sm" c="dimmed">
            Writes the demand letter narrative from the parts of the documents most relevant to each section, citing the documents
            for every fact.
          </Text>
          <Text size="sm">Last draft: {formatDateTime(detail.draftUpdatedAt)}</Text>
          {detail.draftOutOfDate && <Alert color="yellow">The analysis has changed since this draft was written.</Alert>}
          <Group>
            <Button
              leftSection={<IconPlayerPlay size={16} />}
              onClick={confirmDraft}
              disabled={jobActive || !detail.analysisUpdatedAt}
              loading={start.isPending && start.variables === 'Draft'}
            >
              {detail.draftUpdatedAt ? 'Draft again' : 'Draft'}
            </Button>
            {!detail.analysisUpdatedAt && (
              <Text size="sm" c="dimmed">
                Run ingest first.
              </Text>
            )}
          </Group>
        </Stack>
      </Card>

      {detail.job && (
        <Stack gap="xs">
          <Title order={4}>{jobActive ? 'Current job' : 'Last job'}</Title>
          <JobStatusCard job={detail.job} onCancel={() => cancel.mutate()} cancelling={cancel.isPending} now={now} />
        </Stack>
      )}
    </Stack>
  )
}

function openIngestConfirmation(detail: CaseDetail, onConfirm: () => void) {
  const toSummarize = recordsNeedingSummaries(detail.records)
  modals.openConfirmModal({
    title: 'Run ingest?',
    children: (
      <Stack gap="xs">
        <Text size="sm">
          This summarizes{' '}
          {toSummarize === 0 ? 'no new documents' : `up to ${toSummarize} new or changed ${toSummarize === 1 ? 'document' : 'documents'}`}, then
          checks all documents for contradictions and writes the case summary.
        </Text>
        <Text size="sm" c="dimmed">
          Large cases can take several minutes. You can leave this page; the job keeps running.
        </Text>
      </Stack>
    ),
    labels: { confirm: 'Run ingest', cancel: 'Not now' },
    onConfirm,
  })
}

/**
 * The current time, ticking every second while active, so elapsed times update between polls.
 */
function useNow(active: boolean): number {
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
