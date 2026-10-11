import { Menu } from '@mantine/core'
import { IconChartBar, IconDownload, IconFileText, IconFolderOpen, IconPencil, IconTrash, IconWritingSign } from '@tabler/icons-react'
import type { ReactNode } from 'react'
import { api } from '../api/client'
import type { CaseListItem } from '../api/types'
import { isJobActive } from '../lib/format'

export type CaseAction = 'open' | 'documents' | 'analysis' | 'draft' | 'rename' | 'delete'

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
        <Menu.Item leftSection={<IconFolderOpen size={16} />} onClick={() => onAction('open')}>
          Open
        </Menu.Item>
        <Menu.Item leftSection={<IconFileText size={16} />} onClick={() => onAction('documents')}>
          Documents
        </Menu.Item>
        <Menu.Item leftSection={<IconChartBar size={16} />} disabled={!item.hasAnalysis} onClick={() => onAction('analysis')}>
          Analysis
        </Menu.Item>
        <Menu.Item leftSection={<IconWritingSign size={16} />} disabled={!item.hasDraft} onClick={() => onAction('draft')}>
          Draft
        </Menu.Item>
        {item.hasDraft && (
          <Menu.Item leftSection={<IconDownload size={16} />} component="a" href={api.draftExportUrl(item.id, 'docx')} download>
            Download draft (Word)
          </Menu.Item>
        )}
        <Menu.Divider />
        <Menu.Item leftSection={<IconPencil size={16} />} onClick={() => onAction('rename')}>
          Rename…
        </Menu.Item>
        <Menu.Item
          color="red"
          leftSection={<IconTrash size={16} />}
          disabled={jobActive}
          title={jobActive ? 'Wait for the current job to finish' : undefined}
          onClick={() => onAction('delete')}
        >
          Delete…
        </Menu.Item>
      </Menu.Dropdown>
    </Menu>
  )
}
