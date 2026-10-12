import { Alert, Button, Card, Group, Stack, Text, Title } from '@mantine/core'
import { modals } from '@mantine/modals'
import { useParams } from 'react-router'
import type { CaseDetail } from '../api/types'
import { IconRun } from '../components/icons'
import { JobStatusCard } from '../components/JobStatusCard'
import { useCase } from '../hooks/useCase'
import { useJobActions, useNow } from '../hooks/useJob'
import { formatDateTime, isJobActive, recordsNeedingSummaries } from '../lib/format'

export function AnalyzeTab() {
  const { caseId = '' } = useParams()
  const caseQuery = useCase(caseId)
  const { start, cancel } = useJobActions(caseId)
  const now = useNow(isJobActive(caseQuery.data?.job))

  if (!caseQuery.data) {
    return null
  }

  const detail = caseQuery.data
  const jobActive = isJobActive(detail.job)

  return (
    <Stack gap="md">
      <Card withBorder padding="lg" radius="lg">
        <Stack gap="sm">
          <Title order={3}>Analyze the documents</Title>
          <Text size="sm" c="dimmed">
            Reads each document and pulls out quotes with their page numbers. Then it checks the documents against each other
            for contradictions and writes a case summary that cites them. Documents that haven't changed since the last
            analysis aren't read again.
          </Text>
          <Text size="sm">Last analyzed: {formatDateTime(detail.analysisUpdatedAt)}</Text>
          {detail.analysisOutOfDate && <Alert color="amber">Documents changed after the last analysis.</Alert>}
          <Group>
            <Button
              leftSection={<IconRun size={16} />}
              onClick={() => confirmAnalysis(detail, () => start.mutate('Ingest'))}
              disabled={jobActive || detail.records.length === 0}
              loading={start.isPending}
            >
              {detail.analysisUpdatedAt ? 'Analyze again' : 'Analyze'}
            </Button>
          </Group>
        </Stack>
      </Card>

      {detail.job?.kind === 'Ingest' && (
        <JobStatusCard job={detail.job} onCancel={() => cancel.mutate()} cancelling={cancel.isPending} now={now} />
      )}
    </Stack>
  )
}

function confirmAnalysis(detail: CaseDetail, onConfirm: () => void) {
  const toRead = recordsNeedingSummaries(detail.records)
  modals.openConfirmModal({
    title: 'Analyze the documents?',
    children: (
      <Stack gap="xs">
        <Text size="sm">
          This reads {toRead === 0 ? 'no new documents' : `up to ${toRead} new or changed ${toRead === 1 ? 'document' : 'documents'}`}, then
          checks every document for contradictions and writes the case summary.
        </Text>
        <Text size="sm" c="dimmed">
          Large cases can take several minutes. You can leave this page while it runs.
        </Text>
      </Stack>
    ),
    labels: { confirm: 'Analyze', cancel: 'Not now' },
    onConfirm,
  })
}
