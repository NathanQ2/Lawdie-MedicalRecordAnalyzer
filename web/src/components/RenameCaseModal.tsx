import { Alert, Button, Group, Modal, Stack, TextInput } from '@mantine/core'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useState, type FormEvent } from 'react'
import { api } from '../api/client'
import type { CaseListItem } from '../api/types'
import { caseKeys } from '../hooks/useCase'

export function RenameCaseModal({ item, onClose }: { item: CaseListItem | null; onClose: () => void }) {
  return (
    <Modal opened={item !== null} onClose={onClose} title="Rename case">
      {/* Keyed by case so the field starts from that case's current name. */}
      {item && <RenameCaseForm key={item.id} item={item} onClose={onClose} />}
    </Modal>
  )
}

function RenameCaseForm({ item, onClose }: { item: CaseListItem; onClose: () => void }) {
  const queryClient = useQueryClient()
  const [name, setName] = useState(item.name)
  const rename = useMutation({
    mutationFn: (newName: string) => api.renameCase(item.id, newName),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: caseKeys.all })
      onClose()
    },
  })

  const submit = (event: FormEvent) => {
    event.preventDefault()
    if (name.trim() && name.trim() !== item.name) {
      rename.mutate(name.trim())
    } else {
      onClose()
    }
  }

  return (
    <form onSubmit={submit}>
      <Stack>
        <TextInput
          label="Case name"
          value={name}
          onChange={(event) => setName(event.currentTarget.value)}
          maxLength={200}
          data-autofocus
          required
        />
        {rename.isError && <Alert color="brick">{rename.error.message}</Alert>}
        <Group justify="flex-end">
          <Button variant="default" onClick={onClose}>
            Cancel
          </Button>
          <Button type="submit" loading={rename.isPending} disabled={!name.trim()}>
            Rename
          </Button>
        </Group>
      </Stack>
    </form>
  )
}
