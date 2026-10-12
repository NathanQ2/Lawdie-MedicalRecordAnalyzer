import { ActionIcon, Anchor, Badge, Group, Table, Text, Tooltip } from '@mantine/core'
import { IconTrash } from './icons'
import type { RecordInfo } from '../api/types'
import { formatBytes, formatDateTime, recordStatusColors, recordStatusLabels } from '../lib/format'

interface RecordsTableProps {
  records: RecordInfo[]
  recordUrl: (fileName: string) => string
  onDelete?: (record: RecordInfo) => void
  disabled?: boolean
}

export function RecordsTable({ records, recordUrl, onDelete, disabled = false }: RecordsTableProps) {
  if (records.length === 0) {
    return (
      <Text c="dimmed" ta="center" py="lg">
        No documents yet. Add them above.
      </Text>
    )
  }

  return (
    <Table highlightOnHover verticalSpacing="xs">
      <Table.Thead>
        <Table.Tr>
          <Table.Th>Document</Table.Th>
          <Table.Th>Status</Table.Th>
          <Table.Th>Size</Table.Th>
          <Table.Th>Uploaded</Table.Th>
          <Table.Th aria-label="Actions" />
        </Table.Tr>
      </Table.Thead>
      <Table.Tbody>
        {records.map((record) => (
          <Table.Tr key={record.name}>
            <Table.Td>
              <Anchor href={recordUrl(record.name)} target="_blank" rel="noreferrer" size="sm">
                {record.name}
              </Anchor>
            </Table.Td>
            <Table.Td>
              <Tooltip label={record.reason} disabled={!record.reason} multiline w={320}>
                <Badge color={recordStatusColors[record.status]} variant="light">
                  {recordStatusLabels[record.status]}
                </Badge>
              </Tooltip>
            </Table.Td>
            <Table.Td>
              <Text size="sm">{formatBytes(record.size)}</Text>
            </Table.Td>
            <Table.Td>
              <Text size="sm">{formatDateTime(record.modifiedAt)}</Text>
            </Table.Td>
            <Table.Td>
              {onDelete && (
                <Group justify="flex-end">
                  <Tooltip label={disabled ? 'Wait for the current job to finish' : 'Delete document'}>
                    <ActionIcon
                      variant="subtle"
                      color="brick"
                      aria-label={`Delete ${record.name}`}
                      disabled={disabled}
                      onClick={() => onDelete(record)}
                    >
                      <IconTrash size={16} />
                    </ActionIcon>
                  </Tooltip>
                </Group>
              )}
            </Table.Td>
          </Table.Tr>
        ))}
      </Table.Tbody>
    </Table>
  )
}
