import { Menu } from '@mantine/core'
import { IconChronology, IconConflict, IconDocuments, IconDownload, IconLetter, IconOverview, IconPencil, IconTrash } from './icons'
import type { ReactNode } from 'react'
import { api } from '../api/client'
import type { CaseListItem } from '../api/types'
import { isJobActive } from '../lib/format'

export type CaseAction = 'open' | 'documents' | 'chronology' | 'issues' | 'letter' | 'rename' | 'delete'

interface CaseActionsMenuProps {
  item: CaseListItem
  /** The element the menu opens from: the ⋮ button, or an invisible point at the cursor for right-clicks. */
  target: ReactNode
  opened?: boolean
  onChange?: (opened: boolean) => void
  onAction: (action: CaseAction) => void
}

/**
 * The actions for one case, shared by the row's right-click menu and its ⋮ button.
 */
export function CaseActionsMenu({ item, target, opened, onChange, onAction }: CaseActionsMenuProps) {
  const jobActive = isJobActive(item.job)

  return (
    <Menu opened={opened} onChange={onChange} position="bottom-end" withinPortal shadow="md" width={220}>
      <Menu.Target>{target}</Menu.Target>
      <Menu.Dropdown onClick={(event) => event.stopPropagation()}>
        <Menu.Item leftSection={<IconOverview size={16} />} onClick={() => onAction('open')}>
          Open
        </Menu.Item>
        <Menu.Item leftSection={<IconDocuments size={16} />} onClick={() => onAction('documents')}>
          Documents
        </Menu.Item>
        <Menu.Item leftSection={<IconChronology size={16} />} disabled={!item.hasAnalysis} onClick={() => onAction('chronology')}>
          Chronology
        </Menu.Item>
        <Menu.Item leftSection={<IconConflict size={16} />} disabled={!item.hasAnalysis} onClick={() => onAction('issues')}>
          Issues
        </Menu.Item>
        <Menu.Item leftSection={<IconLetter size={16} />} disabled={!item.hasDraft} onClick={() => onAction('letter')}>
          Letter
        </Menu.Item>
        {item.hasDraft && (
          <Menu.Item leftSection={<IconDownload size={16} />} component="a" href={api.draftExportUrl(item.id, 'docx')} download>
            Download letter (Word)
          </Menu.Item>
        )}
        <Menu.Divider />
        <Menu.Item leftSection={<IconPencil size={16} />} onClick={() => onAction('rename')}>
          Rename…
        </Menu.Item>
        <Menu.Item
          color="brick"
          leftSection={<IconTrash size={16} />}
          disabled={jobActive}
          title={jobActive ? 'Wait until the analysis or draft finishes.' : undefined}
          onClick={() => onAction('delete')}
        >
          Delete…
        </Menu.Item>
      </Menu.Dropdown>
    </Menu>
  )
}
