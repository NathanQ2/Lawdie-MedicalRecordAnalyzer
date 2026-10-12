import { Alert, Button, Group, Select, SimpleGrid, Stack, TextInput } from '@mantine/core'
import { useState, type FormEvent } from 'react'
import type { MatterInput } from '../api/types'
import { US_JURISDICTIONS } from '../lib/jurisdictions'

interface MatterFormProps {
  initial?: MatterInput
  /** Today as YYYY-MM-DD, the latest date of loss the form accepts. */
  today: string
  submitLabel: string
  pending: boolean
  error: string | null
  onSubmit: (matter: MatterInput) => void
  onCancel?: () => void
}

const EMPTY: MatterInput = { name: '', dateOfLoss: null, jurisdiction: null, county: null }

/**
 * The intake form for a matter: its name and the details the documents may not state reliably. Used both to
 * create a case and to edit one.
 */
export function MatterForm({ initial = EMPTY, today, submitLabel, pending, error, onSubmit, onCancel }: MatterFormProps) {
  const [name, setName] = useState(initial.name)
  const [dateOfLoss, setDateOfLoss] = useState(initial.dateOfLoss ?? '')
  const [jurisdiction, setJurisdiction] = useState<string | null>(initial.jurisdiction)
  const [county, setCounty] = useState(initial.county ?? '')

  const submit = (event: FormEvent) => {
    event.preventDefault()
    onSubmit({
      name: name.trim(),
      dateOfLoss: dateOfLoss || null,
      jurisdiction: jurisdiction || null,
      county: county.trim() || null,
    })
  }

  return (
    <form onSubmit={submit}>
      <Stack>
        <TextInput
          label="Matter name"
          placeholder="Ellis v. Cascade Freight"
          value={name}
          onChange={(event) => setName(event.currentTarget.value)}
          maxLength={200}
          data-autofocus
          required
        />
        <SimpleGrid cols={{ base: 1, sm: 3 }}>
          <TextInput
            label="Date of loss"
            type="date"
            value={dateOfLoss}
            max={today}
            onChange={(event) => setDateOfLoss(event.currentTarget.value)}
            required
          />
          <Select
            label="Jurisdiction"
            placeholder="State"
            data={US_JURISDICTIONS}
            value={jurisdiction}
            onChange={setJurisdiction}
            searchable
            clearable
            // Typing a state and pressing Enter picks it, instead of submitting the form.
            selectFirstOptionOnChange
          />
          <TextInput label="County" placeholder="King" value={county} onChange={(event) => setCounty(event.currentTarget.value)} maxLength={100} />
        </SimpleGrid>
        {error && <Alert color="brick">{error}</Alert>}
        <Group justify="flex-end">
          {onCancel && (
            <Button variant="default" onClick={onCancel}>
              Cancel
            </Button>
          )}
          <Button type="submit" loading={pending} disabled={!name.trim() || !dateOfLoss}>
            {submitLabel}
          </Button>
        </Group>
      </Stack>
    </form>
  )
}
