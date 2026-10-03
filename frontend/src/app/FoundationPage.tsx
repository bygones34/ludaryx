import { Button } from '@/components/ui/button'

export function FoundationPage() {
  return (
    <main className="flex min-h-screen items-center justify-center bg-background px-6 py-12 text-foreground">
      <div className="w-full max-w-xl space-y-5 rounded-xl border bg-card p-8 text-card-foreground shadow-sm">
        <p className="text-sm font-medium text-muted-foreground">Ludaryx</p>
        <h1 className="text-3xl font-semibold tracking-tight">Your games. Your journey.</h1>
        <p className="text-muted-foreground">
          The project foundation is ready. Game discovery and your private library are coming next.
        </p>
        <Button type="button" disabled>
          Explore games soon
        </Button>
      </div>
    </main>
  )
}
