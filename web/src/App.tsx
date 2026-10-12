import { Anchor, AppShell, Container, Group, Stack, Text, ThemeIcon, Title } from '@mantine/core'
import { IconDocuments } from './components/icons'
import { Link, Navigate, Outlet, Route, Routes } from 'react-router'
import { AnalysisTab } from './pages/AnalysisTab'
import { AnalyzeTab } from './pages/AnalyzeTab'
import { CasePage, FirstStepRedirect } from './pages/CasePage'
import { CasesPage } from './pages/CasesPage'
import { DraftTab } from './pages/DraftTab'
import { MatterTab } from './pages/MatterTab'
import { RecordsTab } from './pages/RecordsTab'

export function App() {
  return (
    <Routes>
      <Route element={<Layout />}>
        <Route index element={<CasesPage />} />
        <Route path="cases/:caseId" element={<CasePage />}>
          <Route index element={<FirstStepRedirect />} />
          <Route path="matter" element={<MatterTab />} />
          <Route path="records" element={<RecordsTab />} />
          <Route path="analyze" element={<AnalyzeTab />} />
          <Route path="review" element={<AnalysisTab />} />
          <Route path="letter" element={<DraftTab />} />
          {/* Earlier names for the steps, kept so old links still work. */}
          <Route path="run" element={<Navigate to="../analyze" replace />} />
          <Route path="analysis" element={<Navigate to="../review" replace />} />
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
