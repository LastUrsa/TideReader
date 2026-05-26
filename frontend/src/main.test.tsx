import React from 'react'
import { beforeEach, describe, expect, it, vi } from 'vitest'

const renderMock = vi.fn()
const createRootMock = vi.fn(() => ({
  render: renderMock,
}))

vi.mock('react-dom/client', () => ({
  createRoot: createRootMock,
}))

vi.mock('./App', () => ({
  default: () => null,
}))

describe('main', () => {
  beforeEach(() => {
    document.body.innerHTML = '<div id="root"></div>'
    window.sessionStorage.clear()
    window.history.replaceState({}, '', '/?tr_token=boot-token')
    renderMock.mockClear()
    createRootMock.mockClear()
    vi.resetModules()
  })

  it('bootstraps the app into the root container', async () => {
    await import('./main')

    expect(createRootMock).toHaveBeenCalledWith(document.getElementById('root'))
    expect(renderMock).toHaveBeenCalledOnce()
    expect(window.sessionStorage.getItem('tidereader.local_api_token')).toBe('boot-token')
    expect(window.location.search).toBe('')

    const renderedElement = renderMock.mock.calls[0]?.[0]
    expect(renderedElement.type).toBe(React.StrictMode)
  })
})
