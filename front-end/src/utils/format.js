// Currency formatter (Brazilian Real).
const brl = new Intl.NumberFormat('pt-BR', { style: 'currency', currency: 'BRL' })

export function formatBRL(value) {
  return brl.format(Number.isFinite(value) ? value : 0)
}
