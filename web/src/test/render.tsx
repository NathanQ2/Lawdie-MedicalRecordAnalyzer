import { MantineProvider } from '@mantine/core'
import { render } from '@testing-library/react'
import type { ReactElement, ReactNode } from 'react'
import { theme } from '../theme'

// A wrapper (rather than wrapping ui directly) keeps the provider in place across rerender() calls.
// oxlint-disable-next-line react/only-export-components -- test helper, never hot-reloaded
function Providers({ children }: { children: ReactNode }) {
  return <MantineProvider theme={theme}>{children}</MantineProvider>
}

export function renderWithMantine(ui: ReactElement) {
  return render(ui, { wrapper: Providers })
}
