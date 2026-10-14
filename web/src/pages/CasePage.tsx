import { Alert, Anchor, Badge, Box, Center, Drawer, Group, Loader, Stack, Text, Title } from '@mantine/core'
import { useMediaQuery } from '@mantine/hooks'
import { useMemo, useState } from 'react'
import { Link, Outlet, useLocation, useParams } from 'react-router'
import type { CaseDetail, Citation, ReviewItem } from '../api/types'
import { CaseMenu, type CaseMenuCount } from '../components/CaseMenu'
import { IconBack } from '../components/icons'
import { SourceViewer } from '../components/SourceViewer'
import { useAnalysis, useCase, useReview } from '../hooks/useCase'
import { SourcePanelContext, type SourcePanel } from '../hooks/useSourcePanel'
import { formatDateOnly, isJobActive, jobKindLabels, jobStateColors } from '../lib/format'
import { openIssueCount, openLetterItemCount, sectionFromPath, type SectionId } from '../lib/workspace'
import classes from './CasePage.module.css'

/**
 * The case workspace: the case's name and details, a menu of its pages, the current page, and a source panel that
 * any page can open beside it.
 */
export function CasePage() {
  const { caseId = '' } = useParams()
  const caseQuery = useCase(caseId)
  const analysis = useAnalysis(caseId, caseQuery.data ? caseQuery.data.analysisUpdatedAt : undefined)
  const review = useReview(caseId)
  const location = useLocation()
  const current = sectionFromPath(location.pathname)
  const sideMenu = useMediaQuery('(min-width: 62em)')
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
      <Stack>
        <BackLink />
        <Alert color="brick" title="Could not load the case">
          {caseQuery.error.message}
        </Alert>
      </Stack>
    )
  }

  const detail = caseQuery.data
  // The letter page shows sources in its own side panel, beside the review checklist.
  const open = selected !== null && current !== 'letter'
  const docked = open && roomy

  return (
    <SourcePanelContext.Provider value={panel}>
      <Stack gap="md">
        <BackLink />
        <CaseHeader detail={detail} />
        <div className={classes.workspace} data-docked={docked || undefined}>
          <Box className={classes.menu}>
            <CaseMenu caseId={caseId} current={current} counts={menuCounts(detail, analysis.data?.chronology.length, review.data)} horizontal={!sideMenu} />
          </Box>
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

function menuCounts(detail: CaseDetail, visits: number | undefined, review: ReviewItem[] | undefined): Partial<Record<SectionId, CaseMenuCount>> {
  const documents = detail.records.length
  const issues = openIssueCount(review)
  const letterItems = openLetterItemCount(review)
  return {
    records: documents > 0 ? { value: documents, color: 'gray', hint: `${documents} ${documents === 1 ? 'document' : 'documents'} in this case.` } : undefined,
    chronology: visits ? { value: visits, color: 'gray', hint: `${visits} ${visits === 1 ? 'visit' : 'visits'} found in the documents.` } : undefined,
    issues: issues > 0 ? { value: issues, color: 'brick', hint: `${issues} open ${issues === 1 ? 'issue' : 'issues'} to check before you draft.` } : undefined,
    letter: letterItems > 0 ? { value: letterItems, color: 'amber', hint: `${letterItems} open ${letterItems === 1 ? 'item' : 'items'} to fix in the letter.` } : undefined,
  }
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

function BackLink() {
  return (
    <Anchor component={Link} to="/" size="sm">
      <Group gap={4}>
        <IconBack size={14} />
        All cases
      </Group>
    </Anchor>
  )
}
