import type { components } from '@/api/schema'
import { Badge } from '@/components/ui/badge'

type Status = components['schemas']['BookingStatus'] | components['schemas']['WaitlistStatus']

const variants: Record<Status, 'default' | 'secondary' | 'outline'> = {
  Confirmed: 'default',
  Promoted: 'default',
  Waiting: 'outline',
  Cancelled: 'secondary',
  Expired: 'secondary',
  Withdrawn: 'secondary',
}

export function StatusBadge({ status }: { status: Status }) {
  return <Badge variant={variants[status]}>{status}</Badge>
}
