import { Alert, Group, Paper, Stack, Text } from '@mantine/core'
import { Dropzone, type FileRejection } from '@mantine/dropzone'
import { modals } from '@mantine/modals'
import { notifications } from '@mantine/notifications'
import { IconClose, IconUpload } from '../components/icons'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useParams } from 'react-router'
import { api } from '../api/client'
import type { CaseDetail, RecordInfo } from '../api/types'
import { RecordsTable } from '../components/RecordsTable'
import { caseKeys, useCase } from '../hooks/useCase'
import { isJobActive } from '../lib/format'

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

export function RecordsTab() {
  const { caseId = '' } = useParams()
  const caseQuery = useCase(caseId)
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
      {jobActive && <Alert color="slate">You can't change documents while the case is being analyzed or drafted.</Alert>}
      {detail.analysisOutOfDate && !jobActive && (
        <Alert color="amber">You changed the documents after the last analysis. Analyze again to include the changes.</Alert>
      )}

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
        <RecordsTable
          records={detail.records}
          recordUrl={(fileName) => api.recordUrl(caseId, fileName)}
          onDelete={confirmDelete}
          disabled={jobActive}
        />
      </Paper>
    </Stack>
  )
}
