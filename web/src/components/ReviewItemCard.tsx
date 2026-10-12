import { Badge, Button, Card, Group, Stack, Text, Textarea, UnstyledButton } from '@mantine/core'
import { IconCheck, IconClose, IconLocate, IconUndo } from './icons'
import { useState } from 'react'
import type { Citation, ReviewItem, ReviewItemKind, ReviewStatus } from '../api/types'
import { citationKey, hasTwoQuotes, pageLabel, recordName } from '../lib/citations'

const kindLabels: Record<ReviewItemKind, { label: string; color: string }> = {
  Contradiction: { label: 'Contradiction', color: 'brick' },
  UnsupportedStatement: { label: 'Uncited statement', color: 'amber' },
  Placeholder: { label: 'Placeholder', color: 'slate' },
  RecognizedQuote: { label: 'Scanned text', color: 'slate' },
}

interface ReviewItemCardProps {
  item: ReviewItem
  onUpdate: (status: ReviewStatus, note: string | null) => void
  onSelectCitation: (citation: Citation) => void
  /** Highlights the item's sentences in the letter. Without it, the letter links are not shown. */
  onShowInDraft?: (item: ReviewItem) => void
  /** Whether this item's sentences are the ones highlighted in the letter. */
  shownInDraft?: boolean
  saving?: boolean
}

/**
 * One attorney review item: what to check, its sources, and resolve / dismiss with an optional note.
 */
export function ReviewItemCard({ item, onUpdate, onSelectCitation, onShowInDraft, shownInDraft = false, saving = false }: ReviewItemCardProps) {
  const [note, setNote] = useState(item.note ?? '')
  const open = item.status === 'Open'
  const noteChanged = note.trim() !== (item.note ?? '')
  // A contradiction without two different quotes is the reviewer's own description, not something the documents show.
  const unverified = item.kind === 'Contradiction' && !hasTwoQuotes(item.citations)
  const places = item.draftRanges.length

  return (
    <Card withBorder radius="md" padding="sm" opacity={open ? 1 : 0.7}>
      <Stack gap="xs">
        <Group justify="space-between">
          <Group gap={4}>
            <Badge size="sm" variant="light" color={kindLabels[item.kind].color}>
              {kindLabels[item.kind].label}
            </Badge>
            {unverified && (
              <Badge size="sm" variant="light" color="gray">
                Unverified
              </Badge>
            )}
          </Group>
          {!open && (
            <Badge size="sm" variant="outline" color={item.status === 'Resolved' ? 'sage' : 'gray'}>
              {item.status}
            </Badge>
          )}
        </Group>

        <Text size="sm">{item.text}</Text>

        {item.citations.map((citation) => (
          <UnstyledButton key={citationKey(citation)} onClick={() => onSelectCitation(citation)}>
            <Text size="xs" c="slate">
              {recordName(citation.filePath)}
              {pageLabel(citation) && ` · ${pageLabel(citation)}`}: "{citation.citedText.replace(/\s+/g, ' ').trim()}"
            </Text>
          </UnstyledButton>
        ))}

        {onShowInDraft &&
          (places > 0 ? (
            <Button
              size="compact-xs"
              variant={shownInDraft ? 'filled' : 'light'}
              color="amber"
              w="fit-content"
              leftSection={<IconLocate size={12} />}
              onClick={() => onShowInDraft(item)}
            >
              {shownInDraft ? 'Shown in letter' : `Show in letter (${places} ${places === 1 ? 'place' : 'places'})`}
            </Button>
          ) : (
            item.kind === 'Contradiction' && (
              <Text size="xs" c="dimmed">
                No cited sentence in the letter uses these quotes. Check the letter for other mentions.
              </Text>
            )
          ))}

        <Textarea
          size="xs"
          placeholder="Add a note, such as how you resolved it"
          autosize
          minRows={1}
          value={note}
          onChange={(event) => setNote(event.currentTarget.value)}
          aria-label="Review note"
        />

        <Group gap="xs" justify="flex-end">
          {open ? (
            <>
              <Button size="compact-xs" variant="light" color="gray" leftSection={<IconClose size={12} />} loading={saving} onClick={() => onUpdate('Dismissed', note.trim() || null)}>
                Dismiss
              </Button>
              <Button size="compact-xs" variant="light" color="sage" leftSection={<IconCheck size={12} />} loading={saving} onClick={() => onUpdate('Resolved', note.trim() || null)}>
                Resolve
              </Button>
            </>
          ) : (
            <>
              {noteChanged && (
                <Button size="compact-xs" variant="light" loading={saving} onClick={() => onUpdate(item.status, note.trim() || null)}>
                  Save note
                </Button>
              )}
              <Button size="compact-xs" variant="subtle" leftSection={<IconUndo size={12} />} loading={saving} onClick={() => onUpdate('Open', note.trim() || null)}>
                Reopen
              </Button>
            </>
          )}
        </Group>
      </Stack>
    </Card>
  )
}
