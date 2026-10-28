import { Navigate, Route, Routes, useLocation } from 'react-router'
import { lazy, Suspense } from 'react'
import { LoginPage, RegisterPage } from '@/features/auth/AuthPages'
import { useAuth } from '@/features/auth/useAuth'
import { AppShell } from '@/app/AppShell'
import { GameSearchPage, GameDetailsPage } from '@/features/games/GamePages'
import { LibraryPage } from '@/features/library/LibraryPage'
import { LibraryItemPage } from '@/features/library/LibraryItemPage'
import { ThemeProvider } from '@/app/theme/ThemeProvider'
import { ThemeSelector } from '@/app/theme/ThemeSelector'
import { ToastProvider } from '@/components/feedback/ToastProvider'
import { LoadingState } from '@/components/feedback/QueryState'

const DashboardPage = lazy(() => import('@/features/dashboard/DashboardPage')
  .then(module => ({ default: module.DashboardPage })))
const BacklogPage = lazy(() => import('@/features/backlog/BacklogPage')
  .then(module => ({ default: module.BacklogPage })))

function RestoringSession() {
  return <main className="min-h-screen space-y-6 p-8">
    <ThemeSelector />
    <LoadingState message="Restoring your session…" layout="compact" />
  </main>
}

function ProtectedRoutes() {
  const { status } = useAuth()
  const location = useLocation()
  if (status === 'initializing') return <RestoringSession />
  if (status === 'anonymous') return <Navigate to="/login" replace state={{ from: location.pathname + location.search }} />
  return <AppShell />
}

function GuestPage({ children }: { children: React.ReactNode }) {
  const { status } = useAuth()
  const location = useLocation()
  const from = (location.state as { from?: string } | null)?.from ?? '/'
  if (status === 'initializing') return <RestoringSession />
  if (status === 'authenticated') return <Navigate to={from} replace />
  return children
}

function App() {
  const { status, user } = useAuth()
  const scope = status === 'authenticated' ? `account:${user?.id}` : status
  return (
    <ThemeProvider><ToastProvider key={scope}><Routes>
      <Route element={<ProtectedRoutes />}>
        <Route path="/" element={<Suspense fallback={<LoadingState message="Loading your dashboard…" layout="dashboard" />}><DashboardPage /></Suspense>} />
        <Route path="/games" element={<GameSearchPage />} />
        <Route path="/games/:igdbId" element={<GameDetailsPage />} />
        <Route path="/library" element={<LibraryPage />} />
        <Route path="/library/:id" element={<LibraryItemPage />} />
        <Route path="/backlog" element={<Suspense fallback={<LoadingState message="Loading your backlog…" layout="rows" />}><BacklogPage /></Suspense>} />
      </Route>
      <Route path="/login" element={<GuestPage><LoginPage /></GuestPage>} />
      <Route path="/register" element={<GuestPage><RegisterPage /></GuestPage>} />
      <Route path="*" element={<Navigate to="/" replace />} />
    </Routes></ToastProvider></ThemeProvider>
  )
}

export default App
