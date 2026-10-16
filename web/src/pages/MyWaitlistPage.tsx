import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Link } from 'react-router'
import { toast } from 'sonner'
import { client } from '@/api/client'
import type { WaitlistEntryResponse } from '@/api/bookings'
import { myWaitlistQuery } from '@/api/queries'
import { ConfirmDialog } from '@/components/ConfirmDialog'
import { ErrorAlert } from '@/components/ErrorAlert'
import { ResourceName } from '@/components/ResourceName'
import { StatusBadge } from '@/components/StatusBadge'
import { formatDateTime, formatDateTimeRange } from '@/lib/datetime'

const statusNotes: Record<WaitlistEntryResponse['status'], string> = {
  Waiting: "You'll be booked automatically if a place frees up before it starts.",
  Promoted: 'A place freed up and the booking was made for you.',
  Expired: 'The start time passed before a place freed up.',
  Withdrawn: 'You withdrew this request.',
}

// Waiting entries first (the ones you can still act on), then the newest.
function byRelevance(a: WaitlistEntryResponse, b: WaitlistEntryResponse) {
  const waiting = Number(b.status === 'Waiting') - Number(a.status === 'Waiting')
  return waiting !== 0 ? waiting : b.createdAt.localeCompare(a.createdAt)
}

export function MyWaitlistPage() {
  const { data, error, isPending } = useQuery(myWaitlistQuery())

  return (
    <section className="space-y-6">
      <div className="space-y-1">
        <h1 className="font-heading text-2xl font-semibold">My waitlist</h1>
        <p className="text-muted-foreground">Requests made while the resource was full.</p>
      </div>
      {isPending && <p className="text-muted-foreground">Loading…</p>}
      {error && <ErrorAlert>{error.message}</ErrorAlert>}
      {data?.length === 0 && (
        <p className="text-muted-foreground">
          You're not on any waitlist. <Link to="/resources" className="underline underline-offset-4">Browse resources</Link>
        </p>
      )}
      {data && data.length > 0 && (
        <ul className="divide-y rounded-lg border">
          {data.toSorted(byRelevance).map((entry) => (
            <WaitlistRow key={entry.id} entry={entry} />
          ))}
        </ul>
      )}
    </section>
  )
}

function WaitlistRow({ entry }: { entry: WaitlistEntryResponse }) {
  const queryClient = useQueryClient()
  const withdraw = useMutation({
    mutationFn: () => client.POST('/api/Waitlist/{id}/withdraw', { params: { path: { id: entry.id } } }),
    onSuccess: () => toast.success('Withdrawn from the waitlist.'),
    // 409: promoted or expired in the meantime. The refetch below shows which.
    onError: (error) => toast.error(error.message),
    onSettled: () => queryClient.invalidateQueries({ queryKey: myWaitlistQuery().queryKey }),
  })

  return (
    <li className="flex flex-wrap items-center gap-x-4 gap-y-2 px-4 py-3">
      <div className="min-w-0 flex-1 space-y-1">
        <div className="flex items-center gap-2 font-medium">
          <Link to={`/resources/${entry.resourceId}`} className="hover:underline">
            <ResourceName id={entry.resourceId} />
          </Link>
          <StatusBadge status={entry.status} />
        </div>
        <p className="text-sm">{formatDateTimeRange(entry.requestedStartTime, entry.requestedEndTime)}</p>
        <p className="text-muted-foreground text-xs">
          {statusNotes[entry.status]} Requested {formatDateTime(entry.createdAt)}.
        </p>
      </div>
      {entry.status === 'Waiting' && (
        <ConfirmDialog
          trigger={withdraw.isPending ? 'Withdrawing…' : 'Withdraw'}
          title="Withdraw from the waitlist?"
          description="You won't be booked automatically for this time any more."
          confirmLabel="Withdraw"
          onConfirm={() => withdraw.mutate()}
          disabled={withdraw.isPending}
        />
      )}
    </li>
  )
}
