import { Alert, Anchor, Button, Center, Group, Loader, Modal, Stack, Text, Textarea, TextInput } from '@mantine/core'
import { useEffect, useId, useState, type FormEvent } from 'react'
import { Link } from 'react-router'
import type { LetterPlaceholder } from '../api/types'
import { usePlaceholders, usePlaceholderUpdate } from '../hooks/useCase'
import { isMultiline, placeholderLabel } from '../lib/letter'

interface LetterDetailsModalProps {
  caseId: string
  opened: boolean
  onClose: () => void
  /** The placeholder whose field takes focus, such as the one a review item asks to fill in. */
  focus?: string | null
}

/**
 * The details that fill the letter's bracketed placeholders, such as the claim number or the adjuster's name. Firm
 * details come from the firm profile unless this case gives its own; the date of loss comes from the matter details.
 */
export function LetterDetailsModal({ caseId, opened, onClose, focus = null }: LetterDetailsModalProps) {
  const placeholders = usePlaceholders(caseId, opened)

  return (
    <Modal opened={opened} onClose={onClose} title="Letter details" size="lg">
      {placeholders.isPending && (
        <Center py="lg">
          <Loader size="sm" />
        </Center>
      )}
      {placeholders.isError && <Alert color="brick">{placeholders.error.message}</Alert>}
      {placeholders.data && <LetterDetailsForm caseId={caseId} placeholders={placeholders.data} focus={focus} onClose={onClose} />}
    </Modal>
  )
}

interface LetterDetailsFormProps {
  caseId: string
  placeholders: LetterPlaceholder[]
  focus: string | null
  onClose: () => void
}

function LetterDetailsForm({ caseId, placeholders, focus, onClose }: LetterDetailsFormProps) {
  const update = usePlaceholderUpdate(caseId)
  const saved = Object.fromEntries(placeholders.map((item) => [item.placeholder, item.value ?? '']))
  // Only the attorney's changes are kept here, over the saved values, so a reload while typing keeps them.
  const [edits, setEdits] = useState<Record<string, string>>({})
  const valueOf = (placeholder: string) => edits[placeholder] ?? saved[placeholder]
  const formId = useId()
  const fieldId = (placeholder: string) => `${formId}${placeholder.replace(/\W+/g, '-')}`
  const focusId = focus ? fieldId(focus) : null

  // The dialog opens before the placeholders load, so the requested field is focused once the form appears.
  useEffect(() => {
    if (focusId) {
      document.getElementById(focusId)?.focus()
    }
  }, [focusId])

  const submit = (event: FormEvent) => {
    event.preventDefault()
    const changed = Object.fromEntries(
      placeholders
        .filter((item) => !item.fromMatter && valueOf(item.placeholder) !== saved[item.placeholder])
        .map((item) => [item.placeholder, valueOf(item.placeholder)]),
    )
    if (Object.keys(changed).length === 0) {
      onClose()
      return
    }
    update.mutate(changed, { onSuccess: onClose })
  }

  if (placeholders.length === 0) {
    return (
      <Text size="sm" c="dimmed" ta="center" py="md">
        The letter has no placeholders left to fill in.
      </Text>
    )
  }

  return (
    <form onSubmit={submit}>
      <Stack gap="sm">
        <Text size="sm" c="dimmed">
          These fill the bracketed placeholders in this letter. Anything left empty stays in brackets and is listed for review.
        </Text>
        {placeholders.map((item) => (
          <PlaceholderField
            key={item.placeholder}
            item={item}
            id={fieldId(item.placeholder)}
            value={valueOf(item.placeholder)}
            onChange={(value) => setEdits((current) => ({ ...current, [item.placeholder]: value }))}
            onLeave={onClose}
          />
        ))}
        {update.isError && <Alert color="brick">{update.error.message}</Alert>}
        <Group justify="flex-end">
          <Button variant="default" onClick={onClose}>
            Cancel
          </Button>
          <Button type="submit" loading={update.isPending}>
            Save
          </Button>
        </Group>
      </Stack>
    </form>
  )
}

interface PlaceholderFieldProps {
  item: LetterPlaceholder
  id: string
  value: string
  onChange: (value: string) => void
  onBlur?: () => void
  /** Called when the attorney follows the link to the firm profile, which leaves this view. */
  onLeave?: () => void
}

/**
 * One placeholder's field. The date of loss is shown but set in the matter details; a firm detail shows the firm
 * profile's value until the case gives its own.
 */
export function PlaceholderField({ item, id, value, onChange, onBlur, onLeave }: PlaceholderFieldProps) {
  const label = placeholderLabel(item.placeholder)
  if (item.fromMatter) {
    return <TextInput label={label} value={item.value ?? ''} placeholder="Not entered" description="Set in the matter details on the case overview." disabled />
  }

  const field = {
    id,
    label,
    value,
    onChange: (event: { currentTarget: { value: string } }) => onChange(event.currentTarget.value),
    onBlur,
    placeholder: item.firmValue ?? undefined,
    description: item.firmValue ? (
      <>
        From the{' '}
        <Anchor component={Link} to="/firm" size="xs" onClick={onLeave}>
          firm profile
        </Anchor>{' '}
        unless you fill it in here.
      </>
    ) : undefined,
    maxLength: 500,
  }
  return isMultiline(item.placeholder) ? <Textarea {...field} autosize minRows={2} maxRows={6} /> : <TextInput {...field} />
}
