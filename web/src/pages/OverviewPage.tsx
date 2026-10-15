import { Accordion, Anchor, Badge, Button, Group, Modal, Paper, SimpleGrid, Stack, Text, Title, Tooltip } from '@mantine/core'
import { useDisclosure } from '@mantine/hooks'
import { notifications } from '@mantine/notifications'
import { useState, type ReactNode } from 'react'
import { Link, useParams } from 'react-router'
import type { CaseAnalysis, CaseDetail, CitedDate, MatterInput } from '../api/types'
import { AnnotatedText } from '../components/AnnotatedText'
import { IconPencil } from '../components/icons'
import { JobStatusCard } from '../components/JobStatusCard'
import { MatterForm } from '../components/MatterForm'
import { NextActionCard } from '../components/NextActionCard'
import { useAnalysis, useCase, useMatterUpdate, useReview } from '../hooks/useCase'
import { useJobActions, useNow } from '../hooks/useJob'
import { useSourcePanel } from '../hooks/useSourcePanel'
import { groupByProvider } from '../lib/chronology'
import { citationKey, citationNumbering, describeCitation } from '../lib/citations'
import { formatDateOnly, formatDateTime, isJobActive, isoDate } from '../lib/format'
import { nextAction, openIssueCount, openLetterItemCount } from '../lib/workspace'

/**
 * The case at a glance: what to do next, the key facts with their sources, the matter details, and the case summary.
 */
export function OverviewPage() {
  const { caseId = '' } = useParams()
  const caseQuery = useCase(caseId)
  const analysis = useAnalysis(caseId, caseQuery.data ? caseQuery.data.analysisUpdatedAt : undefined)
  const review = useReview(caseId)
  const { cancel } = useJobActions(caseId)
  const now = useNow(isJobActive(caseQuery.data?.job))

  if (!caseQuery.data) {
    return null
  }

  const detail = caseQuery.data
  const job = detail.job

  return (
    <Stack gap="lg">
      <NextActionCard caseId={caseId} action={nextAction(detail, review.data)} />
      {job && isJobActive(job) && <JobStatusCard job={job} onCancel={() => cancel.mutate()} cancelling={cancel.isPending} now={now} />}

      <SimpleGrid cols={{ base: 1, sm: 2, lg: 4 }} spacing="md">
        <DateOfLossFact detail={detail} recorded={analysis.data?.recordedDateOfLoss ?? null} />
        <TreatmentFact caseId={caseId} analysis={analysis.data ?? null} />
        <Fact title="Issues" link={`/cases/${caseId}/issues`} linkLabel="Review issues">
          <Text fw={600}>{analysis.data ? `${openIssueCount(review.data)} open` : 'Not analyzed yet'}</Text>
          <Text size="xs" c="dimmed">
            Contradictions and intake details to check before you draft.
          </Text>
        </Fact>
        <Fact title="Letter" link={`/cases/${caseId}/letter`} linkLabel="Go to the letter">
          <Text fw={600}>{detail.draftUpdatedAt ? `Drafted ${formatDateTime(detail.draftUpdatedAt)}` : 'Not drafted yet'}</Text>
          <Text size="xs" c="dimmed">
            {detail.draftUpdatedAt ? `${openLetterItemCount(review.data)} open items to fix before you send it.` : 'Each fact in the letter will cite a document.'}
          </Text>
        </Fact>
      </SimpleGrid>

      <MatterDetails detail={detail} />
      {analysis.data?.summary && <CaseSummary analysis={analysis.data} />}
    </Stack>
  )
}

function Fact({ title, link, linkLabel, children }: { title: string; link?: string; linkLabel?: string; children: ReactNode }) {
  return (
    <Paper withBorder radius="md" p="md">
      <Stack gap={6} h="100%">
        <Text size="xs" fw={600} c="dimmed" tt="uppercase">
          {title}
        </Text>
        {children}
        {link && (
          <Anchor component={Link} to={link} size="sm" mt="auto">
            {linkLabel}
          </Anchor>
        )}
      </Stack>
    </Paper>
  )
}

/**
 * The date of loss entered at intake, compared with the one the documents state. The letter uses the intake date,
 * so a disagreement is shown here as well as on the Issues page, with the quote and a way to take the documents' date.
 */
function DateOfLossFact({ detail, recorded }: { detail: CaseDetail; recorded: CitedDate | null }) {
  const panel = useSourcePanel()
  const save = useMatterUpdate(detail.id)
  const differs = recorded !== null && recorded.date !== detail.dateOfLoss
  const takeRecordedDate = () =>
    recorded &&
    save.mutate(
      { name: detail.name, dateOfLoss: recorded.date, jurisdiction: detail.jurisdiction, county: detail.county },
      {
        onSuccess: () => notifications.show({ color: 'sage', message: `The date of loss is now ${formatDateOnly(recorded.date)}.` }),
        onError: (error) => notifications.show({ color: 'brick', title: 'Could not save the date of loss', message: error.message }),
      },
    )

  return (
    <Fact title="Date of loss">
      <Text fw={600}>{formatDateOnly(detail.dateOfLoss)}</Text>
      {recorded && (
        <Stack gap={4}>
          <Text size="xs" c={differs ? 'brick' : 'dimmed'}>
            {differs ? `The documents give ${formatDateOnly(recorded.date)}.` : 'Matches the documents.'}{' '}
            <Anchor component="button" size="xs" onClick={() => panel.show(recorded.citations[0])}>
              {describeCitation(recorded.citations[0])}
            </Anchor>
          </Text>
          {differs && (
            <Tooltip label="Changes the date of loss in the matter details. The letter uses it.">
              <Button size="compact-xs" variant="light" color="brick" w="fit-content" loading={save.isPending} onClick={takeRecordedDate}>
                Use {formatDateOnly(recorded.date)}
              </Button>
            </Tooltip>
          )}
        </Stack>
      )}
    </Fact>
  )
}

