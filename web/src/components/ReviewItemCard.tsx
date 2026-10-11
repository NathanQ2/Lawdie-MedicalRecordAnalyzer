import { Badge, Button, Card, Group, Stack, Text, Textarea, UnstyledButton } from '@mantine/core'
import { IconArrowBackUp, IconCheck, IconX } from '@tabler/icons-react'
import { useState } from 'react'
import type { Citation, ReviewItem, ReviewItemKind, ReviewStatus } from '../api/types'
import { citationKey, pageLabel, recordName } from '../lib/citations'

const kindLabels: Record<ReviewItemKind, { label: string; color: string }> = {
  Contradiction: { label: 'Contradiction', color: 'red' },
  UnsupportedStatement: { label: 'Uncited statement', color: 'yellow' },
  Placeholder: { label: 'Placeholder', color: 'grape' },
}

interface ReviewItemCardProps {
  item: ReviewItem
  onUpdate: (status: ReviewStatus, note: string | null) => void
  onSelectCitation: (citation: Citation) => void
  saving?: boolean
}

/**
 * One attorney review item: what to check, its sources, and resolve / dismiss with an optional note.
 */
export function ReviewItemCard({ item, onUpdate, onSelectCitation, saving = false }: ReviewItemCardProps) {
  const [note, setNote] = useState(item.note ?? '')
  const open = item.status === 'Open'
  const noteChanged = note.trim() !== (item.note ?? '')

  return (
    <Card withBorder radius="md" padding="sm" opacity={open ? 1 : 0.7}>
      <Stack gap="xs">
        <Group justify="space-between">
          <Badge size="sm" variant="light" color={kindLabels[item.kind].color}>
            {kindLabels[item.kind].label}
          </Badge>
          {!open && (
            <Badge size="sm" variant="outline" color={item.status === 'Resolved' ? 'green' : 'gray'}>
              {item.status}
            </Badge>
          )}
        </Group>

        <Text size="sm">{item.text}</Text>

        {item.citations.map((citation) => (
          <UnstyledButton key={citationKey(citation)} onClick={() => onSelectCitation(citation)}>
            <Text size="xs" c="indigo">
              {recordName(citation.filePath)}
              {pageLabel(citation) && ` · ${pageLabel(citation)}`}: “{citation.citedText.replace(/\s+/g, ' ').trim()}”
            </Text>
          </UnstyledButton>
        ))}

        <Textarea
          size="xs"
          placeholder="Note (optional), e.g. how it was resolved"
          autosize
          minRows={1}
          value={note}
          onChange={(event) => setNote(event.currentTarget.value)}
          aria-label="Review note"
        />

        <Group gap="xs" justify="flex-end">
          {open ? (
            <>
              <Button size="compact-xs" variant="light" color="gray" leftSection={<IconX size={12} />} loading={saving} onClick={() => onUpdate('Dismissed', note.trim() || null)}>
                Dismiss
              </Button>
              <Button size="compact-xs" variant="light" color="green" leftSection={<IconCheck size={12} />} loading={saving} onClick={() => onUpdate('Resolved', note.trim() || null)}>
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
              <Button size="compact-xs" variant="subtle" leftSection={<IconArrowBackUp size={12} />} loading={saving} onClick={() => onUpdate('Open', note.trim() || null)}>
                Reopen
              </Button>
            </>
          )}
        </Group>
      </Stack>
    </Card>
  )
}
