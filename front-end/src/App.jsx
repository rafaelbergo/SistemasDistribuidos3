import { useState } from 'react'
import Catalogo from './screens/Catalogo'
import Carrinho from './screens/Carrinho'
import MeusPedidos from './screens/MeusPedidos'
import Promocoes from './screens/Promocoes'

const TABS = [
  { id: 'catalogo', label: 'Catálogo', Component: Catalogo },
  { id: 'carrinho', label: 'Carrinho', Component: Carrinho },
  { id: 'pedidos', label: 'Meus Pedidos', Component: MeusPedidos },
  { id: 'promocoes', label: 'Promoções', Component: Promocoes },
]

export default function App() {
  const [active, setActive] = useState('catalogo')
  const ActiveComponent = TABS.find((t) => t.id === active).Component

  return (
    <div className="min-h-screen bg-slate-50 text-slate-900">
      <header className="border-b border-slate-200 bg-white">
        <div className="mx-auto flex max-w-5xl items-center justify-between px-4 py-4">
          <h1 className="text-xl font-bold text-blue-700">🛒 e-Commerce</h1>
          <span className="text-sm text-slate-500">Sistemas Distribuídos · Trab2</span>
        </div>
        <nav className="mx-auto flex max-w-5xl gap-1 px-4">
          {TABS.map((tab) => (
            <button
              key={tab.id}
              type="button"
              onClick={() => setActive(tab.id)}
              className={`-mb-px border-b-2 px-4 py-2 text-sm font-medium transition-colors ${
                active === tab.id
                  ? 'border-blue-600 text-blue-700'
                  : 'border-transparent text-slate-500 hover:text-slate-800'
              }`}
            >
              {tab.label}
            </button>
          ))}
        </nav>
      </header>

      <main className="mx-auto max-w-5xl px-4 py-6">
        <ActiveComponent />
      </main>
    </div>
  )
}
