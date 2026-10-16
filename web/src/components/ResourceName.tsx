import { useQuery } from '@tanstack/react-query'
import { resourceQuery } from '@/api/queries'

// Bookings and waitlist entries only carry a resourceId. Each distinct id is fetched once and cached; if the resource
// can't be read (a user gets 404 for an inactive one), the id is shown instead.
export function ResourceName({ id }: { id: number }) {
  const { data } = useQuery(resourceQuery(id))
  return <>{data?.name ?? `Resource #${id}`}</>
}
