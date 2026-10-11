import { Alert, Badge, Button, Center, Group, Loader, Modal, Paper, Stack, Table, Text, TextInput, Title } from '@mantine/core'
import { useDisclosure } from '@mantine/hooks'
import { IconPlus } from '@tabler/icons-react'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useState, type FormEvent } from 'react'
import { useNavigate } from 'react-router'
import { api } from '../api/client'
import { caseKeys, useCases } from '../hooks/useCase'
import { formatDateTime, isJobActive, jobStateColors } from '../lib/format'

export function CasesPage() {
  const cases = useCases()
  const navigate = useNavigate()
  const [creating, { open, close }] = useDisclosure(false)

  return (
    <Stack gap="lg">
      <Group justify="space-between">
        <Title order={2}>Cases</Title>
        <Button leftSection={<IconPlus size={16} />} onClick={open}>
          New case
        </Button>
      </Group>

      {cases.isPending && (
        <Center py="xl">
          <Loader />
        </Center>
      )}
      {cases.isError && <Alert color="red" title="Could not load cases">{cases.error.message}</Alert>}

      {cases.data && cases.data.length === 0 && (
        <Paper withBorder p="xl" radius="md">
          <Stack align="center" gap="xs">
            <Text fw={500}>No cases yet</Text>
            <Text c="dimmed" size="sm">
              Create a case, upload its medical records, and run ingest to analyze them.
            </Text>
          </Stack>
        </Paper>
      )}

      {cases.data && cases.data.length > 0 && (
        <Paper withBorder radius="md">
          <Table highlightOnHover verticalSpacing="sm">
            <Table.Thead>
              <Table.Tr>
                <Table.Th>Case</Table.Th>
                <Table.Th>Records</Table.Th>
                <Table.Th>Status</Table.Th>
                <Table.Th>Updated</Table.Th>
              </Table.Tr>
            </Table.Thead>
            <Table.Tbody>
              {cases.data.map((item) => (
                <Table.Tr key={item.id} style={{ cursor: 'pointer' }} onClick={() => navigate(`/cases/${item.id}`)}>
                  <Table.Td>
                    <Text fw={500}>{item.name}</Text>
                  </Table.Td>
                  <Table.Td>{item.recordCount}</Table.Td>
                  <Table.Td>
                    <Group gap="xs">
                      {isJobActive(item.job) && item.job && (
                        <Badge color={jobStateColors[item.job.state]} variant="light">
                          {item.job.kind} {item.job.state.toLowerCase()}
                        </Badge>
                      )}
                      {item.hasAnalysis && <Badge variant="light" color="teal">Analyzed</Badge>}
                      {item.hasDraft && <Badge variant="light" color="violet">Drafted</Badge>}
                      {!item.hasAnalysis && !isJobActive(item.job) && <Badge variant="light" color="gray">New</Badge>}
                    </Group>
                  </Table.Td>
                  <Table.Td>{formatDateTime(item.updatedAt)}</Table.Td>
                </Table.Tr>
              ))}
            </Table.Tbody>
          </Table>
        </Paper>
      )}

      <NewCaseModal opened={creating} onClose={close} onCreated={(caseId) => navigate(`/cases/${caseId}/records`)} />
    </Stack>
  )
}

interface NewCaseModalProps {
  opened: boolean
  onClose: () => void
  onCreated: (caseId: string) => void
}

function NewCaseModal({ opened, onClose, onCreated }: NewCaseModalProps) {
  const queryClient = useQueryClient()
  const [name, setName] = useState('')
  const create = useMutation({
    mutationFn: api.createCase,
    onSuccess: async (created) => {
      await queryClient.invalidateQueries({ queryKey: caseKeys.all })
      setName('')
      onClose()
      onCreated(created.id)
    },
  })

  const submit = (event: FormEvent) => {
    event.preventDefault()
    if (name.trim()) {
      create.mutate(name.trim())
    }
  }

  return (
    <Modal opened={opened} onClose={onClose} title="New case">
      <form onSubmit={submit}>
        <Stack>
          <TextInput
            label="Case name"
            placeholder="e.g. Ellis v. Cascade Freight"
            value={name}
            onChange={(event) => setName(event.currentTarget.value)}
            maxLength={200}
            data-autofocus
            required
          />
          {create.isError && <Alert color="red">{create.error.message}</Alert>}
          <Group justify="flex-end">
            <Button variant="default" onClick={onClose}>
              Cancel
            </Button>
            <Button type="submit" loading={create.isPending} disabled={!name.trim()}>
              Create
            </Button>
          </Group>
        </Stack>
      </form>
    </Modal>
  )
}
