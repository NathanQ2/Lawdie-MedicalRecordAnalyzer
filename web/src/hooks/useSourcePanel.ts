import { createContext, useContext } from 'react'
import type { Citation } from '../api/types'

export interface SourcePanel {
  /** The citation the panel shows, or null when it is closed. */
  selected: Citation | null
  show: (citation: Citation) => void
  close: () => void
}

export const SourcePanelContext = createContext<SourcePanel | null>(null)

/**
 * The case workspace's source panel. Any page in the workspace can open a citation's source, and it stays open
 * beside the page while the attorney moves between pages.
 */
export function useSourcePanel(): SourcePanel {
  const panel = useContext(SourcePanelContext)
  if (!panel) {
    throw new Error('useSourcePanel must be used inside a case workspace.')
  }
  return panel
}
