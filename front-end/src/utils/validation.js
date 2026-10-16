// Validação simples de formato de e-mail: algo@algo.dominio, sem espaços.
const EMAIL_RE = /^[^\s@]+@[^\s@]+\.[^\s@]+$/

export function isEmailValido(email) {
  return EMAIL_RE.test(String(email).trim())
}
