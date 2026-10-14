import { describe, expect, it } from 'vitest'
import { isMultiline, placeholderLabel } from './letter'

describe('placeholderLabel', () => {
  it('turns a placeholder into a sentence-case label', () => {
    expect(placeholderLabel('[CLAIM NUMBER]')).toBe('Claim number')
    expect(placeholderLabel('[LAW FIRM NAME]')).toBe('Law firm name')
  })

  it('names the letter date plainly', () => {
    expect(placeholderLabel('[DATE]')).toBe('Date of the letter')
  })
})

describe('isMultiline', () => {
  it('gives addresses and lists several lines', () => {
    expect(isMultiline('[INSURER ADDRESS]')).toBe(true)
    expect(isMultiline('[LIST OF ENCLOSURES]')).toBe(true)
    expect(isMultiline('[CLAIM NUMBER]')).toBe(false)
  })
})

