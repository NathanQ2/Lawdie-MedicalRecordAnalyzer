import { Accordion, Alert, Button, Card, Group, Paper, Stack, Text, Title } from '@mantine/core'
import { Dropzone, type FileRejection } from '@mantine/dropzone'
import { modals } from '@mantine/modals'
import { notifications } from '@mantine/notifications'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useParams } from 'react-router'
import { api } from '../api/client'
import type { CaseAnalysis, CaseDetail, RecordInfo } from '../api/types'
import { AnnotatedText } from '../components/AnnotatedText'
import { IconClose, IconRun, IconUpload } from '../components/icons'
import { JobStatusCard } from '../components/JobStatusCard'
import { RecordsTable } from '../components/RecordsTable'
import { caseKeys, useAnalysis, useCase } from '../hooks/useCase'
import { useJobActions, useNow } from '../hooks/useJob'
import { useSourcePanel } from '../hooks/useSourcePanel'
import { citationKey, citationNumbering, recordName } from '../lib/citations'
import { formatDateTime, isJobActive, recordsNeedingSummaries } from '../lib/format'

const MAX_UPLOAD_BYTES = 250 * 1024 * 1024

// Images are read by OCR when it is configured on the server; otherwise analysis skips them with the reason.
const ACCEPTED_TYPES = {
  'application/pdf': ['.pdf'],
  'text/plain': ['.txt'],
  'text/markdown': ['.md'],
  'image/png': ['.png'],
  'image/jpeg': ['.jpg', '.jpeg'],
  'image/tiff': ['.tif', '.tiff'],
  'image/bmp': ['.bmp'],
  'image/heif': ['.heif'],
}

/**
 * The case's documents: add and remove them, analyze them, and read what each one says.
 */
