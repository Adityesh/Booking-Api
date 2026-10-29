import { useMutation } from '@tanstack/react-query'
import { useState, type FormEvent } from 'react'
import { Link } from 'react-router'
import { client, fieldErrors, unwrap } from '@/api/client'
import type { components } from '@/api/schema'
import { useAuth } from '@/auth/authContext'
import { ErrorAlert } from '@/components/ErrorAlert'
import { Button } from '@/components/ui/button'
import { Field, FieldDescription, FieldError, FieldGroup, FieldLabel } from '@/components/ui/field'
import { Input } from '@/components/ui/input'
import { asFieldErrors } from '@/lib/forms'

type RegisterRequest = components['schemas']['RegisterDto']

export function RegisterPage() {
  const { signIn } = useAuth()
  // The API never sees the confirmation: it is a typo check that only the browser can do.
  const [confirmError, setConfirmError] = useState<string | null>(null)

  const register = useMutation({
    mutationFn: (body: RegisterRequest) => unwrap(client.POST('/api/Auth/register', { body })),
    // Registering returns a token too, so the new user is signed in straight away; <RedirectIfAuthenticated> moves on.
    onSuccess: (response) => signIn(response),
  })

  function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const form = new FormData(event.currentTarget)
    const password = String(form.get('password'))
    if (password !== String(form.get('confirmPassword'))) {
      setConfirmError('The passwords do not match.')
      register.reset() // drop the previous attempt's server errors; they no longer describe this input
      return
    }
    setConfirmError(null)
    register.mutate({ username: String(form.get('username')), email: String(form.get('email')).trim(), password })
  }

  const { error } = register
  // 409 "Username already taken" belongs to the username field, next to the 400 messages for it.
  const usernameErrors = [...fieldErrors(error, 'username'), ...(error?.kind === 'conflict' ? [error.message] : [])]
  const emailErrors = fieldErrors(error, 'email')
  const passwordErrors = fieldErrors(error, 'password')
  const formError = error && error.kind !== 'validation' && error.kind !== 'conflict' ? error.message : null

  return (
    <div className="space-y-6">
      <div className="space-y-1">
        <h1 className="font-heading text-xl font-semibold">Create an account</h1>
        <p className="text-muted-foreground text-sm">Choose a username, add your email and pick a password.</p>
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
              aria-describedby="username-hint"
            />
            {/* A hint only: the API's RegisterDtoValidator decides, and its messages show below. */}
            <FieldDescription id="username-hint">
              3–30 characters: letters, digits, _ . or -. Usernames are case-sensitive.
            </FieldDescription>
            <FieldError errors={asFieldErrors(usernameErrors)} />
          </Field>
          <Field data-invalid={emailErrors.length > 0 || undefined}>
            <FieldLabel htmlFor="email">Email</FieldLabel>
            <Input
              id="email"
              name="email"
              type="email"
              autoComplete="email"
              required
              aria-invalid={emailErrors.length > 0 || undefined}
              aria-describedby="email-hint"
            />
            <FieldDescription id="email-hint">Used for booking and waitlist notifications.</FieldDescription>
            <FieldError errors={asFieldErrors(emailErrors)} />
          </Field>
          <Field data-invalid={passwordErrors.length > 0 || undefined}>
            <FieldLabel htmlFor="password">Password</FieldLabel>
            <Input
              id="password"
              name="password"
              type="password"
              autoComplete="new-password"
              required
              aria-invalid={passwordErrors.length > 0 || undefined}
              aria-describedby="password-hint"
            />
            <FieldDescription id="password-hint">8–72 characters, including at least one of - _ or .</FieldDescription>
            <FieldError errors={asFieldErrors(passwordErrors)} />
          </Field>
          <Field data-invalid={confirmError ? true : undefined}>
            <FieldLabel htmlFor="confirmPassword">Confirm password</FieldLabel>
            <Input
              id="confirmPassword"
              name="confirmPassword"
              type="password"
              autoComplete="new-password"
              required
              aria-invalid={confirmError ? true : undefined}
            />
            <FieldError errors={confirmError ? [{ message: confirmError }] : undefined} />
          </Field>
        </FieldGroup>

        {formError && <ErrorAlert>{formError}</ErrorAlert>}

        <Button type="submit" className="w-full" disabled={register.isPending}>
          {register.isPending ? 'Creating account…' : 'Create account'}
        </Button>
      </form>

      <p className="text-muted-foreground text-center text-sm">
        Already registered?{' '}
        <Link to="/login" className="text-foreground underline underline-offset-4">
          Sign in
        </Link>
      </p>
    </div>
  )
}
