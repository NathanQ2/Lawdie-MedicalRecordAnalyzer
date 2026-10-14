import { Alert, Badge, Box, Button, Card, Center, Drawer, Grid, Group, Loader, Menu, Paper, SegmentedControl, Stack, Text, Title, Tooltip } from '@mantine/core'
import { modals } from '@mantine/modals'
import { useMediaQuery } from '@mantine/hooks'
import { IconDownload, IconPencil, IconRun } from '../components/icons'
import { useState } from 'react'
import { useParams } from 'react-router'
import { api } from '../api/client'
import type { Citation, ReviewItem } from '../api/types'
import { AnnotatedText } from '../components/AnnotatedText'
import { JobStatusCard } from '../components/JobStatusCard'
import { LetterDetailsModal } from '../components/LetterDetailsModal'
import { ReviewItemCard } from '../components/ReviewItemCard'
import { SourceViewer } from '../components/SourceViewer'
import { useCase, useDraft, useReview, useReviewUpdate } from '../hooks/useCase'
import { useJobActions, useNow } from '../hooks/useJob'
import { citationKey, citationNumbering } from '../lib/citations'
import { isJobActive } from '../lib/format'
import { openIssueCount, openLetterItemCount } from '../lib/workspace'
import classes from './LetterPage.module.css'

type Panel = 'review' | 'source'

