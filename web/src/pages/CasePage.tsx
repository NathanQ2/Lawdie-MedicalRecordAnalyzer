import { Alert, Badge, Box, Center, Drawer, Group, Loader, Stack, Text, Title } from '@mantine/core'
import { useMediaQuery } from '@mantine/hooks'
import { useMemo, useState } from 'react'
import { Outlet, useLocation, useParams } from 'react-router'
import type { CaseDetail, Citation } from '../api/types'
import { SourceViewer } from '../components/SourceViewer'
import { useCase } from '../hooks/useCase'
import { SourcePanelContext, type SourcePanel } from '../hooks/useSourcePanel'
import { formatDateOnly, isJobActive, jobKindLabels, jobStateColors } from '../lib/format'
import { sectionFromPath } from '../lib/workspace'
import classes from './CasePage.module.css'

/**
 * The case workspace: the case's name and details, the current page, and a source panel that any page can open
 * beside it. The sidebar lists the case's pages.
 */
export function CasePage() {
  const { caseId = '' } = useParams()
  const caseQuery = useCase(caseId)
  const location = useLocation()
  const current = sectionFromPath(location.pathname)
  const roomy = useMediaQuery('(min-width: 75em)')

  const [selected, setSelected] = useState<Citation | null>(null)
  const panel = useMemo<SourcePanel>(() => ({ selected, show: setSelected, close: () => setSelected(null) }), [selected])

  if (caseQuery.isPending) {
    return (
      <Center py="xl">
        <Loader />
      </Center>
    )
  }

  if (caseQuery.isError) {
    return (
      <Alert color="brick" title="Could not load the case">
        {caseQuery.error.message}
      </Alert>
    )
  }

  const detail = caseQuery.data
  // The letter page shows sources in its own side panel, beside the review checklist.
  const open = selected !== null && current !== 'letter'
  const docked = open && roomy

  return (
    <SourcePanelContext.Provider value={panel}>
      <Stack gap="md">
        <CaseHeader detail={detail} />
        <div className={classes.workspace} data-docked={docked || undefined}>
          {/* Keyed by page so each page plays its entrance. */}
          <div key={current} className={classes.view}>
            <Outlet />
          </div>
          {docked && selected && (
            <Box className={classes.panel}>
              <SourceViewer caseId={caseId} citation={selected} onClose={panel.close} />
            </Box>
          )}
        </div>
        {!roomy && (
          <Drawer opened={open} onClose={panel.close} position="right" size="xl" withCloseButton={false} padding={0}>
            {selected && <SourceViewer caseId={caseId} citation={selected} onClose={panel.close} />}
          </Drawer>
        )}
      </Stack>
    </SourcePanelContext.Provider>
  )
}

function CaseHeader({ detail }: { detail: CaseDetail }) {
  const jobActive = isJobActive(detail.job)
  return (
    <Stack gap={4}>
      <Title order={2}>{detail.name}</Title>
      <Group gap="xs">
        <Text size="sm" c="dimmed">
          {matterLine(detail)}
        </Text>
        {jobActive && detail.job && (
          <Badge color={jobStateColors[detail.job.state]} variant="light">
            {jobKindLabels[detail.job.kind]} {detail.job.state.toLowerCase()}
          </Badge>
        )}
      </Group>
    </Stack>
  )
}

function matterLine(detail: CaseDetail): string {
  const documents = `${detail.records.length} ${detail.records.length === 1 ? 'document' : 'documents'}`
  const place = [detail.county && `${detail.county} County`, detail.jurisdiction].filter(Boolean).join(', ')
  const loss = detail.dateOfLoss ? `Date of loss ${formatDateOnly(detail.dateOfLoss)}` : 'No date of loss yet'
  return [loss, place, documents].filter(Boolean).join(' · ')
}
