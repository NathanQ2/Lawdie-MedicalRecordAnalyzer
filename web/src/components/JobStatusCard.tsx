import { Alert, Badge, Button, Card, Group, Progress, Stack, Text, Tooltip } from '@mantine/core'
import { IconStop } from './icons'
import type { JobStatus } from '../api/types'
import { formatDateTime, formatDuration, isJobActive, jobKindLabels, jobProgress, jobStateColors, stageLabel } from '../lib/format'

interface JobStatusCardProps {
  job: JobStatus
  onCancel?: () => void
  cancelling?: boolean
  // The current time, passed in (rather than read during render) so elapsed time stays stable between ticks.
  now: number
}

export function JobStatusCard({ job, onCancel, cancelling = false, now }: JobStatusCardProps) {
  const active = isJobActive(job)
  const { percent, detail } = jobProgress(job)
  const started = job.startedAt ? new Date(job.startedAt).getTime() : null
  const finished = job.finishedAt ? new Date(job.finishedAt).getTime() : null

  return (
    <Card withBorder padding="lg" radius="md">
      <Stack gap="sm">
        <Group justify="space-between">
          <Group gap="sm">
            <Text fw={600}>{jobKindLabels[job.kind]}</Text>
            <Badge color={jobStateColors[job.state]} variant="light">
              {job.state}
            </Badge>
          </Group>
          {active && onCancel && (
            <Tooltip
              label={
                job.kind === 'Ingest'
                  ? 'Stop the analysis. Documents already read are kept, so the next analysis skips them.'
                  : 'Stop drafting. The current letter stays as it is.'
              }
            >
              <Button
                variant="light"
                color="brick"
                size="xs"
                leftSection={<IconStop size={14} />}
                onClick={onCancel}
                loading={cancelling}
              >
                Cancel
              </Button>
            </Tooltip>
          )}
        </Group>

        {job.state === 'Running' && (
          <>
            <Text size="sm">{stageLabel(job.stage)}</Text>
            <Progress value={percent ?? 100} animated striped={percent === null} aria-label="Job progress" />
            {detail && (
              <Text size="xs" c="dimmed">
                {detail}
              </Text>
            )}
          </>
        )}
        {job.state === 'Queued' && (
          <Text size="sm" c="dimmed">
            {detail}
          </Text>
        )}

        {job.state === 'Failed' && (
          <Alert color="brick" title="The job failed">
            {job.error ?? 'No details were reported.'}
          </Alert>
        )}
        {job.state === 'Cancelled' && job.error && <Alert color="amber">{job.error}</Alert>}

        <Text size="xs" c="dimmed">
          {started === null
            ? `Queued ${formatDateTime(job.queuedAt)}`
            : `Started ${formatDateTime(job.startedAt)} · ${active ? 'running for' : 'took'} ${formatDuration((finished ?? now) - started)}`}
        </Text>
      </Stack>
    </Card>
  )
}
