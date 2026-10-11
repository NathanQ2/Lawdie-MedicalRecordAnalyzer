import { Accordion, Alert, Anchor, Badge, Box, Card, Center, Drawer, Grid, Group, Loader, Paper, SimpleGrid, Stack, Text, Title, UnstyledButton } from '@mantine/core'
import { useMediaQuery } from '@mantine/hooks'
import { useState } from 'react'
import { Link, useParams } from 'react-router'
import type { CaseAnalysis, Citation, Contradiction } from '../api/types'
import { AnnotatedText } from '../components/AnnotatedText'
import { SourceViewer } from '../components/SourceViewer'
import { useAnalysis, useCase } from '../hooks/useCase'
import { citationKey, citationNumbering, pageLabel, recordName } from '../lib/citations'
import classes from './AnalysisTab.module.css'

export function AnalysisTab() {
  const { caseId = '' } = useParams()
  const caseQuery = useCase(caseId)
  const analysis = useAnalysis(caseId, caseQuery.data ? caseQuery.data.analysisUpdatedAt : undefined)
  const [selected, setSelected] = useState<Citation | null>(null)
  const wide = useMediaQuery('(min-width: 75em)')

  if (caseQuery.isPending || analysis.isPending) {
    return (
      <Center py="xl">
        <Loader />
      </Center>
    )
  }

  if (analysis.isError) {
    return <Alert color="red" title="Could not load the analysis">{analysis.error.message}</Alert>
  }

  if (!analysis.data) {
    return (
      <Paper withBorder p="xl" radius="md">
        <Stack align="center" gap="xs">
          <Text fw={500}>This case has not been analyzed yet</Text>
          <Text size="sm" c="dimmed">
            Upload the documents, then{' '}
            <Anchor component={Link} to={`/cases/${caseId}/run`}>
              run ingest
            </Anchor>
            .
          </Text>
        </Stack>
      </Paper>
    )
  }

  const viewer = selected && <SourceViewer caseId={caseId} citation={selected} onClose={() => setSelected(null)} />

  return (
    <Stack gap="md">
      {caseQuery.data?.analysisOutOfDate && (
        <Alert color="yellow">Documents changed since this analysis was made. Run ingest again to update it.</Alert>
      )}
      <Grid gap="md">
        <Grid.Col span={selected && wide ? 7 : 12}>
          <AnalysisSections analysis={analysis.data} selected={selected} onSelect={setSelected} />
        </Grid.Col>
        {wide && selected && (
          <Grid.Col span={5}>
            <Box className={classes.viewer}>{viewer}</Box>
          </Grid.Col>
        )}
      </Grid>
      {!wide && (
        <Drawer opened={selected !== null} onClose={() => setSelected(null)} position="right" size="xl" withCloseButton={false} padding={0}>
          {viewer}
        </Drawer>
      )}
    </Stack>
  )
}

interface AnalysisSectionsProps {
  analysis: CaseAnalysis
  selected: Citation | null
  onSelect: (citation: Citation) => void
}

function AnalysisSections({ analysis, selected, onSelect }: AnalysisSectionsProps) {
  const selectedKey = selected ? citationKey(selected) : null
  const summaryCitations = analysis.summary?.citations ?? []
  const summaryNumbers = citationNumbering(summaryCitations)
  const unsupported = analysis.summary?.unsupportedStatements ?? []

  return (
    <Stack gap="xl">
      <Stack gap="xs">
        <Group justify="space-between" align="baseline">
          <Title order={3}>Case summary</Title>
          <Group gap="xs">
            <Badge variant="light">{new Set(summaryCitations.map(citationKey)).size} sources cited</Badge>
            {unsupported.length > 0 && (
              <Badge variant="light" color="yellow">
                {unsupported.length} {unsupported.length === 1 ? 'sentence' : 'sentences'} without a citation
              </Badge>
            )}
          </Group>
        </Group>
        <Text size="xs" c="dimmed">
          Click a number to see the quote in its source document. Highlighted sentences have no citation; check them against the
          documents before relying on them.
        </Text>
        <Paper withBorder p="lg" radius="md">
          {analysis.summary ? (
            <AnnotatedText
              text={analysis.summary.summary}
              citations={summaryCitations}
              unsupportedStatements={unsupported}
              numberOf={summaryNumbers}
              selectedKey={selectedKey}
              onSelect={onSelect}
            />
          ) : (
            <Text c="dimmed">No case summary was written.</Text>
          )}
        </Paper>
      </Stack>

      <Stack gap="xs">
        <Group gap="xs" align="baseline">
          <Title order={3}>Contradictions</Title>
          <Badge variant="light" color={analysis.contradictions.length > 0 ? 'red' : 'green'}>
            {analysis.contradictions.length}
          </Badge>
        </Group>
        {analysis.contradictions.length === 0 ? (
          <Text c="dimmed" size="sm">
            No contradictions were found between or within the documents.
          </Text>
        ) : (
          analysis.contradictions.map((contradiction, index) => (
            <ContradictionCard key={index} contradiction={contradiction} selectedKey={selectedKey} onSelect={onSelect} />
          ))
        )}
      </Stack>

      <Stack gap="xs">
        <Title order={3}>Documents</Title>
        {analysis.skippedFiles.length > 0 && (
          <Alert color="orange" title={`${analysis.skippedFiles.length} ${analysis.skippedFiles.length === 1 ? 'document was' : 'documents were'} skipped`}>
            <Stack gap={4}>
              {analysis.skippedFiles.map((skipped) => (
                <Text key={skipped.filePath} size="sm">
                  <b>{recordName(skipped.filePath)}</b>: {skipped.reason}
                </Text>
              ))}
            </Stack>
          </Alert>
        )}
        <RecordSummaries analysis={analysis} selectedKey={selectedKey} onSelect={onSelect} />
      </Stack>
    </Stack>
  )
}

