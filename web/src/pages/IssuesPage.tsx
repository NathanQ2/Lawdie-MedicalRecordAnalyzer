import { Alert, Anchor, Badge, Button, Center, Group, Loader, Paper, SegmentedControl, Stack, Text, Title, Tooltip } from '@mantine/core'
import { notifications } from '@mantine/notifications'
import { useState, type ReactNode } from 'react'
import { Link, useParams } from 'react-router'
import type { CaseDetail, CitedDate, ReviewItem } from '../api/types'
import { ReviewItemCard } from '../components/ReviewItemCard'
import { useAnalysis, useCase, useMatterUpdate, useReview, useReviewUpdate } from '../hooks/useCase'
import { useSourcePanel } from '../hooks/useSourcePanel'
import { recordName } from '../lib/citations'
import { formatDateOnly } from '../lib/format'
import { isIssue } from '../lib/workspace'

/**
 * What to settle before drafting: intake details the documents disagree with, contradictions between documents,
 * and documents that couldn't be read. Each item is resolved or dismissed with an optional note.
 */
export function IssuesPage() {
  const { caseId = '' } = useParams()
  const caseQuery = useCase(caseId)
  const analysis = useAnalysis(caseId, caseQuery.data ? caseQuery.data.analysisUpdatedAt : undefined)
  const review = useReview(caseId)
  const update = useReviewUpdate(caseId)
  const panel = useSourcePanel()
  const [filter, setFilter] = useState<'open' | 'all'>('open')

  if (caseQuery.isPending || analysis.isPending || review.isPending) {
    return (
      <Center py="xl">
        <Loader />
      </Center>
    )
  }

  if (!caseQuery.data || !analysis.data) {
    return (
      <Paper withBorder p="xl" radius="md">
        <Stack align="center" gap="xs">
          <Text fw={500}>The documents have not been analyzed yet</Text>
          <Text size="sm" c="dimmed">
            <Anchor component={Link} to={`/cases/${caseId}/records`}>
              Analyze the documents
            </Anchor>{' '}
            to find where they disagree.
          </Text>
        </Stack>
      </Paper>
    )
  }

  const detail = caseQuery.data
  const items = (review.data ?? []).filter(isIssue)
  const handled = items.filter((item) => item.status !== 'Open').length
  const shown = filter === 'open' ? items.filter((item) => item.status === 'Open') : items
  const card = (item: ReviewItem, actions?: ReactNode) => (
    <ReviewItemCard
      key={item.id}
      item={item}
      saving={update.isPending && update.variables?.itemId === item.id}
      onUpdate={(status, note) => update.mutate({ itemId: item.id, status, note })}
      onSelectCitation={panel.show}
      actions={actions}
    />
  )
  const intake = shown.filter((item) => item.kind === 'IntakeMismatch')
  const contradictions = shown.filter((item) => item.kind === 'Contradiction')
  const skipped = analysis.data.skippedFiles

  return (
    <Stack gap="lg">
      <Group justify="space-between" align="flex-end">
        <Stack gap={4}>
          <Title order={3}>Issues</Title>
          <Text size="xs" c="dimmed">
            Decide which version is right where the documents disagree. Resolved items stay on record with your note.
          </Text>
        </Stack>
        <Group gap="sm">
          <Text size="sm" c="dimmed">
            {handled} of {items.length} handled
          </Text>
          <SegmentedControl
            size="xs"
            value={filter}
            onChange={(value) => setFilter(value as 'open' | 'all')}
            data={[
              { value: 'open', label: 'Open' },
              { value: 'all', label: 'All' },
            ]}
          />
        </Group>
      </Group>

      {intake.length > 0 && (
        <Section title="Matter details" count={intake.length}>
          {intake.map((item) => card(item, <TakeRecordedDate detail={detail} recorded={analysis.data?.recordedDateOfLoss ?? null} />))}
        </Section>
      )}

      <Section title="Contradictions" count={contradictions.length}>
        {contradictions.length > 0 ? (
          contradictions.map((item) => card(item))
        ) : (
          <Text size="sm" c="dimmed">
            {analysis.data.contradictions.length === 0 ? 'No contradictions were found.' : 'Every contradiction has been resolved or dismissed.'}
          </Text>
        )}
      </Section>

      {skipped.length > 0 && (
        <Section title="Documents that couldn't be read" count={skipped.length}>
          <Alert color="amber">
            <Stack gap={4}>
              {skipped.map((file) => (
                <Text key={file.filePath} size="sm">
                  <b>{recordName(file.filePath)}</b>: {file.reason}
                </Text>
              ))}
            </Stack>
          </Alert>
        </Section>
      )}
    </Stack>
  )
}

function Section({ title, count, children }: { title: string; count: number; children: ReactNode }) {
  return (
    <Stack gap="xs">
      <Group gap="xs">
        <Title order={4}>{title}</Title>
        <Badge variant="light" color={count > 0 ? 'brick' : 'gray'}>
          {count}
        </Badge>
      </Group>
      {children}
    </Stack>
  )
}

/** Replaces the intake date of loss with the one the documents state. */
function TakeRecordedDate({ detail, recorded }: { detail: CaseDetail; recorded: CitedDate | null }) {
  const save = useMatterUpdate(detail.id)
  if (!recorded) {
    return null
  }

  const take = () =>
    save.mutate(
      { name: detail.name, dateOfLoss: recorded.date, jurisdiction: detail.jurisdiction, county: detail.county },
      {
        onSuccess: () => notifications.show({ color: 'sage', message: `The date of loss is now ${formatDateOnly(recorded.date)}.` }),
        onError: (error) => notifications.show({ color: 'brick', title: 'Could not save the date of loss', message: error.message }),
      },
    )

  return (
    <Tooltip label="Changes the date of loss in the matter details. The letter uses it.">
      <Button size="compact-xs" variant="light" color="brick" loading={save.isPending} onClick={take}>
        Use {formatDateOnly(recorded.date)}
      </Button>
    </Tooltip>
  )
}
