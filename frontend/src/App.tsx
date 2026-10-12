import { Navigate, Route, Routes, useLocation } from 'react-router'
import { FoundationPage } from '@/app/FoundationPage'
import { LoginPage, RegisterPage } from '@/features/auth/AuthPages'
import { useAuth } from '@/features/auth/useAuth'
import { AppShell } from '@/app/AppShell'
import { GameSearchPage, GameDetailsPage } from '@/features/games/GamePages'
import { LibraryPage } from '@/features/library/LibraryPage'
import { LibraryItemPage } from '@/features/library/LibraryItemPage'

function ProtectedRoutes() {
  const { status } = useAuth()
  const location = useLocation()
  if (status === 'initializing') return <p role="status" className="p-8">Restoring your session…</p>
  if (status === 'anonymous') return <Navigate to="/login" replace state={{ from: location.pathname + location.search }} />
  return <AppShell />
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
      <Route element={<ProtectedRoutes />}>
        <Route path="/" element={<FoundationPage />} />
        <Route path="/games" element={<GameSearchPage />} />
        <Route path="/games/:igdbId" element={<GameDetailsPage />} />
        <Route path="/library" element={<LibraryPage />} />
        <Route path="/library/:id" element={<LibraryItemPage />} />
      </Route>
      <Route path="/login" element={<GuestPage><LoginPage /></GuestPage>} />
      <Route path="/register" element={<GuestPage><RegisterPage /></GuestPage>} />
      <Route path="*" element={<Navigate to="/" replace />} />
    </Routes>
  )
}

export default App
