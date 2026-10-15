import { Anchor, Center, Group, Loader, Paper, SegmentedControl, Stack, Switch, Text, TextInput, Title } from '@mantine/core'
import { useState } from 'react'
import { Link, useParams } from 'react-router'
import { ChronologyTable } from '../components/ChronologyTable'
import { useAnalysis, useCase } from '../hooks/useCase'
import { useSourcePanel } from '../hooks/useSourcePanel'
import { chronologyFlags, searchChronology } from '../lib/chronology'
import { citationKey } from '../lib/citations'

/** Every visit, test and procedure in the documents, by date or by provider, each linked to its quote. */
export function ChronologyPage() {
  const { caseId = '' } = useParams()
  const caseQuery = useCase(caseId)
  const analysis = useAnalysis(caseId, caseQuery.data ? caseQuery.data.analysisUpdatedAt : undefined)
  const panel = useSourcePanel()
  const [query, setQuery] = useState('')
  const [groupBy, setGroupBy] = useState<'date' | 'provider'>('date')
  const [flaggedOnly, setFlaggedOnly] = useState(false)

  if (caseQuery.isPending || analysis.isPending) {
    return (
      <Center py="xl">
        <Loader />
      </Center>
    )
  }

  if (!analysis.data || analysis.data.chronology.length === 0) {
    return (
      <Paper withBorder p="xl" radius="md">
        <Stack align="center" gap="xs">
          <Text fw={500}>{analysis.data ? 'No visits are listed yet' : 'The documents have not been analyzed yet'}</Text>
          <Text size="sm" c="dimmed" ta="center">
            {analysis.data ? 'This analysis was made before visits were listed, or no quote gives a visit date. ' : ''}
            <Anchor component={Link} to={`/cases/${caseId}/records`}>
              {analysis.data ? 'Analyze the documents again' : 'Analyze the documents'}
            </Anchor>{' '}
            to list every visit by date.
          </Text>
        </Stack>
      </Paper>
    )
  }

  const data = analysis.data
  // The documents' date is preferred, since visits before it are judged against what the records say.
  const dateOfLoss = data.recordedDateOfLoss?.date ?? caseQuery.data?.dateOfLoss ?? null
  const searched = searchChronology(data.chronology, query)
  const shown = flaggedOnly ? searched.filter((entry) => chronologyFlags(entry, data.contradictions, dateOfLoss).length > 0) : searched

  return (
    <Stack gap="md">
      <Group justify="space-between" align="flex-end">
        <Stack gap={4}>
          <Title order={3}>Chronology</Title>
          <Text size="xs" c="dimmed">
            Click a visit to see its quote in the document. Flags show what to check before you rely on it.
          </Text>
        </Stack>
        <Text size="sm" c="dimmed">
          {shown.length === data.chronology.length ? `${shown.length} visits` : `${shown.length} of ${data.chronology.length} visits`}
        </Text>
      </Group>

      <Group gap="md" align="center">
        <TextInput
          placeholder="Search visits"
          aria-label="Search visits"
          value={query}
          onChange={(event) => setQuery(event.currentTarget.value)}
          style={{ flex: 1, minWidth: 200 }}
        />
        <SegmentedControl
          value={groupBy}
          onChange={(value) => setGroupBy(value as 'date' | 'provider')}
          data={[
            { value: 'date', label: 'By date' },
            { value: 'provider', label: 'By provider' },
          ]}
        />
        <Switch label="Flagged only" checked={flaggedOnly} onChange={(event) => setFlaggedOnly(event.currentTarget.checked)} />
      </Group>

      <Paper withBorder radius="md">
        {shown.length === 0 ? (
          <Text c="dimmed" ta="center" py="lg" size="sm">
            No visits match.
          </Text>
        ) : (
          <ChronologyTable
            entries={shown}
            contradictions={data.contradictions}
            dateOfLoss={dateOfLoss}
            groupBy={groupBy}
            selectedKey={panel.selected ? citationKey(panel.selected) : null}
            onSelect={panel.show}
          />
        )}
      </Paper>
    </Stack>
  )
}
