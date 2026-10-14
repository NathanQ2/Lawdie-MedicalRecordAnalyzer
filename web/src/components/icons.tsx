import type { ReactNode, SVGProps } from 'react'

export interface IconProps extends Omit<SVGProps<SVGSVGElement>, 'stroke'> {
  size?: number | string
  /** Stroke width in pixels on the 24px grid. */
  stroke?: number
}

/**
 * The app's own icon set: one drawing style on a 24px grid, rounded 1.6px strokes, no fills. Icons take their color
 * from the surrounding text, so they follow the theme.
 */
function Svg({ size = 18, stroke = 1.6, children, ...props }: IconProps & { children: ReactNode }) {
  return (
    <svg
      xmlns="http://www.w3.org/2000/svg"
      width={size}
      height={size}
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth={stroke}
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden="true"
      focusable="false"
      {...props}
    >
      {children}
    </svg>
  )
}

// Pages of the case workspace
export function IconOverview(props: IconProps) {
  return (
    <Svg {...props}>
      <rect x="3.5" y="3.5" width="7" height="7" rx="1.8" />
      <rect x="13.5" y="3.5" width="7" height="7" rx="1.8" />
      <rect x="3.5" y="13.5" width="7" height="7" rx="1.8" />
      <path d="M13.5 15h7M13.5 19h4.5" />
    </Svg>
  )
}

export function IconChronology(props: IconProps) {
  return (
    <Svg {...props}>
      <rect x="3.5" y="5" width="17" height="15.5" rx="2.5" />
      <path d="M3.5 9.5h17M8 3v4M16 3v4" />
      <path d="M7.5 13.5h2M11 13.5h2M14.5 13.5h2M7.5 17h2M11 17h2" />
    </Svg>
  )
}

// Steps of the workflow
export function IconMatter(props: IconProps) {
  return (
    <Svg {...props}>
      <rect x="3" y="7" width="18" height="13" rx="2.5" />
      <path d="M8.5 7V5.5a2 2 0 0 1 2-2h3a2 2 0 0 1 2 2V7" />
      <path d="M3 12.5h18" />
      <path d="M10.5 12.5V14h3v-1.5" />
    </Svg>
  )
}

export function IconDocuments(props: IconProps) {
  return (
    <Svg {...props}>
      <path d="M9 3.5h6l4 4V17a2 2 0 0 1-2 2H9a2 2 0 0 1-2-2V5.5a2 2 0 0 1 2-2z" />
      <path d="M15 3.5v4h4" />
      <path d="M4.5 7.5V19a2 2 0 0 0 2 2h8" />
    </Svg>
  )
}

export function IconAnalyze(props: IconProps) {
  return (
    <Svg {...props}>
      <circle cx="10.5" cy="10.5" r="6.5" />
      <path d="M20 20l-4.6-4.6" />
      <path d="M7.8 9h5.4M7.8 12h3.4" />
    </Svg>
  )
}

export function IconReview(props: IconProps) {
  return (
    <Svg {...props}>
      <path d="M10 6.5h10M10 12h10M10 17.5h6" />
      <path d="M3.8 6.6l1.4 1.4 2.4-2.6M3.8 12.1l1.4 1.4 2.4-2.6" />
      <circle cx="5.4" cy="17.5" r="1.4" />
    </Svg>
  )
}

export function IconLetter(props: IconProps) {
  return (
    <Svg {...props}>
      <rect x="3" y="5" width="18" height="14" rx="2.5" />
      <path d="M3.5 7l8.5 6 8.5-6" />
    </Svg>
  )
}

// Review states
export function IconConflict(props: IconProps) {
  return (
    <Svg {...props}>
      <path d="M12 21v-7" />
      <path d="M12 14L6.5 6.5M12 14l5.5-7.5" />
      <path d="M6.5 10V6.5H10M17.5 10V6.5H14" />
    </Svg>
  )
}

export function IconScan(props: IconProps) {
  return (
    <Svg {...props}>
      <path d="M4 8.5v-3A1.5 1.5 0 0 1 5.5 4h3M15.5 4h3A1.5 1.5 0 0 1 20 5.5v3M20 15.5v3a1.5 1.5 0 0 1-1.5 1.5h-3M8.5 20h-3A1.5 1.5 0 0 1 4 18.5v-3" />
      <path d="M7.5 9.5c1.5-1 2.5 1 4 0s2.5 1 4 0M7.5 14c1.5-1 2.5 1 4 0" />
    </Svg>
  )
}

export function IconPlaceholder(props: IconProps) {
  return (
    <Svg {...props}>
      <path d="M8.5 4h-3v16h3M15.5 4h3v16h-3" />
      <path d="M10 12h.01M14 12h.01" />
    </Svg>
  )
}

export function IconUncited(props: IconProps) {
  return (
    <Svg {...props}>
      <path d="M4 6.5h16M4 11.5h10M4 16.5h7" />
      <path d="M17 14.5l4 4M21 14.5l-4 4" />
    </Svg>
  )
}

