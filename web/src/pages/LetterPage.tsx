import { Alert, Anchor, Badge, Box, Button, Card, Center, Drawer, Grid, Group, Loader, Menu, Paper, SegmentedControl, Stack, Text, Textarea, TextInput, Title, Tooltip } from '@mantine/core'
import { modals } from '@mantine/modals'
import { useMediaQuery } from '@mantine/hooks'
import { IconDownload, IconPencil, IconRun } from '../components/icons'
import { useEffect, useRef, useState, type FormEvent, type ReactNode } from 'react'
import { Link, useParams } from 'react-router'
import { api } from '../api/client'
import type { Citation, ReviewItem } from '../api/types'
import { EditableLetter } from '../components/EditableLetter'
import { JobStatusCard } from '../components/JobStatusCard'
import { LetterDetailsModal } from '../components/LetterDetailsModal'
import { ReviewItemCard } from '../components/ReviewItemCard'
import { SourceViewer } from '../components/SourceViewer'
import { useCase, useDraft, usePlaceholderUpdate, useReview, useReviewUpdate } from '../hooks/useCase'
import { useJobActions, useNow } from '../hooks/useJob'
import { citationKey, citationNumbering } from '../lib/citations'
import { isJobActive } from '../lib/format'
import { firmPlaceholders, isMultiline, placeholderLabel } from '../lib/letter'
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
  // The review item found from the letter; the count makes clicking the same placeholder again scroll to it again.
  const [found, setFound] = useState<{ id: string; count: number } | null>(null)
  const wide = useMediaQuery('(min-width: 75em)')
  // The letter details dialog, for changing values already filled in; closed when null.
  const [details, setDetails] = useState<{ focus: string | null } | null>(null)
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
          {draft.data &&
            (caseQuery.data?.letterEditedAt
              ? ' This replaces the current letter, including your edits to its text. Your review decisions and letter details stay.'
              : ' This replaces the current letter. Your review decisions and letter details stay.')}
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
  const citations = segments.flatMap((segment) => segment.citations)
  // Counted the same way as the sidebar: issues are checked before drafting, letter items are fixed in the letter.
  const openIssues = openIssueCount(review.data)
  const openLetterItems = openLetterItemCount(review.data)

  // The highlighted item is looked up again so its ranges follow the latest review data.
  const shown = review.data?.find((item) => item.id === shownItem?.id) ?? null
  const highlights = (shown?.draftRanges ?? []).map(({ start, length }): [number, number] => [start, start + length])
  const showInDraft = (item: ReviewItem) => setShownItem(shown?.id === item.id ? null : item)

  // Clicking a placeholder or an uncited sentence in the letter finds its review item and shows where it is.
  const find = (item: ReviewItem | undefined) => {
    if (!item) {
      return
    }
    setPanel('review')
    setShownItem(item)
    setFound((current) => ({ id: item.id, count: (current?.count ?? 0) + 1 }))
  }
  const findPlaceholder = (placeholder: string) =>
    find(review.data?.find((item) => item.kind === 'Placeholder' && item.text === placeholder))
  // The text clicked may be only part of the sentence, where a highlight or citation splits it.
  const findUnsupported = (text: string) =>
    find(review.data?.find((item) => item.kind === 'UnsupportedStatement' && item.text.includes(text.trim())))

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
                fillPlaceholders
                found={found}
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
            Click a number to see its source. Underlined sentences have no citation. Fill in each [BRACKETED] placeholder before sending, and use the
            pencil beside a paragraph to edit it.
          </Text>
        </Stack>
        <ExportMenu
          caseId={caseId}
          issues={openIssues}
          letterItems={openLetterItems}
          details={
            <Tooltip label="Fill in the claim number, adjuster and other details the documents don't give.">
              <Button variant="default" leftSection={<IconPencil size={16} />} onClick={() => setDetails({ focus: null })}>
                Edit letter details
              </Button>
            </Tooltip>
          }
        />
      </Group>

      <Grid gap="md">
        <Grid.Col span={wide ? 7 : 12}>
          <Paper withBorder p="lg" radius="md">
            <EditableLetter
              caseId={caseId}
              draft={draft.data}
              numberOf={citationNumbering(citations)}
              selectedKey={selected ? citationKey(selected) : null}
              onSelect={select}
              highlights={highlights}
              editable={!isJobActive(job)}
              onPlaceholderClick={findPlaceholder}
              onUnsupportedClick={findUnsupported}
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
                fillPlaceholders
                found={found}
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

interface ExportMenuProps {
  caseId: string
  issues: number
  letterItems: number
  /** The button that opens the letter details, shown between the counts and Export. */
  details: ReactNode
}

