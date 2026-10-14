import '@testing-library/jest-dom/vitest'

// JSDOM has no layout observer. Sortable behavior is verified with explicit
// element geometry in tests and real browser interaction during acceptance.
globalThis.ResizeObserver = class implements ResizeObserver {
  observe() {}
  unobserve() {}
  disconnect() {}
}
