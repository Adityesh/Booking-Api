import type { ReactNode } from 'react'

export function ErrorAlert({ children }: { children: ReactNode }) {
  return (
    <p role="alert" className="bg-destructive/10 text-destructive rounded-lg px-3 py-2 text-sm">
      {children}
    </p>
  )
}
