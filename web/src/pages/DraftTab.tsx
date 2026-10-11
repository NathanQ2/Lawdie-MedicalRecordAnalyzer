import { Alert, Anchor, Badge, Box, Button, Center, Drawer, Grid, Group, Loader, Menu, Paper, SegmentedControl, Stack, Text, Title } from '@mantine/core'
import { useMediaQuery } from '@mantine/hooks'
import { notifications } from '@mantine/notifications'
import { IconDownload } from '@tabler/icons-react'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { Link, useParams } from 'react-router'
import { api } from '../api/client'
import type { Citation, ReviewItem, ReviewStatus } from '../api/types'
import { AnnotatedText } from '../components/AnnotatedText'
import { ReviewItemCard } from '../components/ReviewItemCard'
import { SourceViewer } from '../components/SourceViewer'
import { caseKeys, useCase, useDraft, useReview } from '../hooks/useCase'
import { citationKey, citationNumbering } from '../lib/citations'
import classes from './AnalysisTab.module.css'

type Panel = 'review' | 'source'

export function DraftTab() {
  const { caseId = '' } = useParams()
  const caseQuery = useCase(caseId)
  const draft = useDraft(caseId, caseQuery.data ? caseQuery.data.draftUpdatedAt : undefined)
  const review = useReview(caseId)
  const [selected, setSelected] = useState<Citation | null>(null)
  const [panel, setPanel] = useState<Panel>('review')
  const wide = useMediaQuery('(min-width: 75em)')

  if (caseQuery.isPending || draft.isPending) {
    return (
      <Center py="xl">
        <Loader />
      </Center>
    )
  }

  if (draft.isError) {
    return <Alert color="red" title="Could not load the draft">{draft.error.message}</Alert>
  }

  if (!draft.data) {
    return (
      <Paper withBorder p="xl" radius="md">
        <Stack align="center" gap="xs">
          <Text fw={500}>No demand letter draft yet</Text>
          <Text size="sm" c="dimmed">
            After ingest,{' '}
            <Anchor component={Link} to={`/cases/${caseId}/run`}>
              draft the demand letter
            </Anchor>
            .
          </Text>
        </Stack>
      </Paper>
    )
  }

  const segments = draft.data.segments
  const narrative = segments.map((segment) => segment.text).join('')
  const citations = segments.flatMap((segment) => segment.citations)
  const openItems = review.data?.filter((item) => item.status === 'Open').length ?? 0

  const select = (citation: Citation) => {
    setSelected(citation)
    setPanel('source')
  }

  const sidePanel = (
    <Stack gap="sm" h="100%">
      <SegmentedControl
        value={panel}
        onChange={(value) => setPanel(value as Panel)}
        data={[
          { value: 'review', label: `Review (${openItems} open)` },
          { value: 'source', label: 'Source', disabled: !selected },
        ]}
      />
      <Box style={{ flex: 1, minHeight: 0 }}>
        {panel === 'source' && selected ? (
          <SourceViewer caseId={caseId} citation={selected} onClose={() => setPanel('review')} />
        ) : (
          <ReviewChecklist caseId={caseId} items={review.data} loading={review.isPending} onSelectCitation={select} />
        )}
      </Box>
    </Stack>
  )

  return (
    <Stack gap="md">
      {caseQuery.data?.draftOutOfDate && (
        <Alert color="yellow">The analysis changed after this draft was written. Draft again from the Run tab to update it.</Alert>
      )}

      <Group justify="space-between" align="flex-end">
        <Stack gap={4}>
          <Title order={3}>Demand letter narrative</Title>
          <Text size="xs" c="dimmed">
            Click a number to see its source. Yellow sentences have no citation; purple placeholders must be filled in.
          </Text>
        </Stack>
        <ExportMenu caseId={caseId} openItems={openItems} />
      </Group>

      <Grid gap="md">
        <Grid.Col span={wide ? 7 : 12}>
          <Paper withBorder p="lg" radius="md">
            <AnnotatedText
              text={narrative}
              citations={citations}
              unsupportedStatements={draft.data.unsupportedStatements}
              numberOf={citationNumbering(citations)}
              selectedKey={selected ? citationKey(selected) : null}
              onSelect={select}
              highlightPlaceholders
            />
          </Paper>
          {!wide && (
            <Box mt="md">
              <ReviewChecklist caseId={caseId} items={review.data} loading={review.isPending} onSelectCitation={select} />
            </Box>
          )}
        </Grid.Col>
        {wide && (
          <Grid.Col span={5}>
            <Box className={classes.viewer}>{sidePanel}</Box>
          </Grid.Col>
        )}
      </Grid>

      {!wide && (
        <Drawer opened={selected !== null && panel === 'source'} onClose={() => setPanel('review')} position="right" size="xl" withCloseButton={false} padding={0}>
          {selected && <SourceViewer caseId={caseId} citation={selected} onClose={() => setPanel('review')} />}
        </Drawer>
      )}
    </Stack>
  )
}

