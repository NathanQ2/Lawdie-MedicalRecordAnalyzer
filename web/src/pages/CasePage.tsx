import { Alert, Anchor, Badge, Button, Center, Group, Loader, Stack, Tabs, Text, Title, Tooltip } from '@mantine/core'
import { modals } from '@mantine/modals'
import { notifications } from '@mantine/notifications'
import { IconArrowLeft, IconTrash } from '@tabler/icons-react'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { Link, Outlet, useLocation, useNavigate, useParams } from 'react-router'
import { api } from '../api/client'
import { caseKeys, useCase } from '../hooks/useCase'
import { isJobActive, jobStateColors } from '../lib/format'

const tabs = [
  { value: 'records', label: 'Records' },
  { value: 'run', label: 'Run' },
  { value: 'analysis', label: 'Analysis' },
  { value: 'draft', label: 'Draft' },
]

export function CasePage() {
  const { caseId = '' } = useParams()
  const caseQuery = useCase(caseId)
  const navigate = useNavigate()
  const location = useLocation()
  const queryClient = useQueryClient()
  const activeTab = tabs.find((tab) => location.pathname.endsWith(`/${tab.value}`))?.value ?? 'records'

  const remove = useMutation({
    mutationFn: () => api.deleteCase(caseId),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: caseKeys.all })
      notifications.show({ color: 'green', message: 'Case deleted.' })
      navigate('/')
    },
    onError: (error) => notifications.show({ color: 'red', title: 'Could not delete the case', message: error.message }),
  })

  if (caseQuery.isPending) {
    return (
      <Center py="xl">
        <Loader />
      </Center>
    )
  }

  if (caseQuery.isError) {
    return (
      <Stack>
        <BackLink />
        <Alert color="red" title="Could not load the case">
          {caseQuery.error.message}
        </Alert>
      </Stack>
    )
  }

  const detail = caseQuery.data
  const jobActive = isJobActive(detail.job)
  const confirmDelete = () =>
    modals.openConfirmModal({
      title: `Delete "${detail.name}"?`,
      children: <Text size="sm">This permanently deletes the case, its uploaded records, and all results. It cannot be undone.</Text>,
      labels: { confirm: 'Delete case', cancel: 'Keep it' },
      confirmProps: { color: 'red' },
      onConfirm: () => remove.mutate(),
    })

  return (
    <Stack gap="md">
      <BackLink />
      <Group justify="space-between" align="flex-start">
        <Stack gap={4}>
          <Title order={2}>{detail.name}</Title>
          <Group gap="xs">
            <Text size="sm" c="dimmed">
              {detail.records.length} {detail.records.length === 1 ? 'record' : 'records'}
            </Text>
            {jobActive && detail.job && (
              <Badge color={jobStateColors[detail.job.state]} variant="light">
                {detail.job.kind} {detail.job.state.toLowerCase()}
              </Badge>
            )}
          </Group>
        </Stack>
        <Tooltip label="Wait for the current job to finish" disabled={!jobActive}>
          <Button variant="subtle" color="red" leftSection={<IconTrash size={16} />} onClick={confirmDelete} disabled={jobActive}>
            Delete case
          </Button>
        </Tooltip>
      </Group>

      <Tabs value={activeTab} onChange={(value) => value && navigate(`/cases/${caseId}/${value}`)}>
        <Tabs.List>
          {tabs.map((tab) => (
            <Tabs.Tab key={tab.value} value={tab.value}>
              {tab.label}
            </Tabs.Tab>
          ))}
        </Tabs.List>
      </Tabs>

      <Outlet />
    </Stack>
  )
}

function BackLink() {
  return (
    <Anchor component={Link} to="/" size="sm">
      <Group gap={4}>
        <IconArrowLeft size={14} />
        All cases
      </Group>
    </Anchor>
  )
}
