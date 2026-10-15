import { Box, Menu, type MenuProps } from '@mantine/core'
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
  position?: MenuProps['position']
}

/**
 * The actions for one case, shared by the row's right-click menu and its ⋮ button.
 */
export function CaseActionsMenu({ item, target, opened, onChange, onAction, position = 'bottom-end' }: CaseActionsMenuProps) {
  const jobActive = isJobActive(item.job)

  return (
    <Menu opened={opened} onChange={onChange} position={position} withinPortal shadow="md" width={220}>
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

/** Where a right-click opened a case's menu. */
export interface CaseMenuPoint {
  item: CaseListItem
  x: number
  y: number
}

interface CaseContextMenuProps {
  point: CaseMenuPoint
  onClose: () => void
  onAction: (action: CaseAction) => void
}

/**
 * A case's menu opened by a right-click, below and to the right of the cursor like a system context menu. Keyed by
 * the point, so a second right-click moves the menu rather than closing it.
 */
export function CaseContextMenu({ point, onClose, onAction }: CaseContextMenuProps) {
  return (
    <CaseActionsMenu
      key={`${point.item.id}-${point.x}-${point.y}`}
      item={point.item}
      opened
      onChange={(opened) => !opened && onClose()}
      onAction={onAction}
      position="bottom-start"
      target={<Box pos="fixed" left={point.x} top={point.y} w={1} h={1} />}
    />
  )
}
