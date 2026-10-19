import test from 'node:test'
import assert from 'node:assert/strict'
import { reservarCheckout } from './checkout.js'

function browser() {
  const tab = { document: { body: {} }, closed: false, close() { this.closed = true }, location: { replace(url) { tab.url = url } } }
  return { tab, open: () => tab }
}

test('reserves immediately, waits for SSE and opens the returned checkout once', () => {
  const mock = browser()
  const checkout = reservarCheckout(mock)
  assert.equal(mock.tab.opener, null)
  assert.match(mock.tab.document.body.textContent, /Aguardando/)
  assert.equal(checkout.atualizar([{ status: 'estoque_confirmado' }]), false)
  assert.equal(checkout.atualizar([{ status: 'pagamento_pendente', checkoutUrl: 'http://localhost/checkout/123' }]), true)
  assert.equal(mock.tab.url, 'http://localhost/checkout/123')
  checkout.cancelar()
  assert.equal(mock.tab.closed, false)
})

test('closes the reserved tab on insufficient inventory or failed request', () => {
  const mock = browser()
  const checkout = reservarCheckout(mock)
  assert.equal(checkout.atualizar([{ status: 'estoque_indisponivel' }]), true)
  assert.equal(mock.tab.closed, true)
  const failed = browser()
  reservarCheckout(failed).cancelar()
  assert.equal(failed.tab.closed, true)
})

test('does not reopen a paid checkout when events are replayed', () => {
  const mock = browser()
  reservarCheckout(mock).atualizar([
    { status: 'pagamento_pendente', checkoutUrl: 'http://localhost/checkout/123' },
    { status: 'pagamento_aprovado' },
  ])
  assert.equal(mock.tab.closed, true)
  assert.equal(mock.tab.url, undefined)
})

test('blocked or manually closed tabs allow manual checkout fallback', () => {
  const blocked = reservarCheckout({ open: () => null })
  assert.equal(blocked.atualizar([{ status: 'pagamento_pendente', checkoutUrl: 'http://localhost/checkout/123' }]), true)
  const mock = browser()
  const checkout = reservarCheckout(mock)
  mock.tab.closed = true
  assert.equal(checkout.atualizar([{ status: 'pagamento_pendente', checkoutUrl: 'http://localhost/checkout/123' }]), true)
  assert.equal(mock.tab.url, undefined)
})
