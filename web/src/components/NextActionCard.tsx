import { Button, Card, Group, Stack, Text, Title } from '@mantine/core'
import { Link } from 'react-router'
import type { NextAction } from '../lib/workspace'
import { IconChevronRight } from './icons'

/** The one thing to do next on the case, with a button that goes there. */
export function NextActionCard({ caseId, action }: { caseId: string; action: NextAction }) {
  return (
    <Card withBorder radius="lg" padding="lg">
      <Group justify="space-between" align="center" wrap="wrap" gap="md">
        <Stack gap={4} style={{ flex: 1, minWidth: 240 }}>
          <Text size="xs" fw={600} c="dimmed" tt="uppercase">
            Next step
          </Text>
          <Title order={3}>{action.title}</Title>
          <Text size="sm" c="dimmed">
            {action.detail}
          </Text>
        </Stack>
        <Button component={Link} to={`/cases/${caseId}/${action.section}`} rightSection={<IconChevronRight size={16} />}>
          {action.label}
        </Button>
      </Group>
    </Card>
  )
}
