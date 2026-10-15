import { ActionIcon, Alert, Button, Group, Stack, Text, Textarea, Tooltip } from '@mantine/core'
import { useState, type KeyboardEvent } from 'react'
import type { Citation, DemandDraft } from '../api/types'
import { useLetterEdit, useUnfilledDraft } from '../hooks/useCase'
import { citationsIn, paragraphs, rangesIn, textForEditing, type Paragraph } from '../lib/letter'
import { AnnotatedText } from './AnnotatedText'
import classes from './EditableLetter.module.css'
import { IconPencil } from './icons'

interface EditableLetterProps {
  caseId: string
  /** The letter with its placeholders filled in, as it is shown and sent. */
  draft: DemandDraft
  numberOf: (citation: Citation) => number
  selectedKey: string | null
  onSelect: (citation: Citation) => void
  /** [start, end) ranges of the letter to highlight, such as the sentences a review item affects. */
  highlights: [number, number][]
  /** Off while a job runs, since a new draft would replace the edit. */
  editable: boolean
  onPlaceholderClick?: (placeholder: string) => void
  onUnsupportedClick?: (text: string) => void
}

interface Editing {
  index: number
  /** Where the paragraph is in the letter as drafted, and what it read when editing began. */
  start: number
  original: string
  text: string
}

/**
 * The letter, a paragraph at a time, each with a button to edit it. An edit is made on the letter as drafted, with
 * its placeholders still in brackets (they are filled in when the letter is shown) and each source's number after
 * the statement it supports. The server keeps those sources attached and lists any sentence left without one.
 */
export function EditableLetter({ caseId, draft, numberOf, selectedKey, onSelect, highlights, editable, onPlaceholderClick, onUnsupportedClick }: EditableLetterProps) {
  const unfilled = useUnfilledDraft(caseId, editable)
  const edit = useLetterEdit(caseId)
  const [editing, setEditing] = useState<Editing | null>(null)

  const text = draft.segments.map((segment) => segment.text).join('')
  const blocks = paragraphs(text)
  const unfilledText = unfilled.data?.segments.map((segment) => segment.text).join('') ?? ''
  const unfilledBlocks = unfilled.data ? paragraphs(unfilledText) : null
  // Filled-in values never contain a blank line, so both forms have the same paragraphs.
  const canEdit = editable && unfilledBlocks?.length === blocks.length
  const firstHighlight = blocks.findIndex((block) => rangesIn(highlights, block).length > 0)
  const allCitations = draft.segments.flatMap((segment) => segment.citations)

  const startEditing = (index: number) => {
    if (!unfilled.data || !unfilledBlocks) {
      return
    }
    const block: Paragraph = unfilledBlocks[index]
    edit.reset()
    setEditing({
      index,
      start: block.start,
      original: unfilledText.slice(block.start, block.end),
      text: textForEditing(unfilled.data.segments, block, numberOf),
    })
  }

  const save = (current: Editing, text: string) => {
    if (text === current.text) {
      setEditing(null)
      return
    }
    const sources = Object.fromEntries(allCitations.map((citation) => [numberOf(citation), citation]))
    edit.mutate({ start: current.start, original: current.original, text, sources }, { onSuccess: () => setEditing(null) })
  }

  return (
    <div className={classes.letter}>
      {blocks.map((block, index) =>
        editing?.index === index ? (
          <ParagraphEditor
            key={`edit-${index}`}
            initial={editing.text}
            saving={edit.isPending}
            error={edit.error?.message ?? null}
            onCancel={() => setEditing(null)}
            onSave={(value) => save(editing, value)}
          />
        ) : (
          <div key={`${index}-${block.start}`} className={classes.paragraph}>
            <AnnotatedText
              text={text.slice(block.start, block.end)}
              citations={citationsIn(draft.segments, block)}
              unsupportedStatements={draft.unsupportedStatements}
              numberOf={numberOf}
              selectedKey={selectedKey}
              onSelect={onSelect}
              letter
              highlights={rangesIn(highlights, block)}
              scrollToHighlight={index === firstHighlight}
              onPlaceholderClick={onPlaceholderClick}
              onUnsupportedClick={onUnsupportedClick}
            />
            {canEdit && editing === null && (
              <Tooltip label="Edit this paragraph">
                <ActionIcon className={classes.edit} variant="subtle" color="gray" size="sm" aria-label="Edit this paragraph" onClick={() => startEditing(index)}>
                  <IconPencil size={14} />
                </ActionIcon>
              </Tooltip>
            )}
          </div>
        ),
      )}
    </div>
  )
}

interface ParagraphEditorProps {
  initial: string
  saving: boolean
  error: string | null
  onCancel: () => void
  onSave: (text: string) => void
}

function ParagraphEditor({ initial, saving, error, onCancel, onSave }: ParagraphEditorProps) {
  const [value, setValue] = useState(initial)

  const onKeyDown = (event: KeyboardEvent<HTMLTextAreaElement>) => {
    if (event.key === 'Escape') {
      onCancel()
    } else if (event.key === 'Enter' && (event.metaKey || event.ctrlKey)) {
      onSave(value)
    }
  }

  return (
    <Stack gap="xs" className={classes.editor}>
      <Textarea
        value={value}
        onChange={(event) => setValue(event.currentTarget.value)}
        onKeyDown={onKeyDown}
        autosize
        minRows={2}
        autoFocus
        aria-label="Paragraph text"
        classNames={{ input: classes.input }}
      />
      <Text size="xs" c="dimmed">
        Numbers in brackets, such as [3], are sources. Keep each one right after the sentence it supports, or delete it if the sentence no longer says
        what the source does. A sentence without a source is listed for review. Fill in placeholders from the review list, or with Edit letter details.
      </Text>
      {error && <Alert color="brick">{error}</Alert>}
      <Group gap="xs" justify="flex-end">
        <Button size="xs" variant="default" onClick={onCancel}>
          Cancel
        </Button>
        <Button size="xs" loading={saving} onClick={() => onSave(value)}>
          Save
        </Button>
      </Group>
    </Stack>
  )
}
