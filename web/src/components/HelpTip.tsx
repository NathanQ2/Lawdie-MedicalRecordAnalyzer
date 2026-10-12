import { Tooltip } from '@mantine/core'
import type { ReactNode } from 'react'
import { IconHelp } from './icons'

/**
 * A field label with a help icon that explains the field on hover, keyboard focus or tap.
 */
export function LabelWithHelp({ label, help }: { label: ReactNode; help: string }) {
  return (
    <span style={{ display: 'inline-flex', alignItems: 'center', gap: 4 }}>
      {label}
      <Tooltip label={help}>
        <span role="img" aria-label={help} tabIndex={0} style={{ display: 'inline-flex', color: 'var(--mantine-color-dimmed)', cursor: 'help' }}>
          <IconHelp size={14} />
        </span>
      </Tooltip>
    </span>
  )
}
