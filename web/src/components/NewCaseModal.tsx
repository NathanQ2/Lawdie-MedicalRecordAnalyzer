import { Modal } from '@mantine/core'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { api } from '../api/client'
import type { MatterInput } from '../api/types'
import { caseKeys } from '../hooks/useCase'
import { isoDate } from '../lib/format'
import { MatterForm } from './MatterForm'

interface NewCaseModalProps {
  opened: boolean
  onClose: () => void
  onCreated: (caseId: string) => void
}

/** Opens a new case from its intake details, from the case list or the sidebar. */
export function NewCaseModal({ opened, onClose, onCreated }: NewCaseModalProps) {
  const queryClient = useQueryClient()
  const [today] = useState(() => isoDate(new Date()))
  const create = useMutation({
    mutationFn: (matter: MatterInput) => api.createCase(matter),
    onSuccess: async (created) => {
      await queryClient.invalidateQueries({ queryKey: caseKeys.all })
      onClose()
      onCreated(created.id)
    },
  })

  return (
    <Modal opened={opened} onClose={onClose} title="New case" size="lg">
      {/* Mounted only while open, so each new matter starts from an empty form. */}
      {opened && (
        <MatterForm
          today={today}
          submitLabel="Create"
          pending={create.isPending}
          error={create.isError ? create.error.message : null}
          onSubmit={(matter) => create.mutate(matter)}
          onCancel={onClose}
        />
      )}
    </Modal>
  )
}