function TreatmentFact({ caseId, analysis }: { caseId: string; analysis: CaseAnalysis | null }) {
  const entries = analysis?.chronology ?? []
  if (entries.length === 0) {
    return (
      <Fact title="Treatment" link={`/cases/${caseId}/chronology`} linkLabel="See the chronology">
        <Text fw={600}>{analysis ? 'No visits listed' : 'Not analyzed yet'}</Text>
        <Text size="xs" c="dimmed">
          {analysis ? 'Analyze the documents again to list the visits by date.' : 'The analysis lists every visit by date.'}
        </Text>
      </Fact>
    )
  }

  const providers = groupByProvider(entries).length
  return (
    <Fact title="Treatment" link={`/cases/${caseId}/chronology`} linkLabel="See the chronology">
      <Text fw={600}>
        {entries.length} {entries.length === 1 ? 'visit' : 'visits'}, {providers} {providers === 1 ? 'provider' : 'providers'}
      </Text>
      <Text size="xs" c="dimmed">
        {formatDateOnly(entries[0].date)} to {formatDateOnly(entries[entries.length - 1].date)}
      </Text>
    </Fact>
  )
}

function MatterDetails({ detail }: { detail: CaseDetail }) {
  const [editing, { open, close }] = useDisclosure(false)
  const [today] = useState(() => isoDate(new Date()))
  const save = useMatterUpdate(detail.id)
  const submit = (matter: MatterInput) => save.mutate(matter, { onSuccess: close })

  return (
    <Paper withBorder radius="md" p="md">
      <Group justify="space-between" align="flex-start">
        <Stack gap={6}>
          <Title order={4}>Matter details</Title>
          <SimpleGrid cols={{ base: 1, sm: 3 }} spacing="xl" verticalSpacing={4}>
            <Detail label="Date of loss" value={formatDateOnly(detail.dateOfLoss)} />
            <Detail label="Jurisdiction" value={detail.jurisdiction ?? 'Not entered'} />
            <Detail label="County" value={detail.county ?? 'Not entered'} />
          </SimpleGrid>
        </Stack>
        <Button variant="light" leftSection={<IconPencil size={16} />} onClick={open}>
          Edit
        </Button>
      </Group>
      <Modal opened={editing} onClose={close} title="Matter details" size="lg">
        {/* Mounted only while open, so the form starts from the saved details each time. */}
        {editing && (
          <Stack>
            <Text size="sm" c="dimmed">
              The letter uses the date of loss in its heading and first paragraph. It updates when you save.
            </Text>
            <MatterForm
              initial={{ name: detail.name, dateOfLoss: detail.dateOfLoss, jurisdiction: detail.jurisdiction, county: detail.county }}
              today={today}
              submitLabel="Save"
              pending={save.isPending}
              error={save.isError ? save.error.message : null}
              onSubmit={submit}
              onCancel={close}
            />
          </Stack>
        )}
      </Modal>
    </Paper>
  )
}

function Detail({ label, value }: { label: string; value: string }) {
  return (
    <Stack gap={0}>
      <Text size="xs" c="dimmed">
        {label}
      </Text>
      <Text size="sm">{value}</Text>
    </Stack>
  )
}

/** The case summary, folded away by default: the chronology and issues are where review starts. */
function CaseSummary({ analysis }: { analysis: CaseAnalysis }) {
  const panel = useSourcePanel()
  const summary = analysis.summary
  if (!summary) {
    return null
  }

  return (
    <Accordion variant="separated" radius="md">
      <Accordion.Item value="summary">
        <Accordion.Control>
          <Group gap="xs">
            <Text fw={600}>Case summary</Text>
            {summary.unsupportedStatements.length > 0 && (
              <Tooltip label="No quote supports these sentences. They are highlighted in the summary.">
                <Badge variant="light" color="amber">
                  {summary.unsupportedStatements.length} without a citation
                </Badge>
              </Tooltip>
            )}
          </Group>
        </Accordion.Control>
        <Accordion.Panel>
          <AnnotatedText
            text={summary.summary}
            citations={summary.citations}
            unsupportedStatements={summary.unsupportedStatements}
            numberOf={citationNumbering(summary.citations)}
            selectedKey={panel.selected ? citationKey(panel.selected) : null}
            onSelect={panel.show}
          />
        </Accordion.Panel>
      </Accordion.Item>
    </Accordion>
  )
}