function ExportMenu({ caseId, issues, letterItems, details }: ExportMenuProps) {
  return (
    <Group gap="sm">
      {letterItems > 0 && (
        <Tooltip label="Uncited statements, placeholders and scanned text to fix before you send the letter. They are listed beside it.">
          <Badge color="amber" variant="light">
            {letterItems} open letter {letterItems === 1 ? 'item' : 'items'}
          </Badge>
        </Tooltip>
      )}
      {issues > 0 && (
        <Tooltip label="Contradictions and intake details to check before you send the letter. They are listed beside it and on the Issues page.">
          <Badge color="brick" variant="light">
            {issues} open {issues === 1 ? 'issue' : 'issues'}
          </Badge>
        </Tooltip>
      )}
      {details}
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
  /** Lets placeholder items be filled in from their cards. */
  fillPlaceholders?: boolean
  /** An item to scroll to and mark, such as the one for a placeholder clicked in the letter. */
  found?: { id: string; count: number } | null
}

export function ReviewChecklist({ caseId, items, loading, onSelectCitation, shownItemId = null, onShowInDraft, fillPlaceholders = false, found = null }: ReviewChecklistProps) {
  const [filter, setFilter] = useState<'open' | 'all'>('open')
  const update = useReviewUpdate(caseId)
  const handled = useRef(0)
  // The find that the attorney last changed the filter after; a newer find may override it.
  const [filterSetAt, setFilterSetAt] = useState(0)
  const foundItem = found ? items?.find((item) => item.id === found.id) : undefined
  // A resolved or dismissed item is only listed under All, so finding one shows All until the filter is changed.
  const showAll = filter === 'all' || (found !== null && found.count > filterSetAt && foundItem !== undefined && foundItem.status !== 'Open')

  useEffect(() => {
    if (!found || handled.current === found.count) {
      return
    }
    handled.current = found.count
    const card = document.querySelector<HTMLElement>(`[data-review-item="${CSS.escape(found.id)}"]`)
    if (!card) {
      return
    }
    // The letter scrolls smoothly to the item's sentences at the same time, and a second smooth scroll would cancel
    // it or be cancelled, so where the list scrolls on its own it jumps straight to the card.
    const list = card.closest<HTMLElement>('[data-review-list]')
    if (list && list.scrollHeight > list.clientHeight) {
      const offset = card.getBoundingClientRect().top - list.getBoundingClientRect().top
      list.scrollTop += offset - (list.clientHeight - card.offsetHeight) / 2
    } else {
      card.scrollIntoView({ block: 'center', behavior: 'smooth' })
    }
    card.querySelector<HTMLElement>('[data-fill]')?.focus({ preventScroll: true })
  }, [found])

  if (loading || !items) {
    return (
      <Center py="lg">
        <Loader size="sm" />
      </Center>
    )
  }

  const done = items.filter((item) => item.status !== 'Open').length
  const shown = showAll ? items : items.filter((item) => item.status === 'Open')

  return (
    <Paper withBorder radius="md" p="md" h="100%" style={{ overflowY: 'auto' }} data-review-list>
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
            value={showAll ? 'all' : 'open'}
            onChange={(value) => {
              setFilter(value as 'open' | 'all')
              setFilterSetAt(found?.count ?? 0)
            }}
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
              focused={found?.id === item.id}
              body={item.kind === 'Placeholder' && fillPlaceholders ? <PlaceholderFill caseId={caseId} placeholder={item.text} /> : undefined}
            />
          ))
        )}
      </Stack>
    </Paper>
  )
}

/**
 * A field in a placeholder's review item to fill it in. The value fills every place the placeholder appears in the
 * letter, which takes the item off the open list; Edit letter details changes it later.
 */
function PlaceholderFill({ caseId, placeholder }: { caseId: string; placeholder: string }) {
  const update = usePlaceholderUpdate(caseId)
  const [value, setValue] = useState('')

  if (placeholder === '[DATE OF LOSS]') {
    return (
      <Text size="xs" c="dimmed">
        Set the date of loss in the matter details on the case overview.
      </Text>
    )
  }

  const submit = (event: FormEvent) => {
    event.preventDefault()
    if (value.trim()) {
      update.mutate({ [placeholder]: value })
    }
  }

  const label = `Value for ${placeholder}`
  return (
    <form onSubmit={submit}>
      <Stack gap={4}>
        <Group gap="xs" align="flex-start" wrap="nowrap">
          {isMultiline(placeholder) ? (
            <Textarea data-fill size="xs" flex={1} autosize minRows={2} maxLength={500} placeholder={placeholderLabel(placeholder)} aria-label={label} value={value} onChange={(event) => setValue(event.currentTarget.value)} />
          ) : (
            <TextInput data-fill size="xs" flex={1} maxLength={500} placeholder={placeholderLabel(placeholder)} aria-label={label} value={value} onChange={(event) => setValue(event.currentTarget.value)} />
          )}
          <Button type="submit" size="xs" variant="light" loading={update.isPending} disabled={!value.trim()}>
            Fill in
          </Button>
        </Group>
        {firmPlaceholders.includes(placeholder) && (
          <Text size="xs" c="dimmed">
            Fills this letter only. Set it in the{' '}
            <Anchor component={Link} to="/firm" size="xs">
              firm profile
            </Anchor>{' '}
            to fill every letter.
          </Text>
        )}
        {update.isError && <Alert color="brick">{update.error.message}</Alert>}
      </Stack>
    </form>
  )
}
