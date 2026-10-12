import { ActionIcon, Alert, Badge, Box, Button, Center, Group, Loader, Modal, Paper, Stack, Table, Text, TextInput, Title, Tooltip } from '@mantine/core'
import { useDisclosure } from '@mantine/hooks'
import { modals } from '@mantine/modals'
import { notifications } from '@mantine/notifications'
import { IconMore, IconPlus } from '../components/icons'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useState, type FormEvent, type MouseEvent } from 'react'
import { useNavigate } from 'react-router'
import { api } from '../api/client'
import type { CaseListItem, MatterInput } from '../api/types'
import { CaseActionsMenu, type CaseAction } from '../components/CaseActionsMenu'
import { MatterForm } from '../components/MatterForm'
import { caseKeys, useCases } from '../hooks/useCase'
import { formatDateOnly, formatDateTime, isJobActive, isoDate, jobKindLabels, jobStateColors } from '../lib/format'

export function CasesPage() {
  const cases = useCases()
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const [creating, { open, close }] = useDisclosure(false)
  const [renaming, setRenaming] = useState<CaseListItem | null>(null)
  // A right-click opens the case's menu at the cursor.
  const [contextMenu, setContextMenu] = useState<{ item: CaseListItem; x: number; y: number } | null>(null)

  const remove = useMutation({
    mutationFn: (item: CaseListItem) => api.deleteCase(item.id),
    onSuccess: async (_, item) => {
      await queryClient.invalidateQueries({ queryKey: caseKeys.all })
      notifications.show({ color: 'sage', message: `Deleted "${item.name}".` })
    },
    onError: (error) => notifications.show({ color: 'brick', title: 'Could not delete the case', message: error.message }),
  })

  const runAction = (item: CaseListItem, action: CaseAction) => {
    setContextMenu(null)
    switch (action) {
      case 'open':
        navigate(`/cases/${item.id}`)
        break
      case 'documents':
        navigate(`/cases/${item.id}/records`)
        break
      case 'review':
        navigate(`/cases/${item.id}/review`)
        break
      case 'letter':
        navigate(`/cases/${item.id}/letter`)
        break
      case 'rename':
        setRenaming(item)
        break
      case 'delete':
        modals.openConfirmModal({
          title: `Delete "${item.name}"?`,
          children: <Text size="sm">This deletes the case, its documents and all results. You can't undo this.</Text>,
          labels: { confirm: 'Delete case', cancel: 'Keep it' },
          confirmProps: { color: 'brick' },
          onConfirm: () => remove.mutate(item),
        })
        break
    }
  }

  const openContextMenu = (event: MouseEvent, item: CaseListItem) => {
    event.preventDefault()
    setContextMenu({ item, x: event.clientX, y: event.clientY })
  }

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
      {cases.isError && <Alert color="brick" title="Could not load cases">{cases.error.message}</Alert>}

      {cases.data && cases.data.length === 0 && (
        <Paper withBorder p="xl" radius="md">
          <Stack align="center" gap="xs">
            <Text fw={500}>No cases yet</Text>
            <Text c="dimmed" size="sm">
              Create a case, then add its medical documents.
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
                <Table.Th>Date of loss</Table.Th>
                <Table.Th>Documents</Table.Th>
                <Table.Th>Status</Table.Th>
                <Table.Th>Updated</Table.Th>
                <Table.Th aria-label="Options" w={48} />
              </Table.Tr>
            </Table.Thead>
            <Table.Tbody>
              {cases.data.map((item) => (
                <Table.Tr
                  key={item.id}
                  style={{ cursor: 'pointer' }}
                  onClick={() => navigate(`/cases/${item.id}`)}
                  onContextMenu={(event) => openContextMenu(event, item)}
                >
                  <Table.Td>
                    <Text fw={500}>{item.name}</Text>
                  </Table.Td>
                  <Table.Td>{formatDateOnly(item.dateOfLoss)}</Table.Td>
                  <Table.Td>{item.recordCount}</Table.Td>
                  <Table.Td>
                    <Group gap="xs">
                      {isJobActive(item.job) && item.job && (
                        <Badge color={jobStateColors[item.job.state]} variant="light">
                          {jobKindLabels[item.job.kind]} {item.job.state.toLowerCase()}
                        </Badge>
                      )}
                      {item.hasAnalysis && <Badge variant="light" color="sage">Analyzed</Badge>}
                      {item.hasDraft && <Badge variant="light" color="slate">Drafted</Badge>}
                      {!item.hasAnalysis && !isJobActive(item.job) && <Badge variant="light" color="gray">New</Badge>}
                    </Group>
                  </Table.Td>
                  <Table.Td>{formatDateTime(item.updatedAt)}</Table.Td>
                  <Table.Td onClick={(event) => event.stopPropagation()}>
                    <CaseActionsMenu
                      item={item}
                      onAction={(action) => runAction(item, action)}
                      target={
                        <Tooltip label="Options">
                          <ActionIcon variant="subtle" color="gray" aria-label={`Options for ${item.name}`}>
                            <IconMore size={18} />
                          </ActionIcon>
                        </Tooltip>
                      }
                    />
                  </Table.Td>
                </Table.Tr>
              ))}
            </Table.Tbody>
          </Table>
        </Paper>
      )}

      {contextMenu && (
        <CaseActionsMenu
          key={`${contextMenu.item.id}-${contextMenu.x}-${contextMenu.y}`}
          item={contextMenu.item}
          opened
          onChange={(opened) => !opened && setContextMenu(null)}
          onAction={(action) => runAction(contextMenu.item, action)}
          target={<Box pos="fixed" left={contextMenu.x} top={contextMenu.y} w={1} h={1} />}
        />
      )}

      <NewCaseModal opened={creating} onClose={close} onCreated={(caseId) => navigate(`/cases/${caseId}/records`)} />
      <RenameCaseModal item={renaming} onClose={() => setRenaming(null)} />
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

function RenameCaseModal({ item, onClose }: { item: CaseListItem | null; onClose: () => void }) {
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
