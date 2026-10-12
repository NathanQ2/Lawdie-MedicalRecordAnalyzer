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
            The analysis finds the quote and page behind each fact, and lists the places where the documents disagree.
            Documents you haven't changed are not read again.
          </Text>
          <Text size="sm">Last analyzed: {formatDateTime(detail.analysisUpdatedAt)}</Text>
          {detail.analysisOutOfDate && <Alert color="amber">You changed the documents after the last analysis. Analyze again to include the changes.</Alert>}
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
          The analysis reads {toRead === 0 ? 'no new documents' : `up to ${toRead} new or changed ${toRead === 1 ? 'document' : 'documents'}`}.
          Then it compares all the documents and writes the case summary again.
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
