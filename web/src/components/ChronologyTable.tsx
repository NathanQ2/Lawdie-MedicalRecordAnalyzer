import { Badge, Group, Stack, Table, Text, Tooltip, UnstyledButton } from '@mantine/core'
import { Fragment } from 'react'
import type { ChronologyEntry, Citation, Contradiction } from '../api/types'
import { chronologyFlags, groupByProvider, type FlagKind } from '../lib/chronology'
import { citationKey, pageLabel, recordName } from '../lib/citations'
import { formatDateOnly } from '../lib/format'

const flagColors: Record<FlagKind, string> = {
  conflict: 'brick',
  date: 'amber',
  provider: 'amber',
  scanned: 'slate',
  before: 'gray',
}

interface ChronologyTableProps {
  entries: ChronologyEntry[]
  contradictions: Contradiction[]
  /** YYYY-MM-DD; visits before it are marked as before the incident. */
  dateOfLoss: string | null
  groupBy: 'date' | 'provider'
  selectedKey: string | null
  onSelect: (citation: Citation) => void
}

/**
 * The visits in the records, by date or grouped by provider. Each row opens the quote behind it, and flags say what
 * to check: a contradiction, a date or provider not found in the document, a scanned quote, or a visit before the
 * incident.
 */
export function ChronologyTable({ entries, contradictions, dateOfLoss, groupBy, selectedKey, onSelect }: ChronologyTableProps) {
  const row = (entry: ChronologyEntry, index: number) => {
    const source = entry.citations[0]
    const selected = entry.citations.some((citation) => citationKey(citation) === selectedKey)
    const flags = chronologyFlags(entry, contradictions, dateOfLoss)
    return (
      <Table.Tr
        key={`${entry.date}-${entry.provider}-${entry.visitType}-${index}`}
        onClick={() => onSelect(source)}
        style={{ cursor: 'pointer' }}
        bg={selected ? 'var(--mantine-color-sage-0)' : undefined}
      >
        <Table.Td style={{ whiteSpace: 'nowrap' }}>{formatDateOnly(entry.date)}</Table.Td>
        {groupBy === 'date' && <Table.Td>{entry.provider}</Table.Td>}
        <Table.Td>{entry.visitType}</Table.Td>
        <Table.Td>
          <Stack gap={4}>
            {entry.finding}
            {flags.length > 0 && (
              <Group gap={4}>
                {flags.map((flag) => (
                  <Tooltip key={flag.kind} label={flag.hint}>
                    <Badge size="sm" variant="light" color={flagColors[flag.kind]}>
                      {flag.label}
                    </Badge>
                  </Tooltip>
                ))}
              </Group>
            )}
          </Stack>
        </Table.Td>
        <Table.Td>
          {/* File names are long, so the cell shows one line of the name and the full name on hover. */}
          <Tooltip label={recordName(source.filePath)}>
            <UnstyledButton
              onClick={(event) => {
                event.stopPropagation()
                onSelect(source)
              }}
              aria-label={`Open the source of the ${entry.visitType} on ${formatDateOnly(entry.date)}`}
            >
              <Text size="xs" c="slate" truncate="end" maw={150}>
                {recordName(source.filePath)}
              </Text>
              <Text size="xs" c="dimmed">
                {[pageLabel(source), entry.citations.length > 1 && `${entry.citations.length - 1} more`].filter(Boolean).join(' · ')}
              </Text>
            </UnstyledButton>
          </Tooltip>
        </Table.Td>
      </Table.Tr>
    )
  }

  const header = (
    <Table.Thead>
      <Table.Tr>
        <Table.Th>Date</Table.Th>
        {groupBy === 'date' && <Table.Th>Provider</Table.Th>}
        <Table.Th>Visit</Table.Th>
        <Table.Th>Finding</Table.Th>
        <Table.Th>Source</Table.Th>
      </Table.Tr>
    </Table.Thead>
  )

  return (
    <Table.ScrollContainer minWidth={560}>
      <Table highlightOnHover verticalSpacing="xs" fz="sm">
        {header}
        <Table.Tbody>
          {groupBy === 'date'
            ? entries.map(row)
            : groupByProvider(entries).map((group) => (
                <Fragment key={group.provider}>
                  <Table.Tr bg="var(--mantine-color-gray-1)">
                    <Table.Td colSpan={4}>
                      <Stack gap={0}>
                        <Text size="sm" fw={600}>
                          {group.provider}
                        </Text>
                        <Text size="xs" c="dimmed">
                          {group.entries.length} {group.entries.length === 1 ? 'visit' : 'visits'}
                        </Text>
                      </Stack>
                    </Table.Td>
                  </Table.Tr>
                  {group.entries.map(row)}
                </Fragment>
              ))}
        </Table.Tbody>
      </Table>
    </Table.ScrollContainer>
  )
}
