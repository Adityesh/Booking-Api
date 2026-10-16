import { Link } from 'react-router'
import { PlaceholderPage } from '@/components/PlaceholderPage'

export function LoginPage() {
  return (
    <PlaceholderPage
      title="Sign in"
      description="Username and password. A 401 means wrong credentials; on success, signIn() stores the token and the guard redirects."
      endpoints={['POST /api/Auth/login']}
    >
      <p className="text-sm">
        No account?{' '}
        <Link to="/register" className="underline underline-offset-4">
          Register
        </Link>
      </p>
    </PlaceholderPage>
  )
}
