import { Badge, Button, Group, NavLink, ScrollArea, Stack, Tooltip } from '@mantine/core'
import type { ReactNode } from 'react'
import { Link } from 'react-router'
import { sectionLabels, type SectionId } from '../lib/workspace'
import { IconChronology, IconConflict, IconDocuments, IconLetter, IconOverview } from './icons'

export interface CaseMenuCount {
  value: number
  /** Brick for problems, amber for items to check, gray for plain totals. */
  color: 'brick' | 'amber' | 'gray'
  hint: string
}

interface CaseMenuProps {
  caseId: string
  current: SectionId | null
  /** The number shown beside each page, if any. */
  counts: Partial<Record<SectionId, CaseMenuCount>>
  /** A row of buttons for narrow screens, instead of the side list. */
  horizontal?: boolean
}

const icons: Record<SectionId, ReactNode> = {
  overview: <IconOverview size={18} />,
  records: <IconDocuments size={18} />,
  chronology: <IconChronology size={18} />,
  issues: <IconConflict size={18} />,
  letter: <IconLetter size={18} />,
}

const sections: SectionId[] = ['overview', 'records', 'chronology', 'issues', 'letter']

/**
 * The case workspace's pages, each with its count: documents, visits, open issues and open letter items. Any page
 * can be opened at any time; pages that need an analysis or a letter say so themselves.
 */
export function CaseMenu({ caseId, current, counts, horizontal = false }: CaseMenuProps) {
  const badge = (section: SectionId) => {
    const count = counts[section]
    return (
      count && (
        <Tooltip label={count.hint}>
          <Badge size="sm" variant="light" color={count.color} circle={count.value < 10}>
            {count.value}
          </Badge>
        </Tooltip>
      )
    )
  }

  if (horizontal) {
    return (
      <ScrollArea type="never">
        <Group gap={4} wrap="nowrap" component="nav" aria-label="Case pages">
          {sections.map((section) => (
            <Button
              key={section}
              component={Link}
              to={`/cases/${caseId}/${section}`}
              variant={section === current ? 'light' : 'subtle'}
              leftSection={icons[section]}
              rightSection={badge(section)}
              aria-current={section === current ? 'page' : undefined}
            >
              {sectionLabels[section]}
            </Button>
          ))}
        </Group>
      </ScrollArea>
    )
  }

  return (
    <Stack gap={2} component="nav" aria-label="Case pages">
      {sections.map((section) => (
        <NavLink
          key={section}
          component={Link}
          to={`/cases/${caseId}/${section}`}
          label={sectionLabels[section]}
          leftSection={icons[section]}
          rightSection={badge(section)}
          active={section === current}
          aria-current={section === current ? 'page' : undefined}
          variant="light"
          style={{ borderRadius: 'var(--mantine-radius-md)' }}
        />
      ))}
    </Stack>
  )
}
