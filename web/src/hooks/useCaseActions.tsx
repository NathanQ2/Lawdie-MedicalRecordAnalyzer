import { Text } from '@mantine/core'
import { modals } from '@mantine/modals'
import { notifications } from '@mantine/notifications'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { useMatch, useNavigate } from 'react-router'
import { api } from '../api/client'
import type { CaseListItem } from '../api/types'
import type { CaseAction } from '../components/CaseActionsMenu'
import { caseKeys } from './useCase'

const actionPaths: Partial<Record<CaseAction, string>> = {
  open: '',
  documents: '/records',
  chronology: '/chronology',
  issues: '/issues',
  letter: '/letter',
}

/**
 * Runs the case menu's actions, for the case list and the sidebar alike. Renaming sets `renaming` for a
 * RenameCaseModal; deleting asks first, and leaves the case's pages when the deleted case is the one open.
 */
export function useCaseActions(onNavigate?: () => void) {
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const openCaseId = useMatch('/cases/:caseId/*')?.params.caseId
  const [renaming, setRenaming] = useState<CaseListItem | null>(null)

  const remove = useMutation({
    mutationFn: (item: CaseListItem) => api.deleteCase(item.id),
    onSuccess: async (_, item) => {
      if (item.id === openCaseId) {
        navigate('/', { replace: true })
      }
      // Drop the deleted case's own queries rather than refetching them into errors.
      queryClient.removeQueries({ queryKey: caseKeys.detail(item.id) })
      await queryClient.invalidateQueries({ queryKey: caseKeys.all, exact: true })
      notifications.show({ color: 'sage', message: `Deleted "${item.name}".` })
    },
    onError: (error) => notifications.show({ color: 'brick', title: 'Could not delete the case', message: error.message }),
  })

  const run = (item: CaseListItem, action: CaseAction) => {
    const path = actionPaths[action]
    if (path !== undefined) {
      onNavigate?.()
      navigate(`/cases/${item.id}${path}`)
    } else if (action === 'rename') {
      setRenaming(item)
    } else if (action === 'delete') {
      modals.openConfirmModal({
        title: `Delete "${item.name}"?`,
        children: <Text size="sm">This deletes the case, its documents and all results. You can't undo this.</Text>,
        labels: { confirm: 'Delete case', cancel: 'Keep it' },
        confirmProps: { color: 'brick' },
        onConfirm: () => remove.mutate(item),
      })
    }
  }

  return { run, renaming, closeRename: () => setRenaming(null) }
}
