import { useState } from 'react'
import { registrarInteresse, cancelarInteresse } from '../api/client'
import { useCliente } from '../context/ClienteContext'
import { isEmailValido } from '../utils/validation'

// Categorias de promoção publicadas pelo MS.Promocoes (routing keys
// promocao.categoria.A | .B | .C). Os exemplos são ilustrativos.
const CATEGORIAS = [
  { id: 'A', nome: 'Eletrônicos', exemplo: 'Smartphone, Notebook, Monitor, Smart TV' },
  { id: 'B', nome: 'Periféricos & Áudio', exemplo: 'Fone, Teclado, Mouse, Headset' },
  { id: 'C', nome: 'Móveis & Escritório', exemplo: 'Cadeira, Mesa, Suporte, Luminária' },
]

export default function Promocoes() {
  const { clienteId, setClienteId } = useCliente()
  const [email, setEmail] = useState(clienteId)
  const [categorias, setCategorias] = useState([])
  const [enviando, setEnviando] = useState(false)
  const [feedback, setFeedback] = useState(null) // { tipo: 'ok'|'erro', msg }

  function toggleCategoria(id) {
    setCategorias((prev) =>
      prev.includes(id) ? prev.filter((c) => c !== id) : [...prev, id],
    )
  }

  async function inscrever() {
    setFeedback(null)
    const mail = email.trim().toLowerCase()
    if (!isEmailValido(mail)) {
      setFeedback({ tipo: 'erro', msg: 'Informe um e-mail válido (ex.: cliente@email.com).' })
      return
    }
    if (categorias.length === 0) {
      setFeedback({ tipo: 'erro', msg: 'Selecione ao menos uma categoria.' })
      return
    }

    setEnviando(true)
    try {
      const resposta = await registrarInteresse({ email: mail, categorias })
      setEmail(resposta.email)
      setCategorias(resposta.categorias)
      setClienteId(mail)
      setFeedback({
        tipo: 'ok',
        msg: `Solicitação de inscrição/atualização enviada para ${resposta.email} nas categorias ${resposta.categorias.join(', ')}.`,
      })
    } catch (e) {
      setFeedback({ tipo: 'erro', msg: e.message })
    } finally {
      setEnviando(false)
    }
  }

  async function cancelar() {
    setFeedback(null)
    const mail = email.trim().toLowerCase()
    if (!isEmailValido(mail)) {
      setFeedback({ tipo: 'erro', msg: 'Informe um e-mail válido para cancelar.' })
      return
    }

    setEnviando(true)
    try {
      await cancelarInteresse(mail)
      setCategorias([])
      setFeedback({ tipo: 'ok', msg: `Solicitação de cancelamento enviada para ${mail}.` })
    } catch (e) {
      setFeedback({ tipo: 'erro', msg: e.message })
    } finally {
      setEnviando(false)
    }
  }

  return (
    <section>
      <h2 className="mb-1 text-lg font-semibold">Promoções</h2>
      <p className="mb-4 text-sm text-slate-500">
        Cadastre seu e-mail e escolha as categorias de interesse para receber ofertas.
      </p>

      <div className="max-w-xl rounded-lg border border-slate-200 bg-white p-4">
        <label htmlFor="promo-email" className="block text-sm font-medium text-slate-700">
          E-mail
        </label>
        <input
          id="promo-email"
          type="email"
          value={email}
          onChange={(e) => { setEmail(e.target.value); setFeedback(null) }}
          disabled={enviando}
          placeholder="cliente@email.com"
          className="mt-1 w-full rounded-md border border-slate-300 px-3 py-2 text-sm outline-none focus:border-blue-500 focus:ring-1 focus:ring-blue-500"
        />

        <p className="mt-4 mb-2 text-sm font-medium text-slate-700">Categorias de interesse</p>
        <div className="grid gap-2 sm:grid-cols-3">
          {CATEGORIAS.map((cat) => {
            const marcada = categorias.includes(cat.id)
            return (
              <button
                key={cat.id}
                type="button"
                onClick={() => toggleCategoria(cat.id)}
                disabled={enviando}
                aria-pressed={marcada}
                className={`rounded-lg border p-3 text-left transition-colors ${
                  marcada
                    ? 'border-blue-500 bg-blue-50'
                    : 'border-slate-200 bg-white hover:border-slate-300'
                }`}
              >
                <span className="flex items-center justify-between">
                  <span className="font-medium">{cat.nome}</span>
                  <span
                    className={`flex h-5 w-5 items-center justify-center rounded border text-xs ${
                      marcada ? 'border-blue-600 bg-blue-600 text-white' : 'border-slate-300'
                    }`}
                  >
                    {marcada ? '✓' : ''}
                  </span>
                </span>
                <span className="mt-1 block text-xs text-slate-500">
                  Categoria {cat.id} · {cat.exemplo}
                </span>
              </button>
            )
          })}
        </div>

        {feedback && (
          <p
            className={`mt-4 rounded-md p-2 text-sm ${
              feedback.tipo === 'ok'
                ? 'bg-green-50 text-green-700'
                : 'bg-red-50 text-red-700'
            }`}
          >
            {feedback.msg}
          </p>
        )}

        <div className="mt-4 flex flex-wrap gap-2">
          <button
            type="button"
            onClick={inscrever}
            disabled={enviando}
            className="rounded-md bg-blue-600 px-4 py-2 text-sm font-medium text-white hover:bg-blue-700 disabled:cursor-not-allowed disabled:bg-slate-300"
          >
            {enviando ? 'Enviando…' : 'Inscrever / atualizar'}
          </button>
          <button
            type="button"
            onClick={cancelar}
            disabled={enviando}
            className="rounded-md border border-slate-300 px-4 py-2 text-sm text-slate-600 hover:bg-slate-100 disabled:cursor-not-allowed disabled:opacity-50"
          >
            Cancelar inscrição
          </button>
        </div>
      </div>
    </section>
  )
}
