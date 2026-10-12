import { Alert, Box, Center, Loader, Text } from '@mantine/core'
import { getDocument, GlobalWorkerOptions, TextLayer, type PDFDocumentLoadingTask, type PDFDocumentProxy, type RenderTask } from 'pdfjs-dist'
import workerUrl from 'pdfjs-dist/build/pdf.worker.min.mjs?url'
import { useEffect, useRef, useState } from 'react'
import { findQuoteRange, type RunRange } from '../lib/citations'
import './pdfTextLayer.css'

GlobalWorkerOptions.workerSrc = workerUrl

// Keep a few recently opened records loaded, so moving between citations does not reload the file.
const MAX_CACHED_DOCUMENTS = 6
const loadingTasks = new Map<string, PDFDocumentLoadingTask>()

function loadDocument(url: string): Promise<PDFDocumentProxy> {
  let task = loadingTasks.get(url)
  if (task) {
    loadingTasks.delete(url)
  } else {
    const created = getDocument({ url })
    created.promise.catch(() => loadingTasks.delete(url))
    task = created
  }
  loadingTasks.set(url, task)

  while (loadingTasks.size > MAX_CACHED_DOCUMENTS) {
    const [oldestUrl, oldest] = loadingTasks.entries().next().value!
    loadingTasks.delete(oldestUrl)
    void oldest.destroy()
  }
  return task.promise
}

/**
 * Wraps the quoted text in highlight spans: whole runs in the middle, and only the quoted part of the first and
 * last runs (as pdf.js's own search highlighting does). Returns the first highlight.
 */
function highlightRuns(divs: HTMLElement[], runs: string[], range: RunRange): HTMLElement | null {
  let firstHighlight: HTMLElement | null = null
  for (let index = range.first; index <= range.last; index++) {
    const div = divs[index]
    const text = runs[index] ?? ''
    if (!div) {
      continue
    }

    const start = index === range.first ? range.firstOffset : 0
    const end = index === range.last ? range.lastEnd : text.length
    const highlight = document.createElement('span')
    highlight.className = 'mra-highlight'
    highlight.textContent = text.slice(start, end)
    div.replaceChildren(text.slice(0, start), highlight, text.slice(end))
    firstHighlight ??= highlight
  }
  return firstHighlight
}

export interface PdfPageProps {
  url: string
  pageNumber: number
  width: number
  quote: string
  onPageCount: (pageCount: number) => void
}

/**
 * Renders one PDF page with a selectable text layer and highlights the text runs matching the quote.
 */
export default function PdfPage({ url, pageNumber, width, quote, onPageCount }: PdfPageProps) {
  const canvasRef = useRef<HTMLCanvasElement>(null)
  const textLayerRef = useRef<HTMLDivElement>(null)
  const [state, setState] = useState<'loading' | 'ready' | 'error'>('loading')
  const [error, setError] = useState<string | null>(null)
  const [quoteFound, setQuoteFound] = useState(true)

  useEffect(() => {
    const canvas = canvasRef.current
    const textLayerDiv = textLayerRef.current
    if (!canvas || !textLayerDiv || width <= 0) {
      return
    }

    let cancelled = false
    let renderTask: RenderTask | null = null
    let textLayer: TextLayer | null = null
    setState('loading')

    void (async () => {
      try {
        const pdf = await loadDocument(url)
        if (cancelled) {
          return
        }
        onPageCount(pdf.numPages)

        const page = await pdf.getPage(Math.min(Math.max(pageNumber, 1), pdf.numPages))
        if (cancelled) {
          return
        }
        const viewport = page.getViewport({ scale: width / page.getViewport({ scale: 1 }).width })

        // Render at device resolution so text stays sharp on high-density screens.
        const outputScale = window.devicePixelRatio || 1
        canvas.width = Math.floor(viewport.width * outputScale)
        canvas.height = Math.floor(viewport.height * outputScale)
        canvas.style.width = `${Math.floor(viewport.width)}px`
        canvas.style.height = `${Math.floor(viewport.height)}px`
        renderTask = page.render({
          canvas,
          viewport,
          transform: outputScale === 1 ? undefined : [outputScale, 0, 0, outputScale, 0, 0],
        })
        await renderTask.promise

        textLayerDiv.replaceChildren()
        textLayerDiv.style.setProperty('--total-scale-factor', String(viewport.scale))
        textLayer = new TextLayer({ textContentSource: page.streamTextContent(), container: textLayerDiv, viewport })
        await textLayer.render()
        if (cancelled) {
          return
        }

        const range = findQuoteRange(textLayer.textContentItemsStr, quote)
        setQuoteFound(range !== null)
        if (range) {
          const first = highlightRuns(textLayer.textDivs, textLayer.textContentItemsStr, range)
          first?.scrollIntoView({ block: 'center', behavior: 'smooth' })
        }
        setState('ready')
      } catch (caught) {
        if (!cancelled && (caught as Error)?.name !== 'RenderingCancelledException') {
          setError((caught as Error)?.message ?? 'The PDF could not be displayed.')
          setState('error')
        }
      }
    })()

    return () => {
      cancelled = true
      renderTask?.cancel()
      textLayer?.cancel()
    }
  }, [url, pageNumber, width, quote, onPageCount])

  return (
    <Box>
      {state === 'error' && <Alert color="brick" title="The PDF could not be displayed">{error}</Alert>}
      {state === 'ready' && !quoteFound && (
        <Text size="xs" c="dimmed" mb="xs">
          The quote could not be located on this page's text, so it is not highlighted.
        </Text>
      )}
      <Box pos="relative" className="mra-pdf-page" style={{ display: state === 'error' ? 'none' : undefined }}>
        <canvas ref={canvasRef} />
        <div ref={textLayerRef} className="textLayer" />
        {state === 'loading' && (
          <Center pos="absolute" inset={0}>
            <Loader size="sm" />
          </Center>
        )}
      </Box>
    </Box>
  )
}
