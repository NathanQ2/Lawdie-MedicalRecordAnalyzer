import { AppShell, Loader, ScrollArea, Text, ThemeIcon, Tooltip } from '@mantine/core'
import { useDisclosure } from '@mantine/hooks'
import type { ReactNode } from 'react'
import { Link, useLocation, useMatch, useNavigate } from 'react-router'
import type { CaseDetail, ReviewItem } from '../api/types'
import { useAnalysis, useCase, useCases, useReview } from '../hooks/useCase'
import { isJobActive } from '../lib/format'
import { openIssueCount, openLetterItemCount, sectionFromPath, sectionLabels, type SectionId } from '../lib/workspace'
import classes from './AppSidebar.module.css'
import { IconChronology, IconConflict, IconDocuments, IconLetter, IconMatter, IconOverview, IconPlus } from './icons'
import { NewCaseModal } from './NewCaseModal'

interface SidebarCount {
  value: number
  /** Brick for problems, amber for items to check, none for plain totals. */
  tone?: 'brick' | 'amber'
  hint: string
}

const sectionIcons: Record<SectionId, ReactNode> = {
  overview: <IconOverview size={18} />,
  records: <IconDocuments size={18} />,
  chronology: <IconChronology size={18} />,
  issues: <IconConflict size={18} />,
  letter: <IconLetter size={18} />,
}

const sections: SectionId[] = ['overview', 'records', 'chronology', 'issues', 'letter']

// Enough recent cases to switch between the ones in progress; the rest are on the case list.
const RECENT_CASES = 15

interface AppSidebarProps {
  /** Called after any link is followed, so the sidebar closes on narrow screens. */
  onNavigate: () => void
}

/**
 * The app's sidebar: a new case, the case list, the open case's pages with their counts, and recent cases.
 */
export function AppSidebar({ onNavigate }: AppSidebarProps) {
  const caseId = useMatch('/cases/:caseId/*')?.params.caseId ?? null
  const location = useLocation()
  const navigate = useNavigate()
  const cases = useCases()
  const [creating, { open, close }] = useDisclosure(false)

  return (
    <AppShell.Navbar className={classes.sidebar}>
      <Link to="/" className={classes.brand} onClick={onNavigate}>
        <ThemeIcon size={30} radius="md">
          <IconDocuments size={18} />
        </ThemeIcon>
        <Text fw={700} size="sm">
          Medical Record Analysis
        </Text>
      </Link>

      <button type="button" className={classes.item} onClick={open}>
        <span className={classes.icon}>
          <IconPlus size={18} />
        </span>
        <span className={classes.label}>New case</span>
      </button>
      <SidebarLink to="/" icon={<IconMatter size={18} />} label="All cases" active={location.pathname === '/'} onClick={onNavigate} />

      {caseId && <CasePages caseId={caseId} current={sectionFromPath(location.pathname)} onNavigate={onNavigate} />}

      {cases.data && cases.data.length > 0 && (
        <>
          <div className={classes.heading}>Recent cases</div>
          <ScrollArea className={classes.recent} type="hover" scrollbarSize={6}>
            {cases.data.slice(0, RECENT_CASES).map((item) => (
              <Link
                key={item.id}
                to={`/cases/${item.id}`}
                className={classes.item}
                data-current={item.id === caseId || undefined}
                title={item.name}
                onClick={onNavigate}
              >
                <span className={classes.dot} />
                <span className={classes.label}>{item.name}</span>
                {isJobActive(item.job) && <Loader size={12} color="slate" aria-label="Analysis or draft running" />}
              </Link>
            ))}
          </ScrollArea>
        </>
      )}

      <NewCaseModal
        opened={creating}
        onClose={close}
        onCreated={(newCaseId) => {
          onNavigate()
          navigate(`/cases/${newCaseId}/records`)
        }}
      />
    </AppShell.Navbar>
  )
}

/** The open case's pages, under its name, each with its count. */
function CasePages({ caseId, current, onNavigate }: { caseId: string; current: SectionId | null; onNavigate: () => void }) {
  const caseQuery = useCase(caseId)
  const analysis = useAnalysis(caseId, caseQuery.data ? caseQuery.data.analysisUpdatedAt : undefined)
  const review = useReview(caseId)
  const counts = caseQuery.data ? sectionCounts(caseQuery.data, analysis.data?.chronology.length, review.data) : {}

  return (
    <nav aria-label="Case pages">
      <div className={classes.heading} title={caseQuery.data?.name}>
        {caseQuery.data?.name ?? 'Case'}
      </div>
      {sections.map((section) => (
        <SidebarLink
          key={section}
          to={`/cases/${caseId}/${section}`}
          icon={sectionIcons[section]}
          label={sectionLabels[section]}
          active={section === current}
          count={counts[section]}
          onClick={onNavigate}
        />
      ))}
    </nav>
  )
}

function sectionCounts(detail: CaseDetail, visits: number | undefined, review: ReviewItem[] | undefined): Partial<Record<SectionId, SidebarCount>> {
  const documents = detail.records.length
  const issues = openIssueCount(review)
  const letterItems = openLetterItemCount(review)
  return {
    records: documents > 0 ? { value: documents, hint: `${documents} ${documents === 1 ? 'document' : 'documents'} in this case.` } : undefined,
    chronology: visits ? { value: visits, hint: `${visits} ${visits === 1 ? 'visit' : 'visits'} found in the documents.` } : undefined,
    issues: issues > 0 ? { value: issues, tone: 'brick', hint: `${issues} open ${issues === 1 ? 'issue' : 'issues'} to check before you draft.` } : undefined,
    letter: letterItems > 0 ? { value: letterItems, tone: 'amber', hint: `${letterItems} open ${letterItems === 1 ? 'item' : 'items'} to fix in the letter.` } : undefined,
  }
}

interface SidebarLinkProps {
  to: string
  icon: ReactNode
  label: string
  active: boolean
  count?: SidebarCount
  onClick: () => void
}

function SidebarLink({ to, icon, label, active, count, onClick }: SidebarLinkProps) {
  return (
    <Link to={to} className={classes.item} data-active={active || undefined} aria-current={active ? 'page' : undefined} onClick={onClick}>
      <span className={classes.icon}>{icon}</span>
      <span className={classes.label}>{label}</span>
      {count && (
        <Tooltip label={count.hint}>
          <span className={classes.count} data-tone={count.tone}>
            {count.value}
          </span>
        </Tooltip>
      )}
    </Link>
  )
}
