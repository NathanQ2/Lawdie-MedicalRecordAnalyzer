import { Anchor, AppShell, Container, Group, Stack, Text, Title } from '@mantine/core'
import { IconFileDescription } from '@tabler/icons-react'
import { Link, Navigate, Outlet, Route, Routes } from 'react-router'
import { CasePage } from './pages/CasePage'
import { CasesPage } from './pages/CasesPage'
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
          <Route path="analysis" element={<ComingSoon what="The analysis view" />} />
          <Route path="draft" element={<ComingSoon what="The draft view" />} />
        </Route>
        <Route path="*" element={<NotFound />} />
      </Route>
    </Routes>
  )
}

function Layout() {
  return (
    <AppShell header={{ height: 56 }} padding="md">
      <AppShell.Header>
        <Container size="lg" h="100%">
          <Group h="100%">
            <Anchor component={Link} to="/" underline="never" c="inherit">
              <Group gap="xs">
                <IconFileDescription size={22} />
                <Text fw={700}>Medical Record Analysis</Text>
              </Group>
            </Anchor>
          </Group>
        </Container>
      </AppShell.Header>
      <AppShell.Main>
        <Container size="lg" py="md">
          <Outlet />
        </Container>
      </AppShell.Main>
    </AppShell>
  )
}

function ComingSoon({ what }: { what: string }) {
  return (
    <Text c="dimmed" py="lg">
      {what} is coming in the next milestone. For now, the results can be read from the API or exported from the CLI.
    </Text>
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