export function LetterPage() {
  const { caseId = '' } = useParams()
  const caseQuery = useCase(caseId)
  const draft = useDraft(caseId, caseQuery.data ? caseQuery.data.draftUpdatedAt : undefined)
  const review = useReview(caseId)
  const [selected, setSelected] = useState<Citation | null>(null)
  const [panel, setPanel] = useState<Panel>('review')
  const [shownItem, setShownItem] = useState<ReviewItem | null>(null)
  // The letter details dialog: closed, open, or open at the placeholder a review item asks to fill in.
  const [details, setDetails] = useState<{ focus: string | null } | null>(null)
  const fillIn = (placeholder: string) => setDetails({ focus: placeholder })
  const wide = useMediaQuery('(min-width: 75em)')
  const { start, cancel } = useJobActions(caseId)
  const now = useNow(isJobActive(caseQuery.data?.job))

  if (caseQuery.isPending || draft.isPending) {
    return (
      <Center py="xl">
        <Loader />
      </Center>
    )
  }

  if (draft.isError) {
    return <Alert color="brick" title="Could not load the draft">{draft.error.message}</Alert>
  }

  const job = caseQuery.data?.job ?? null
  const drafting = job?.kind === 'Draft' && isJobActive(job)
  const confirmDraft = () =>
    modals.openConfirmModal({
      title: draft.data ? 'Draft the letter again?' : 'Draft the demand letter?',
      children: (
        <Text size="sm">
          Drafting may take a couple of minutes.
          {draft.data && ' This replaces the current letter. Your review decisions and letter details stay.'}
        </Text>
      ),
      labels: { confirm: 'Draft', cancel: 'Not now' },
      onConfirm: () => start.mutate('Draft'),
    })
  // Shown while a draft is being written, or when the last attempt failed or was cancelled.
  const jobCard = job?.kind === 'Draft' && job.state !== 'Succeeded' && (
    <JobStatusCard job={job} onCancel={() => cancel.mutate()} cancelling={cancel.isPending} now={now} />
  )

  if (!draft.data) {
    return (
      <Stack gap="md">
        <Card withBorder padding="lg" radius="lg">
          <Stack gap="sm">
            <Title order={3}>Draft the demand letter</Title>
            <Text size="sm" c="dimmed">
              Each fact in the letter cites a document. Details the documents don't give, such as the claim number, appear as
              [PLACEHOLDERS] for you to fill in.
            </Text>
            <Group>
              <Button leftSection={<IconRun size={16} />} onClick={confirmDraft} disabled={isJobActive(job)} loading={start.isPending}>
                Draft
              </Button>
            </Group>
          </Stack>
        </Card>
        {jobCard}
      </Stack>
    )
  }

  const segments = draft.data.segments
  const narrative = segments.map((segment) => segment.text).join('')
  const citations = segments.flatMap((segment) => segment.citations)
  // Counted the same way as the sidebar: issues are checked before drafting, letter items are fixed in the letter.
  const openIssues = openIssueCount(review.data)
  const openLetterItems = openLetterItemCount(review.data)

  // The highlighted item is looked up again so its ranges follow the latest review data.
  const shown = review.data?.find((item) => item.id === shownItem?.id) ?? null
  const highlights = (shown?.draftRanges ?? []).map(({ start, length }): [number, number] => [start, start + length])
  const showInDraft = (item: ReviewItem) => setShownItem(shown?.id === item.id ? null : item)

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
          { value: 'review', label: 'Review' },
          { value: 'source', label: 'Source', disabled: !selected },
        ]}
      />
      <Box style={{ flex: 1, minHeight: 0 }}>
        {panel === 'source' && selected ? (
          <SourceViewer caseId={caseId} citation={selected} onClose={() => setPanel('review')} />
        ) : (
          <ReviewChecklist
                caseId={caseId}
                items={review.data}
                loading={review.isPending}
                onSelectCitation={select}
                shownItemId={shown?.id ?? null}
                onShowInDraft={showInDraft}
                onFillIn={fillIn}
              />
        )}
      </Box>
    </Stack>
  )

  return (
    <Stack gap="md">
      {caseQuery.data?.draftOutOfDate && !drafting && (
        <Alert color="amber">
          <Group justify="space-between" gap="sm">
            <Text size="sm">The analysis changed after this letter was drafted.</Text>
            <Button size="xs" variant="light" color="amber" leftSection={<IconRun size={14} />} onClick={confirmDraft} disabled={isJobActive(job)}>
              Draft again
            </Button>
          </Group>
        </Alert>
      )}
      {jobCard}

      <Group justify="space-between" align="flex-end">
        {/* The description wraps before the counts and Export button move to their own line. */}
        <Stack gap={4} flex="1 1 320px">
          <Title order={3}>Demand letter</Title>
          <Text size="xs" c="dimmed">
            Click a number to see its source. Underlined sentences have no citation. Fill in each [BRACKETED] placeholder before sending.
          </Text>
        </Stack>
        <Group gap="sm">
          <Tooltip label="Fill in the claim number, adjuster and other details the documents don't give.">
            <Button variant="default" leftSection={<IconPencil size={16} />} onClick={() => setDetails({ focus: null })}>
              Letter details
            </Button>
          </Tooltip>
          <ExportMenu caseId={caseId} issues={openIssues} letterItems={openLetterItems} />
        </Group>
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
              letter
              highlights={highlights}
            />
          </Paper>
          {!wide && (
            <Box mt="md">
              <ReviewChecklist
                caseId={caseId}
                items={review.data}
                loading={review.isPending}
                onSelectCitation={select}
                shownItemId={shown?.id ?? null}
                onShowInDraft={showInDraft}
                onFillIn={fillIn}
              />
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

      <LetterDetailsModal caseId={caseId} opened={details !== null} focus={details?.focus} onClose={() => setDetails(null)} />
    </Stack>
  )
}

function ExportMenu({ caseId, issues, letterItems }: { caseId: string; issues: number; letterItems: number }) {
  return (
    <Group gap="sm">
      {issues > 0 && (
        <Tooltip label="Contradictions and intake details to check before you send the letter. They are listed beside it and on the Issues page.">
          <Badge color="brick" variant="light">
            {issues} open {issues === 1 ? 'issue' : 'issues'}
          </Badge>
        </Tooltip>
      )}
      {letterItems > 0 && (
        <Tooltip label="Uncited statements, placeholders and scanned text to fix before you send the letter. They are listed beside it.">
          <Badge color="amber" variant="light">
            {letterItems} open letter {letterItems === 1 ? 'item' : 'items'}
          </Badge>
        </Tooltip>
      )}
      <Menu position="bottom-end">
        <Menu.Target>
          <Tooltip label="Download the letter. The file ends with the review items, for the attorney.">
            <Button leftSection={<IconDownload size={16} />} variant="light">
              Export
            </Button>
          </Tooltip>
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
  shownItemId?: string | null
  onShowInDraft?: (item: ReviewItem) => void
  /** Opens the letter details at a placeholder, so a placeholder item can be filled in from its card. */
  onFillIn?: (placeholder: string) => void
}

export function ReviewChecklist({ caseId, items, loading, onSelectCitation, shownItemId = null, onShowInDraft, onFillIn }: ReviewChecklistProps) {
  const [filter, setFilter] = useState<'open' | 'all'>('open')
  const update = useReviewUpdate(caseId)

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
              onShowInDraft={onShowInDraft}
              shownInDraft={shownItemId === item.id}
              actions={
                item.kind === 'Placeholder' && onFillIn ? (
                  <Button size="compact-xs" variant="light" leftSection={<IconPencil size={12} />} onClick={() => onFillIn(item.text)}>
                    Fill in
                  </Button>
                ) : undefined
              }
            />
          ))
        )}
      </Stack>
    </Paper>
  )
}
