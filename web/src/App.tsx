import { Anchor, AppShell, Container, Group, Stack, Text, ThemeIcon, Title } from '@mantine/core'
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

function Layout() {
  return (
    <AppShell header={{ height: 56 }} padding="md">
      <AppShell.Header className="mra-glass">
        <Container size="xl" h="100%">
          <Group h="100%">
            <Anchor component={Link} to="/" underline="never" c="inherit">
              <Group gap="xs">
                <ThemeIcon size={30} radius="md">
                  <IconDocuments size={18} />
                </ThemeIcon>
                <Text fw={700}>Medical Record Analysis</Text>
              </Group>
            </Anchor>
          </Group>
        </Container>
      </AppShell.Header>
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
