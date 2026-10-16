import { useMutation } from '@tanstack/react-query'
import type { FormEvent } from 'react'
import { Link } from 'react-router'
import { client, fieldErrors, unwrap } from '@/api/client'
import type { components } from '@/api/schema'
import { useAuth } from '@/auth/authContext'
import { Button } from '@/components/ui/button'
import { Field, FieldError, FieldGroup, FieldLabel } from '@/components/ui/field'
import { Input } from '@/components/ui/input'
import { asFieldErrors } from '@/lib/forms'

type LoginRequest = components['schemas']['LoginDto']

export function LoginPage() {
  const { signIn } = useAuth()
  const login = useMutation({
    mutationFn: (body: LoginRequest) => unwrap(client.POST('/api/Auth/login', { body })),
    // No navigate() here: once the user is set, <RedirectIfAuthenticated> sends them back to where they came from.
    onSuccess: (response) => signIn(response),
  })

  function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const form = new FormData(event.currentTarget)
    login.mutate({ username: String(form.get('username')), password: String(form.get('password')) })
  }

  const usernameErrors = fieldErrors(login.error, 'username')
  const passwordErrors = fieldErrors(login.error, 'password')
  // 401 ("Incorrect credentials"), network failure, 500: one message for the whole form. Field errors show inline.
  const formError = login.error && login.error.kind !== 'validation' ? login.error.message : null

  return (
    <div className="space-y-6">
      <div className="space-y-1">
        <h1 className="font-heading text-xl font-semibold">Sign in</h1>
        <p className="text-muted-foreground text-sm">Enter your username and password.</p>
      </div>

      <form onSubmit={handleSubmit} className="space-y-6">
        <FieldGroup>
          <Field data-invalid={usernameErrors.length > 0 || undefined}>
            <FieldLabel htmlFor="username">Username</FieldLabel>
            <Input
              id="username"
              name="username"
              autoComplete="username"
              autoCapitalize="none"
              spellCheck={false}
              required
              aria-invalid={usernameErrors.length > 0 || undefined}
            />
            <FieldError errors={asFieldErrors(usernameErrors)} />
          </Field>
          <Field data-invalid={passwordErrors.length > 0 || undefined}>
            <FieldLabel htmlFor="password">Password</FieldLabel>
            <Input
              id="password"
              name="password"
              type="password"
              autoComplete="current-password"
              required
              aria-invalid={passwordErrors.length > 0 || undefined}
            />
            <FieldError errors={asFieldErrors(passwordErrors)} />
          </Field>
        </FieldGroup>

        {formError && (
          <p role="alert" className="bg-destructive/10 text-destructive rounded-lg px-3 py-2 text-sm">
            {formError}
          </p>
        )}

        <Button type="submit" className="w-full" disabled={login.isPending}>
          {login.isPending ? 'Signing in…' : 'Sign in'}
        </Button>
      </form>

      <p className="text-muted-foreground text-center text-sm">
        No account?{' '}
        <Link to="/register" className="text-foreground underline underline-offset-4">
          Register
        </Link>
      </p>
    </div>
  )
}
