import { Alert, Button, Center, Group, Loader, Paper, Stack, Text, Textarea, TextInput, Title } from '@mantine/core'
import { notifications } from '@mantine/notifications'
import { useState, type FormEvent } from 'react'
import type { FirmProfile } from '../api/types'
import { useFirmProfile, useFirmProfileUpdate } from '../hooks/useCase'

/** The firm's details, entered once, for the letterhead and signature of every letter. */
export function FirmPage() {
  const profile = useFirmProfile()

  return (
    <Stack gap="lg" maw={640}>
      <Stack gap={4}>
        <Title order={2}>Firm profile</Title>
        <Text size="sm" c="dimmed">
          These details fill the letterhead and signature of every letter. A case can use different ones in its letter details.
        </Text>
      </Stack>
      {profile.isPending && (
        <Center py="xl">
          <Loader />
        </Center>
      )}
      {profile.isError && <Alert color="brick" title="Could not load the firm profile">{profile.error.message}</Alert>}
      {profile.data && <FirmForm profile={profile.data} />}
    </Stack>
  )
}

const fields: { key: keyof FirmProfile; label: string; multiline?: boolean; type?: string }[] = [
  { key: 'firmName', label: 'Firm name' },
  { key: 'address', label: 'Address', multiline: true },
  { key: 'phone', label: 'Phone', type: 'tel' },
  { key: 'email', label: 'Email', type: 'email' },
  { key: 'attorneyName', label: 'Attorney who signs the letters' },
]

function FirmForm({ profile }: { profile: FirmProfile }) {
  const update = useFirmProfileUpdate()
  // Only the attorney's changes are kept here, over the saved profile, so a reload while typing keeps them.
  const [edits, setEdits] = useState<Partial<FirmProfile>>({})

  const submit = (event: FormEvent) => {
    event.preventDefault()
    update.mutate(
      { ...profile, ...edits },
      {
        onSuccess: () => {
          setEdits({})
          notifications.show({ color: 'sage', message: 'Saved the firm profile.' })
        },
      },
    )
  }

  return (
    <Paper withBorder radius="md" p="lg">
      <form onSubmit={submit}>
        <Stack gap="sm">
          {fields.map(({ key, label, multiline, type }) => {
            const field = {
              label,
              value: edits[key] ?? profile[key] ?? '',
              onChange: (event: { currentTarget: { value: string } }) => {
                const value = event.currentTarget.value
                setEdits((current) => ({ ...current, [key]: value }))
              },
              maxLength: 300,
            }
            return multiline ? (
              <Textarea key={key} {...field} autosize minRows={2} maxRows={5} />
            ) : (
              <TextInput key={key} {...field} type={type} />
            )
          })}
          {update.isError && <Alert color="brick">{update.error.message}</Alert>}
          <Group justify="flex-end">
            <Button type="submit" loading={update.isPending}>
              Save
            </Button>
          </Group>
        </Stack>
      </form>
    </Paper>
  )
}
