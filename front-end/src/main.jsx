import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import './index.css'
import App from './App.jsx'
import { CartProvider } from './context/CartContext.jsx'
import { ClienteProvider } from './context/ClienteContext.jsx'
import { PedidosProvider } from './context/PedidosContext.jsx'

createRoot(document.getElementById('root')).render(
  <StrictMode>
    <ClienteProvider>
      <PedidosProvider>
        <CartProvider>
          <App />
        </CartProvider>
      </PedidosProvider>
    </ClienteProvider>
  </StrictMode>,
)