function ExportMenu({ caseId, openItems }: { caseId: string; openItems: number }) {
  return (
    <Group gap="sm">
      {openItems > 0 && (
        <Badge color="orange" variant="light">
          {openItems} review {openItems === 1 ? 'item' : 'items'} open
        </Badge>
      )}
      <Menu position="bottom-end">
        <Menu.Target>
          <Button leftSection={<IconDownload size={16} />} variant="light">
            Export
          </Button>
        </Menu.Target>
        <Menu.Dropdown>
          <Menu.Item component="a" href={api.draftExportUrl(caseId, 'docx')} download>
            Word (.docx)
          </Menu.Item>
          <Menu.Item component="a" href={api.draftExportUrl(caseId, 'md')} download>
            Markdown (.md)
          </Menu.Item>
          <Menu.Item component="a" href={api.draftExportUrl(caseId, 'txt')} download>
            Plain text (.txt)
          </Menu.Item>
        </Menu.Dropdown>
      </Menu>
    </Group>
  )
}

interface ReviewChecklistProps {
  caseId: string
  items: ReviewItem[] | undefined
  loading: boolean
  onSelectCitation: (citation: Citation) => void
}

export function ReviewChecklist({ caseId, items, loading, onSelectCitation }: ReviewChecklistProps) {
  const queryClient = useQueryClient()
  const [filter, setFilter] = useState<'open' | 'all'>('open')
  const update = useMutation({
    mutationFn: ({ itemId, status, note }: { itemId: string; status: ReviewStatus; note: string | null }) =>
      api.updateReview(caseId, itemId, status, note),
    onSuccess: (updated) =>
      queryClient.setQueryData<ReviewItem[]>(caseKeys.review(caseId), (current) =>
        current?.map((item) => (item.id === updated.id ? updated : item)),
      ),
    onError: (error) => notifications.show({ color: 'red', title: 'Could not update the review item', message: error.message }),
  })

  if (loading || !items) {
    return (
      <Center py="lg">
        <Loader size="sm" />
      </Center>
    )
  }

  const done = items.filter((item) => item.status !== 'Open').length
  const shown = filter === 'open' ? items.filter((item) => item.status === 'Open') : items

  return (
    <Paper withBorder radius="md" p="md" h="100%" style={{ overflowY: 'auto' }}>
      <Stack gap="sm">
        <Group justify="space-between">
          <Stack gap={0}>
            <Title order={5}>Attorney review</Title>
            <Text size="xs" c="dimmed">
              {done} of {items.length} handled
            </Text>
          </Stack>
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
        {shown.length === 0 ? (
          <Text size="sm" c="dimmed" ta="center" py="md">
            {items.length === 0 ? 'Nothing to review.' : 'Every item has been resolved or dismissed.'}
          </Text>
        ) : (
          shown.map((item) => (
            <ReviewItemCard
              key={item.id}
              item={item}
              saving={update.isPending && update.variables?.itemId === item.id}
              onUpdate={(status, note) => update.mutate({ itemId: item.id, status, note })}
              onSelectCitation={onSelectCitation}
            />
          ))
        )}
      </Stack>
    </Paper>
  )
}
