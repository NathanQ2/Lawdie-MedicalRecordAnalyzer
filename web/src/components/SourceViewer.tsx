import { ActionIcon, Alert, Anchor, Blockquote, Box, Button, Center, Group, Loader, Mark, Paper, ScrollArea, Stack, Text, Title, Tooltip } from '@mantine/core'
import { useElementSize } from '@mantine/hooks'
import { IconChevronLeft, IconChevronRight, IconExternalLink, IconX } from '@tabler/icons-react'
import { useQuery } from '@tanstack/react-query'
import { lazy, Suspense, useCallback, useEffect, useRef, useState } from 'react'
import { api } from '../api/client'
import type { Citation } from '../api/types'
import { citationKey, locateInText, pageLabel, recordName } from '../lib/citations'

// pdf.js is large, so it loads only when a PDF is first shown.
const PdfPage = lazy(() => import('./PdfPage'))

interface SourceViewerProps {
  caseId: string
  citation: Citation
  onClose: () => void
}

/**
 * Shows a citation's source: the cited PDF page with the quote highlighted, or the text file scrolled to it.
 */
export function SourceViewer({ caseId, citation, onClose }: SourceViewerProps) {
  const name = recordName(citation.filePath)
  const url = api.recordUrl(caseId, name)
  const isPdf = name.toLowerCase().endsWith('.pdf')

  return (
    <Paper withBorder radius="md" p="md" h="100%">
      <Stack gap="sm" h="100%">
        <Group justify="space-between" wrap="nowrap" align="flex-start">
          <Stack gap={2} style={{ minWidth: 0 }}>
            <Title order={5} style={{ overflowWrap: 'anywhere' }}>
              {name}
            </Title>
            <Group gap="xs">
              {pageLabel(citation) && (
                <Text size="sm" c="dimmed">
                  {pageLabel(citation)}
                </Text>
              )}
              <Anchor href={url} target="_blank" rel="noreferrer" size="sm">
                <Group gap={4}>
                  Open original <IconExternalLink size={14} />
                </Group>
              </Anchor>
            </Group>
          </Stack>
          <ActionIcon variant="subtle" color="gray" onClick={onClose} aria-label="Close source">
            <IconX size={18} />
          </ActionIcon>
        </Group>

        <Blockquote p="xs" radius="sm" color="yellow" styles={{ root: { fontSize: 'var(--mantine-font-size-sm)' } }}>
          “{citation.citedText.replace(/\s+/g, ' ').trim()}”
        </Blockquote>
        {citation.claim && (
          <Text size="xs" c="dimmed">
            Supports: {citation.claim}
          </Text>
        )}

        <Box style={{ flex: 1, minHeight: 0 }}>
          {/* Keyed by citation so page navigation starts over at the cited page for each new citation. */}
          {isPdf ? <PdfSource key={citationKey(citation)} url={url} citation={citation} /> : <TextSource url={url} citation={citation} />}
        </Box>
      </Stack>
    </Paper>
  )
}

function PdfSource({ url, citation }: { url: string; citation: Citation }) {
  const { ref, width } = useElementSize()
  const startPage = citation.pageNumber ?? 1
  const [page, setPage] = useState(startPage)
  const [pageCount, setPageCount] = useState<number | null>(null)
  const onPageCount = useCallback((count: number) => setPageCount(count), [])

  return (
    <Stack gap="xs" h="100%">
      <Group justify="space-between">
        <Text size="sm">
          Page {page}
          {pageCount !== null && ` of ${pageCount}`}
        </Text>
        <Group gap={4}>
          {page !== startPage && (
            <Button size="compact-xs" variant="subtle" onClick={() => setPage(startPage)}>
              Back to cited page
            </Button>
          )}
          <Tooltip label="Previous page">
            <ActionIcon variant="default" disabled={page <= 1} onClick={() => setPage(page - 1)} aria-label="Previous page">
              <IconChevronLeft size={16} />
            </ActionIcon>
          </Tooltip>
          <Tooltip label="Next page">
            <ActionIcon
              variant="default"
              disabled={pageCount !== null && page >= pageCount}
              onClick={() => setPage(page + 1)}
              aria-label="Next page"
            >
              <IconChevronRight size={16} />
            </ActionIcon>
          </Tooltip>
        </Group>
      </Group>
      <ScrollArea.Autosize mah="calc(100vh - 330px)" type="auto">
        <Box ref={ref}>
          <Suspense
            fallback={
              <Center py="xl">
                <Loader size="sm" />
              </Center>
            }
          >
            <PdfPage
              url={url}
              pageNumber={page}
              width={Math.max(width - 2, 0)}
              quote={page === startPage ? citation.citedText : ''}
              onPageCount={onPageCount}
            />
          </Suspense>
        </Box>
      </ScrollArea.Autosize>
    </Stack>
  )
}

function TextSource({ url, citation }: { url: string; citation: Citation }) {
  const text = useQuery({
    queryKey: ['record-text', url],
    queryFn: async () => {
      const response = await fetch(url)
      if (!response.ok) {
        throw new Error(`The record could not be loaded (${response.status}).`)
      }
      return response.text()
    },
    staleTime: Infinity,
  })
  const highlight = useRef<HTMLElement>(null)

  useEffect(() => {
    highlight.current?.scrollIntoView({ block: 'center', behavior: 'smooth' })
  }, [text.data, citation])

  if (text.isPending) {
    return (
      <Center py="xl">
        <Loader size="sm" />
      </Center>
    )
  }
  if (text.isError) {
    return <Alert color="red">{text.error.message}</Alert>
  }

  const range = locateInText(text.data, citation)
  return (
    <ScrollArea.Autosize mah="calc(100vh - 300px)" type="auto">
      {!range && (
        <Text size="xs" c="dimmed" mb="xs">
          The quote could not be located in this file, so it is not highlighted.
        </Text>
      )}
      <Text component="pre" size="sm" ff="monospace" style={{ whiteSpace: 'pre-wrap', margin: 0 }}>
        {range ? (
          <>
            {text.data.slice(0, range.start)}
            <Mark ref={highlight} color="yellow">
              {text.data.slice(range.start, range.end)}
            </Mark>
            {text.data.slice(range.end)}
          </>
        ) : (
          text.data
        )}
      </Text>
    </ScrollArea.Autosize>
  )
}