interface ContradictionCardProps {
  contradiction: Contradiction
  selectedKey: string | null
  onSelect: (citation: Citation) => void
}

export function ContradictionCard({ contradiction, selectedKey, onSelect }: ContradictionCardProps) {
  return (
    <Card withBorder radius="md" padding="md">
      <Stack gap="sm">
        {!contradiction.isVerified && (
          <Badge variant="light" color="gray" w="fit-content">
            Unverified
          </Badge>
        )}
        <Text size="sm">{contradiction.summary}</Text>
        {contradiction.citations.length === 0 ? (
          <Text size="xs" c="dimmed">
            No quote from the documents supports this contradiction. Check it against the documents before relying on it.
          </Text>
        ) : (
          <SimpleGrid cols={{ base: 1, sm: Math.min(contradiction.citations.length, 2) }} spacing="sm">
            {contradiction.citations.map((citation) => (
              <QuoteButton
                key={citationKey(citation)}
                citation={citation}
                selected={selectedKey === citationKey(citation)}
                onSelect={onSelect}
              />
            ))}
          </SimpleGrid>
        )}
      </Stack>
    </Card>
  )
}

function QuoteButton({ citation, selected, onSelect }: { citation: Citation; selected: boolean; onSelect: (citation: Citation) => void }) {
  return (
    <UnstyledButton className={classes.quote} data-selected={selected || undefined} onClick={() => onSelect(citation)}>
      <Text size="xs" fw={600} c="dimmed">
        {recordName(citation.filePath)}
        {pageLabel(citation) && ` · ${pageLabel(citation)}`}
      </Text>
      <Text size="sm">“{citation.citedText.replace(/\s+/g, ' ').trim()}”</Text>
    </UnstyledButton>
  )
}

interface RecordSummariesProps {
  analysis: CaseAnalysis
  selectedKey: string | null
  onSelect: (citation: Citation) => void
}

function RecordSummaries({ analysis, selectedKey, onSelect }: RecordSummariesProps) {
  // Sources and document summaries are stored in the same order, one per readable record.
  const records = analysis.documentSummaries.map((summary, index) => ({
    name: recordName(analysis.sources[index]?.filePath ?? summary.citations[0]?.filePath ?? `Document ${index + 1}`),
    summary,
  }))

  return (
    <Accordion variant="separated" multiple>
      {records.map(({ name, summary }) => (
        <Accordion.Item key={name} value={name}>
          <Accordion.Control>
            <Group justify="space-between" pr="sm">
              <Text size="sm" fw={500}>
                {name}
              </Text>
              <Text size="xs" c="dimmed">
                {summary.citations.length} {summary.citations.length === 1 ? 'quote' : 'quotes'}
              </Text>
            </Group>
          </Accordion.Control>
          <Accordion.Panel>
            <AnnotatedText
              text={summary.summary}
              citations={summary.citations}
              numberOf={citationNumbering(summary.citations)}
              selectedKey={selectedKey}
              onSelect={onSelect}
            />
          </Accordion.Panel>
        </Accordion.Item>
      ))}
    </Accordion>
  )
}
