import { Anchor, AppShell, Container, Group, Stack, Text, ThemeIcon, Title } from '@mantine/core'
import { IconDocuments } from './components/icons'
import { Link, Navigate, Outlet, Route, Routes } from 'react-router'
import { AnalysisTab } from './pages/AnalysisTab'
import { CasePage } from './pages/CasePage'
import { CasesPage } from './pages/CasesPage'
import { DraftTab } from './pages/DraftTab'
import { RecordsTab } from './pages/RecordsTab'
import { RunTab } from './pages/RunTab'

export function App() {
  return (
    <Routes>
      <Route element={<Layout />}>
        <Route index element={<CasesPage />} />
        <Route path="cases/:caseId" element={<CasePage />}>
          <Route index element={<Navigate to="records" replace />} />
          <Route path="records" element={<RecordsTab />} />
          <Route path="run" element={<RunTab />} />
          <Route path="analysis" element={<AnalysisTab />} />
          <Route path="draft" element={<DraftTab />} />
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
        <Container size="xl" py="md">
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
