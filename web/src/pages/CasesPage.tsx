import { ActionIcon, Alert, Badge, Button, Center, Group, Loader, Paper, Stack, Table, Text, Title, Tooltip } from '@mantine/core'
import { useDisclosure } from '@mantine/hooks'
import { IconMore, IconPlus } from '../components/icons'
import { useState, type MouseEvent } from 'react'
import { useNavigate } from 'react-router'
import type { CaseListItem } from '../api/types'
import { CaseActionsMenu, CaseContextMenu, type CaseAction, type CaseMenuPoint } from '../components/CaseActionsMenu'
import { NewCaseModal } from '../components/NewCaseModal'
import { RenameCaseModal } from '../components/RenameCaseModal'
import { useCases } from '../hooks/useCase'
import { useCaseActions } from '../hooks/useCaseActions'
import { formatDateOnly, formatDateTime, isJobActive, jobKindLabels, jobStateColors } from '../lib/format'

export function CasesPage() {
  const cases = useCases()
  const navigate = useNavigate()
  const [creating, { open, close }] = useDisclosure(false)
  const { run, renaming, closeRename } = useCaseActions()
  // A right-click opens the case's menu at the cursor.
  const [contextMenu, setContextMenu] = useState<CaseMenuPoint | null>(null)

  const runAction = (item: CaseListItem, action: CaseAction) => {
    setContextMenu(null)
    run(item, action)
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
        <CaseContextMenu point={contextMenu} onClose={() => setContextMenu(null)} onAction={(action) => runAction(contextMenu.item, action)} />
      )}

      <NewCaseModal opened={creating} onClose={close} onCreated={(caseId) => navigate(`/cases/${caseId}/records`)} />
      <RenameCaseModal item={renaming} onClose={closeRename} />
    </Stack>
  )
}