export function IconSource(props: IconProps) {
  return <Svg {...props}><path d="M7 3.5h10a1.5 1.5 0 0 1 1.5 1.5v15.5L12 16.5l-6.5 4V5A1.5 1.5 0 0 1 7 3.5z" /></Svg>
}

// Actions
export function IconCheck(props: IconProps) {
  return <Svg {...props}><path d="M5 12.5l4.5 4.5L19 7.5" /></Svg>
}

export function IconClose(props: IconProps) {
  return <Svg {...props}><path d="M6.5 6.5l11 11M17.5 6.5l-11 11" /></Svg>
}

export function IconPlus(props: IconProps) {
  return <Svg {...props}><path d="M12 5v14M5 12h14" /></Svg>
}

export function IconPencil(props: IconProps) {
  return (
    <Svg {...props}>
      <path d="M4 20l1-4.5L15.5 5a2.1 2.1 0 0 1 3 3L8 18.5z" />
      <path d="M13.5 7l3 3" />
    </Svg>
  )
}

export function IconTrash(props: IconProps) {
  return (
    <Svg {...props}>
      <path d="M4.5 7h15" />
      <path d="M9.5 7V5a1.5 1.5 0 0 1 1.5-1.5h2A1.5 1.5 0 0 1 14.5 5v2" />
      <path d="M6.5 7l.8 11.6A2 2 0 0 0 9.3 20.5h5.4a2 2 0 0 0 2-1.9L17.5 7" />
      <path d="M10.3 11v5.5M13.7 11v5.5" />
    </Svg>
  )
}

export function IconOpen(props: IconProps) {
  return (
    <Svg {...props}>
      <path d="M14 4h6v6M20 4l-8.5 8.5" />
      <path d="M18 14v4.5a1.5 1.5 0 0 1-1.5 1.5h-11A1.5 1.5 0 0 1 4 18.5v-11A1.5 1.5 0 0 1 5.5 6H10" />
    </Svg>
  )
}

export function IconLocate(props: IconProps) {
  return (
    <Svg {...props}>
      <circle cx="12" cy="12" r="3" />
      <path d="M4 8.5v-3A1.5 1.5 0 0 1 5.5 4h3M15.5 4h3A1.5 1.5 0 0 1 20 5.5v3M20 15.5v3a1.5 1.5 0 0 1-1.5 1.5h-3M8.5 20h-3A1.5 1.5 0 0 1 4 18.5v-3" />
    </Svg>
  )
}

export function IconUndo(props: IconProps) {
  return (
    <Svg {...props}>
      <path d="M9 14L4.5 9.5 9 5" />
      <path d="M4.5 9.5H15a4.5 4.5 0 0 1 0 9h-2" />
    </Svg>
  )
}

export function IconRun(props: IconProps) {
  return <Svg {...props}><path d="M8 5.5v13a1 1 0 0 0 1.5.9l10-6.5a1 1 0 0 0 0-1.8l-10-6.5A1 1 0 0 0 8 5.5z" /></Svg>
}

export function IconStop(props: IconProps) {
  return <Svg {...props}><rect x="6" y="6" width="12" height="12" rx="2" /></Svg>
}

export function IconRerun(props: IconProps) {
  return (
    <Svg {...props}>
      <path d="M19.5 12a7.5 7.5 0 1 1-2.2-5.3" />
      <path d="M19.5 4.5v4h-4" />
    </Svg>
  )
}

export function IconDownload(props: IconProps) {
  return (
    <Svg {...props}>
      <path d="M12 4v11M7.5 10.5L12 15l4.5-4.5" />
      <path d="M5 14v4.5A1.5 1.5 0 0 0 6.5 20h11a1.5 1.5 0 0 0 1.5-1.5V14" />
    </Svg>
  )
}

export function IconUpload(props: IconProps) {
  return (
    <Svg {...props}>
      <path d="M12 15V4M7.5 8.5L12 4l4.5 4.5" />
      <path d="M5 14v4.5A1.5 1.5 0 0 0 6.5 20h11a1.5 1.5 0 0 0 1.5-1.5V14" />
    </Svg>
  )
}

export function IconHelp(props: IconProps) {
  return (
    <Svg {...props}>
      <circle cx="12" cy="12" r="8.5" />
      <path d="M9.6 9.5a2.5 2.5 0 0 1 4.8.9c0 1.7-2.4 2.1-2.4 3.6" />
      <path d="M12 17h.01" />
    </Svg>
  )
}

// Navigation
export function IconBack(props: IconProps) {
  return <Svg {...props}><path d="M19 12H5M11 6l-6 6 6 6" /></Svg>
}

export function IconChevronLeft(props: IconProps) {
  return <Svg {...props}><path d="M14.5 6l-6 6 6 6" /></Svg>
}

export function IconChevronRight(props: IconProps) {
  return <Svg {...props}><path d="M9.5 6l6 6-6 6" /></Svg>
}

export function IconMore(props: IconProps) {
  return <Svg {...props}><path d="M12 5.5h.01M12 12h.01M12 18.5h.01" strokeWidth={2.6} /></Svg>
}
