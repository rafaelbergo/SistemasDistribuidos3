// Reserve a tab during the click, before the asynchronous order/SSE round trip.
export function reservarCheckout(browser = window) {
  let aba
  try {
    aba = browser.open('about:blank', '_blank')
    if (aba) {
      aba.opener = null
      aba.document.title = 'Aguardando pagamento'
      aba.document.body.textContent = 'Aguardando confirmação do estoque e criação do pagamento…'
    }
  } catch {
    aba?.close()
    aba = null
  }

  return {
    atualizar(eventos) {
      if (eventos.some((e) => ['estoque_indisponivel', 'pagamento_aprovado', 'pagamento_recusado', 'pedido_enviado'].includes(e.status))) {
        this.cancelar()
        return true
      }
      const checkout = eventos.find((e) => e.status === 'pagamento_pendente' && e.checkoutUrl)
      if (!checkout) return false
      if (aba && !aba.closed) aba.location.replace(checkout.checkoutUrl)
      aba = null
      return true
    },
    cancelar() {
      if (aba && !aba.closed) aba.close()
      aba = null
    },
  }
}
