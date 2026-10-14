import { createTheme, Modal, Tooltip } from '@mantine/core'
import type { CSSVariablesResolver, MantineColorsTuple } from '@mantine/core'

/**
 * Slate & Sage. Slate is the primary color (buttons, active states, text), sage marks selections and finished work,
 * and brick and amber are reserved for problems: contradictions and failures in brick, anything that needs a second
 * look in amber. Shade 6 of each is dark enough to carry text on the page background.
 */
const slate: MantineColorsTuple = ['#F0F2F2', '#DEE2E0', '#C1C9C5', '#9FACA7', '#7E8F88', '#596E65', '#2F4A3F', '#273D34', '#1F312A', '#182520']
const sage: MantineColorsTuple = ['#F1F5F0', '#E1EBDF', '#C6D8C2', '#A9C4A3', '#8FAF88', '#6F9168', '#4D6B48', '#3F583B', '#334730', '#263624']
const brick: MantineColorsTuple = ['#F8F2F0', '#F0E1DE', '#E3C6C1', '#D4A89F', '#C5897E', '#B56759', '#A2412F', '#853527', '#6B2B1F', '#512018']
const amber: MantineColorsTuple = ['#F7F4EE', '#EDE6D9', '#DDD0B7', '#CBB791', '#B99E6B', '#A58140', '#8E6210', '#74500D', '#5E410B', '#473108']
// Neutrals with a slight green bias, so borders and dimmed text belong to the palette.
const gray: MantineColorsTuple = ['#F7F8F5', '#EEF1EC', '#E2E7DF', '#C6CEC2', '#A9B3A5', '#8A958A', '#66726B', '#4F5D56', '#36423D', '#1E2A26']

export const theme = createTheme({
  colors: { slate, sage, brick, amber, gray },
  primaryColor: 'slate',
  primaryShade: 6,
  black: '#1E2A26',
  fontFamily: "'Public Sans Variable', 'Helvetica Neue', Arial, sans-serif",
  headings: { fontFamily: "'Public Sans Variable', 'Helvetica Neue', Arial, sans-serif", fontWeight: '650' },
  defaultRadius: 'md',
  radius: { md: '10px', lg: '16px' },
  components: {
    Modal: Modal.extend({ defaultProps: { overlayProps: { backgroundOpacity: 0.25, blur: 6 }, radius: 'lg' } }),
    // Tooltips also open on keyboard focus and on tap, so they work without a mouse.
    Tooltip: Tooltip.extend({
      defaultProps: { multiline: true, maw: 280, withArrow: true, openDelay: 300, color: 'slate', events: { hover: true, focus: true, touch: true } },
    }),
  },
})

// Cards and inputs sit on a pale sage surface rather than pure white, so the page reads as one palette.
export const cssVariablesResolver: CSSVariablesResolver = () => ({
  variables: {
    '--mra-font-reading': "'Source Serif 4 Variable', Georgia, 'Times New Roman', serif",
  },
  light: {
    '--mantine-color-body': '#F7F8F5',
    '--mra-page': '#E7ECE4',
    '--mra-glass': 'rgba(247, 248, 245, 0.55)',
    '--mra-glass-edge': 'rgba(255, 255, 255, 0.6)',
    '--mra-shadow': 'rgba(30, 42, 38, 0.10)',
    // The sidebar is a lighter pane of the same glass, and its selected row a solid card on it.
    '--mra-sidebar': 'rgba(247, 248, 245, 0.8)',
    '--mra-sidebar-hover': 'rgba(30, 42, 38, 0.05)',
    '--mra-sidebar-active': '#FFFFFF',
  },
  dark: {},
})
