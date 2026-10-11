import { Mark, Text, Tooltip, UnstyledButton } from '@mantine/core'
import { Fragment } from 'react'
import type { Citation } from '../api/types'
import { annotate, citationKey, describeCitation } from '../lib/citations'
import classes from './AnnotatedText.module.css'

interface AnnotatedTextProps {
  text: string
  citations: Citation[]
  unsupportedStatements?: string[]
  /** Numbers shown on the markers, so the same source keeps its number across a section. */
  numberOf: (citation: Citation) => number
  selectedKey: string | null
  onSelect: (citation: Citation) => void
}

/**
 * Generated text with a numbered marker after each cited claim (click to open the source) and uncited sentences
 * highlighted for review.
 */
export function AnnotatedText({ text, citations, unsupportedStatements = [], numberOf, selectedKey, onSelect }: AnnotatedTextProps) {
  const segments = annotate(text, citations, unsupportedStatements)

  return (
    <Text component="div" className={classes.text}>
      {segments.map((segment, index) => (
        <Fragment key={index}>
          {segment.unsupported ? (
            <Tooltip label="No citation supports this sentence. Check it against the records." multiline w={260}>
              <Mark className={classes.unsupported}>{segment.text}</Mark>
            </Tooltip>
          ) : (
            segment.text
          )}
          {segment.citations.map((citation) => (
            <CitationMarker
              key={citationKey(citation)}
              citation={citation}
              number={numberOf(citation)}
              selected={selectedKey === citationKey(citation)}
              onSelect={onSelect}
            />
          ))}
        </Fragment>
      ))}
    </Text>
  )
}

interface CitationMarkerProps {
  citation: Citation
  number: number
  selected: boolean
  onSelect: (citation: Citation) => void
}

export function CitationMarker({ citation, number, selected, onSelect }: CitationMarkerProps) {
  return (
    <Tooltip label={`${describeCitation(citation)}: “${citation.citedText.replace(/\s+/g, ' ').trim()}”`} multiline w={340}>
      <UnstyledButton
        className={classes.marker}
        data-selected={selected || undefined}
        onClick={() => onSelect(citation)}
        aria-label={`Source ${number}: ${describeCitation(citation)}`}
      >
        {number}
      </UnstyledButton>
    </Tooltip>
  )
}
