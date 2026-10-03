import { Route, Routes } from 'react-router'
import { FoundationPage } from '@/app/FoundationPage'

function App() {
  return (
    <Routes>
      <Route path="/" element={<FoundationPage />} />
      <Route path="*" element={<FoundationPage />} />
    </Routes>
  )
}

export default App
