// Placeholders whose names read poorly as labels on their own.
const labels: Record<string, string> = {
  '[DATE]': 'Date of the letter',
}

/** A placeholder's name as a field label: "[CLAIM NUMBER]" becomes "Claim number". */
export function placeholderLabel(placeholder: string): string {
  const known = labels[placeholder]
  if (known) {
    return known
  }
  const words = placeholder.replace(/^\[|\]$/g, '').toLowerCase()
  return words.charAt(0).toUpperCase() + words.slice(1)
}

/** Whether a placeholder usually takes several lines, such as an address or a list of enclosures. */
export function isMultiline(placeholder: string): boolean {
  return /ADDRESS|LIST/.test(placeholder)
}

