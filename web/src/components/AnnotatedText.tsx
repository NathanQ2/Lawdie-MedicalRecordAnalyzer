import { Mark, Text, Tooltip, UnstyledButton } from '@mantine/core'
import { Fragment, useEffect, useRef, type KeyboardEvent, type MouseEvent } from 'react'
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
  /** Lay the text out as a letter: "## " lines as section headings and placeholders such as [CLAIM NUMBER] highlighted. */
  letter?: boolean
  /** [start, end) ranges to highlight, such as the sentences a review item affects. The first is scrolled into view. */
  highlights?: [number, number][]
  /** Whether to scroll the first highlight into view; off for all but one piece when a text is shown in pieces. */
  scrollToHighlight?: boolean
  /** Makes each placeholder clickable, such as to find its review item. */
  onPlaceholderClick?: (placeholder: string) => void
  /** Makes uncited sentences clickable, with the text clicked (a whole sentence or part of one). */
  onUnsupportedClick?: (text: string) => void
}

// Matches the API's placeholder pattern (DemandLetterRenderer.FindPlaceholders).
const PLACEHOLDER = /(\[[A-Z][A-Z0-9 &/'.,-]*\])/
const HEADING_LINE = /^## (.*)$/m

/**
 * Generated text with a numbered marker after each cited claim (click to open the source), uncited sentences
 * highlighted for review, and optionally the sentences a review item affects.
 */
export function AnnotatedText({
  text,
  citations,
  unsupportedStatements = [],
  numberOf,
  selectedKey,
  onSelect,
  letter = false,
  highlights = [],
  scrollToHighlight = true,
  onPlaceholderClick,
  onUnsupportedClick,
}: AnnotatedTextProps) {
  const segments = annotate(text, citations, unsupportedStatements, highlights)
  const show = (content: string) => (letter ? asLetter(content, onPlaceholderClick) : content)
  const container = useRef<HTMLDivElement>(null)
  const highlightKey = highlights.map(([start, end]) => `${start}-${end}`).join(',')

  useEffect(() => {
    if (highlightKey && scrollToHighlight) {
      container.current?.querySelector('[data-highlighted]')?.scrollIntoView({ block: 'center', behavior: 'smooth' })
    }
  }, [highlightKey, scrollToHighlight])

  return (
    <Text component="div" className={classes.text} ref={container}>
      {segments.map((segment, index) => {
        const content = segment.unsupported ? (
          <Tooltip
            label={
              onUnsupportedClick
                ? 'No citation supports this sentence. Check it against the documents before you use it. Click to find it in the review list.'
                : 'No citation supports this sentence. Check it against the documents before you use it.'
            }
            multiline
            w={260}
          >
            {onUnsupportedClick ? (
              <Mark className={classes.unsupported} {...clickable(() => onUnsupportedClick(segment.text))}>
                {show(segment.text)}
              </Mark>
            ) : (
              <Mark className={classes.unsupported}>{show(segment.text)}</Mark>
            )}
          </Tooltip>
        ) : (
          show(segment.text)
        )
        return (
          <Fragment key={index}>
            {segment.highlighted ? (
              <mark className={classes.highlighted} data-highlighted>
                {content}
              </mark>
            ) : (
              content
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
        )
      })}
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
    <Tooltip label={`${describeCitation(citation)}: "${citation.citedText.replace(/\s+/g, ' ').trim()}"`} multiline w={340}>
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

/**
 * Shows "## " lines as section headings (headings are never cited, so a heading line is always whole within one
 * piece of text) and highlights placeholders in the rest.
 */
function asLetter(content: string, onPlaceholderClick?: (placeholder: string) => void) {
  const parts = content.split(new RegExp(HEADING_LINE.source, 'gm'))
  return parts.map((part, index) =>
    index % 2 === 1 ? (
      <span key={index} className={classes.heading}>
        {part}
      </span>
    ) : (
      <Fragment key={index}>{withPlaceholders(part, onPlaceholderClick)}</Fragment>
    ),
  )
}

function withPlaceholders(content: string, onClick?: (placeholder: string) => void) {
  return content.split(PLACEHOLDER).map((part, index) =>
    index % 2 === 1 ? (
      <Tooltip key={index} label={onClick ? 'Fill this in before sending. Click to find it in the review list.' : 'Fill this in before sending'}>
        {onClick ? (
          <Mark className={classes.placeholder} {...clickable(() => onClick(part))}>
            {part}
          </Mark>
        ) : (
          <Mark className={classes.placeholder}>{part}</Mark>
        )}
      </Tooltip>
    ) : (
      part
    ),
  )
}

/**
 * Props that make highlighted text act as a button, by mouse or keyboard. A click stops here, so a placeholder
 * inside an uncited sentence finds the placeholder rather than the sentence.
 */
function clickable(onClick: () => void) {
  return {
    'data-clickable': true,
    role: 'button',
    tabIndex: 0,
    onClick: (event: MouseEvent) => {
      event.stopPropagation()
      onClick()
    },
    onKeyDown: (event: KeyboardEvent) => {
      if (event.key === 'Enter' || event.key === ' ') {
        event.preventDefault()
        event.stopPropagation()
        onClick()
      }
    },
  }
}