export function DocumentsPage() {
  const { caseId = '' } = useParams()
  const caseQuery = useCase(caseId)
  const analysis = useAnalysis(caseId, caseQuery.data ? caseQuery.data.analysisUpdatedAt : undefined)
  const queryClient = useQueryClient()

  const refresh = () => queryClient.invalidateQueries({ queryKey: caseKeys.detail(caseId) })

  const upload = useMutation({
    mutationFn: (files: File[]) => api.uploadRecords(caseId, files),
    onSuccess: async (records, files) => {
      queryClient.setQueryData<CaseDetail>(caseKeys.detail(caseId), (current) => (current ? { ...current, records } : current))
      await refresh()
      notifications.show({ color: 'sage', message: `Uploaded ${files.length} ${files.length === 1 ? 'document' : 'documents'}.` })
    },
    onError: (error) => notifications.show({ color: 'brick', title: 'Upload failed', message: error.message }),
  })

  const remove = useMutation({
    mutationFn: (fileName: string) => api.deleteRecord(caseId, fileName),
    onSuccess: refresh,
    onError: (error) => notifications.show({ color: 'brick', title: 'Could not delete the document', message: error.message }),
  })

  if (!caseQuery.data) {
    return null
  }

  const detail = caseQuery.data
  const jobActive = isJobActive(detail.job)

  const confirmDelete = (record: RecordInfo) =>
    modals.openConfirmModal({
      title: `Delete ${record.name}?`,
      children: <Text size="sm">The document is removed from this case. Analyze again to update the results.</Text>,
      labels: { confirm: 'Delete document', cancel: 'Keep it' },
      confirmProps: { color: 'brick' },
      onConfirm: () => remove.mutate(record.name),
    })

  const reject = (rejections: FileRejection[]) =>
    notifications.show({
      color: 'brick',
      title: 'Some files were not added',
      message: rejections.map((rejection) => `${rejection.file.name}: ${rejection.errors[0]?.message}`).join('\n'),
    })

  return (
    <Stack gap="md">
      <AnalyzeCard detail={detail} />

      {jobActive && <Alert color="slate">You can't change documents while the case is being analyzed or drafted.</Alert>}

      <Dropzone
        onDrop={(files) => upload.mutate(files)}
        onReject={reject}
        accept={ACCEPTED_TYPES}
        maxSize={MAX_UPLOAD_BYTES}
        loading={upload.isPending}
        disabled={jobActive}
      >
        <Group justify="center" gap="lg" mih={110} style={{ pointerEvents: 'none' }}>
          <Dropzone.Accept>
            <IconUpload size={40} stroke={1.5} />
          </Dropzone.Accept>
          <Dropzone.Reject>
            <IconClose size={40} stroke={1.5} />
          </Dropzone.Reject>
          <Dropzone.Idle>
            <IconUpload size={40} stroke={1.5} />
          </Dropzone.Idle>
          <Stack gap={4}>
            <Text size="lg">Drag documents here or click to choose files</Text>
            <Text size="sm" c="dimmed">
              PDF, text and image files. Quotes read from scans and photos are marked so you can check them.
            </Text>
          </Stack>
        </Group>
      </Dropzone>

      <Paper withBorder radius="md">
        <RecordsTable records={detail.records} recordUrl={(fileName) => api.recordUrl(caseId, fileName)} onDelete={confirmDelete} disabled={jobActive} />
      </Paper>

      {analysis.data && analysis.data.documentSummaries.length > 0 && <DocumentSummaries analysis={analysis.data} />}
    </Stack>
  )
}

/** Starts the analysis, after confirming, and shows its progress. */
function AnalyzeCard({ detail }: { detail: CaseDetail }) {
  const { start, cancel } = useJobActions(detail.id)
  const now = useNow(isJobActive(detail.job))
  const jobActive = isJobActive(detail.job)

  const confirm = () => {
    const toRead = recordsNeedingSummaries(detail.records)
    modals.openConfirmModal({
      title: 'Analyze the documents?',
      children: (
        <Stack gap="xs">
          <Text size="sm">
            The analysis reads {toRead === 0 ? 'no new documents' : `up to ${toRead} new or changed ${toRead === 1 ? 'document' : 'documents'}`}.
            Then it compares all the documents, lists the visits by date and writes the case summary again.
          </Text>
          <Text size="sm" c="dimmed">
            Large cases can take several minutes. You can leave this page while it runs.
          </Text>
        </Stack>
      ),
      labels: { confirm: 'Analyze', cancel: 'Not now' },
      onConfirm: () => start.mutate('Ingest'),
    })
  }

  return (
    <Stack gap="md">
      <Card withBorder padding="lg" radius="lg">
        <Group justify="space-between" align="center" wrap="wrap">
          <Stack gap={4} style={{ flex: 1, minWidth: 240 }}>
            <Title order={4}>Analysis</Title>
            <Text size="sm" c="dimmed">
              Finds the quote and page behind each fact, lists the visits by date, and shows where the documents disagree.
              Documents you haven't changed are not read again.
            </Text>
            <Text size="sm">Last analyzed: {formatDateTime(detail.analysisUpdatedAt)}</Text>
            {detail.analysisOutOfDate && <Text size="sm" c="amber">You changed the documents after the last analysis. Analyze again to include the changes.</Text>}
          </Stack>
          <Button leftSection={<IconRun size={16} />} onClick={confirm} disabled={jobActive || detail.records.length === 0} loading={start.isPending}>
            {detail.analysisUpdatedAt ? 'Analyze again' : 'Analyze'}
          </Button>
        </Group>
      </Card>
      {detail.job?.kind === 'Ingest' && detail.job.state !== 'Succeeded' && (
        <JobStatusCard job={detail.job} onCancel={() => cancel.mutate()} cancelling={cancel.isPending} now={now} />
      )}
    </Stack>
  )
}

/** What each document says, with its quotes. */
function DocumentSummaries({ analysis }: { analysis: CaseAnalysis }) {
  const panel = useSourcePanel()
  const selectedKey = panel.selected ? citationKey(panel.selected) : null
  // Sources and document summaries are stored in the same order, one per readable document.
  const documents = analysis.documentSummaries.map((summary, index) => ({
    name: recordName(analysis.sources[index]?.filePath ?? summary.citations[0]?.filePath ?? `Document ${index + 1}`),
    summary,
  }))

  return (
    <Stack gap="xs">
      <Title order={4}>What each document says</Title>
      <Accordion variant="separated" multiple radius="md">
        {documents.map(({ name, summary }) => (
          <Accordion.Item key={name} value={name}>
            <Accordion.Control>
              <Group justify="space-between" pr="sm">
                <Text size="sm" fw={500}>
                  {name}
                </Text>
                <Text size="xs" c="dimmed">
                  {summary.citations.length} {summary.citations.length === 1 ? 'quote' : 'quotes'}
                </Text>
              </Group>
            </Accordion.Control>
            <Accordion.Panel>
              <AnnotatedText
                text={summary.summary}
                citations={summary.citations}
                numberOf={citationNumbering(summary.citations)}
                selectedKey={selectedKey}
                onSelect={panel.show}
              />
            </Accordion.Panel>
          </Accordion.Item>
        ))}
      </Accordion>
    </Stack>
  )
}
