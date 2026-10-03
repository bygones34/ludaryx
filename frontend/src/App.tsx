import { Navigate, Route, Routes, useLocation } from 'react-router'
import { FoundationPage } from '@/app/FoundationPage'
import { LoginPage, RegisterPage } from '@/features/auth/AuthPages'
import { useAuth } from '@/features/auth/useAuth'

function ProtectedHome() {
  const { status } = useAuth()
  const location = useLocation()
  if (status === 'initializing') return <p role="status" className="p-8">Restoring your session…</p>
  if (status === 'anonymous') return <Navigate to="/login" replace state={{ from: location.pathname }} />
  return <FoundationPage />
}

function GuestPage({ children }: { children: React.ReactNode }) {
  const { status } = useAuth()
  if (status === 'initializing') return <p role="status" className="p-8">Restoring your session…</p>
  if (status === 'authenticated') return <Navigate to="/" replace />
  return children
}

function App() {
  return (
    <Routes>
      <Route path="/" element={<ProtectedHome />} />
      <Route path="/login" element={<GuestPage><LoginPage /></GuestPage>} />
      <Route path="/register" element={<GuestPage><RegisterPage /></GuestPage>} />
      <Route path="*" element={<Navigate to="/" replace />} />
    </Routes>
  )
}

export default App
