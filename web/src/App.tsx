import { Anchor, AppShell, Burger, Container, Group, Stack, Text, ThemeIcon, Title } from '@mantine/core'
import { useDisclosure, useMediaQuery } from '@mantine/hooks'
import { AppSidebar } from './components/AppSidebar'
import { IconDocuments } from './components/icons'
import { Link, Navigate, Outlet, Route, Routes } from 'react-router'
import { CasePage } from './pages/CasePage'
import { CasesPage } from './pages/CasesPage'
import { ChronologyPage } from './pages/ChronologyPage'
import { DocumentsPage } from './pages/DocumentsPage'
import { IssuesPage } from './pages/IssuesPage'
import { LetterPage } from './pages/LetterPage'
import { OverviewPage } from './pages/OverviewPage'

export function App() {
  return (
    <Routes>
      <Route element={<Layout />}>
        <Route index element={<CasesPage />} />
        <Route path="cases/:caseId" element={<CasePage />}>
          <Route index element={<Navigate to="overview" replace />} />
          <Route path="overview" element={<OverviewPage />} />
          <Route path="records" element={<DocumentsPage />} />
          <Route path="chronology" element={<ChronologyPage />} />
          <Route path="issues" element={<IssuesPage />} />
          <Route path="letter" element={<LetterPage />} />
          {/* Earlier page names, kept so old links still work. */}
          <Route path="matter" element={<Navigate to="../overview" replace />} />
          <Route path="analyze" element={<Navigate to="../records" replace />} />
          <Route path="review" element={<Navigate to="../issues" replace />} />
          <Route path="run" element={<Navigate to="../records" replace />} />
          <Route path="analysis" element={<Navigate to="../issues" replace />} />
          <Route path="draft" element={<Navigate to="../letter" replace />} />
        </Route>
        <Route path="*" element={<NotFound />} />
      </Route>
    </Routes>
  )
}

/**
 * The app frame: the sidebar on the left and the page beside it. On narrow screens the sidebar folds away behind a
 * menu button in a slim header, which is hidden on wide screens where the sidebar shows the app's name.
 */
function Layout() {
  const [opened, { toggle, close }] = useDisclosure(false)
  const wide = useMediaQuery('(min-width: 62em)', true, { getInitialValueInEffect: false })

  return (
    <AppShell
      header={{ height: 56, collapsed: wide }}
      navbar={{ width: 264, breakpoint: 'md', collapsed: { mobile: !opened } }}
      padding="md"
      withBorder={false}
    >
      <AppShell.Header className="mra-glass">
        <Group h="100%" px="md" gap="sm">
          <Burger opened={opened} onClick={toggle} size="sm" aria-label={opened ? 'Close the menu' : 'Open the menu'} />
          <Anchor component={Link} to="/" underline="never" c="inherit" onClick={close}>
            <Group gap="xs">
              <ThemeIcon size={30} radius="md">
                <IconDocuments size={18} />
              </ThemeIcon>
              <Text fw={700}>Medical Record Analysis</Text>
            </Group>
          </Anchor>
        </Group>
      </AppShell.Header>
      <AppSidebar onNavigate={close} />
      <AppShell.Main>
        <Container size={1480} py="md">
          <Outlet />
        </Container>
      </AppShell.Main>
    </AppShell>
  )
}

function NotFound() {
  return (
    <Stack>
      <Title order={2}>Page not found</Title>
      <Anchor component={Link} to="/">
        Back to cases
      </Anchor>
    </Stack>
  )
}
