import { Paper, Stack, Text, Title } from '@mantine/core'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { useNavigate, useParams } from 'react-router'
import { api } from '../api/client'
import type { MatterInput } from '../api/types'
import { MatterForm } from '../components/MatterForm'
import { caseKeys, useCase } from '../hooks/useCase'
import { isoDate } from '../lib/format'

export function MatterTab() {
  const { caseId = '' } = useParams()
  const caseQuery = useCase(caseId)
  const queryClient = useQueryClient()
  const navigate = useNavigate()
  const [today] = useState(() => isoDate(new Date()))

  const save = useMutation({
    mutationFn: (matter: MatterInput) => api.updateMatter(caseId, matter),
    onSuccess: async (updated) => {
      queryClient.setQueryData(caseKeys.detail(caseId), updated)
      // The letter and its review items are served with the date of loss filled in, so they reload too.
      await queryClient.invalidateQueries({ queryKey: caseKeys.all })
      navigate(`/cases/${caseId}/records`)
    },
  })

  if (!caseQuery.data) {
    return null
  }

  const detail = caseQuery.data
  return (
    <Paper withBorder p="lg" radius="lg">
      <Stack>
        <Stack gap={4}>
          <Title order={3}>Matter details</Title>
          <Text size="sm" c="dimmed">
            The date of loss goes into the letter's heading and opening paragraph. Changing it here updates the letter without
            drafting it again.
          </Text>
        </Stack>
        {/* Keyed by case so polling while a job runs never resets what is being typed. */}
        <MatterForm
          key={detail.id}
          initial={{ name: detail.name, dateOfLoss: detail.dateOfLoss, jurisdiction: detail.jurisdiction, county: detail.county }}
          today={today}
          submitLabel="Save and continue"
          pending={save.isPending}
          error={save.isError ? save.error.message : null}
          onSubmit={(matter) => save.mutate(matter)}
        />
      </Stack>
    </Paper>
  )
}
