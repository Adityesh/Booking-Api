import { Link } from 'react-router'
import { PlaceholderPage } from '@/components/PlaceholderPage'

export function RegisterPage() {
  return (
    <PlaceholderPage
      title="Create an account"
      description="Field errors come from the 400 ProblemDetails; a 409 means the username is taken. Registration signs the user in."
      endpoints={['POST /api/Auth/register']}
    >
      <p className="text-sm">
        Already registered?{' '}
        <Link to="/login" className="underline underline-offset-4">
          Sign in
        </Link>
      </p>
    </PlaceholderPage>
  )
}
