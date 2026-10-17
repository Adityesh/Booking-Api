import { Button } from '@/components/ui/button'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { pageSizes, type PageSize } from '@/lib/paging'

type PaginationProps = {
  page: number
  pageSize: number
  count: number
  shown: number
  // The next page is loading and `page` still describes the previous one: block clicks until it arrives.
  busy: boolean
  onPage: (page: number) => void
  onPageSize: (pageSize: PageSize) => void
}

export function Pagination({ page, pageSize, count, shown, busy, onPage, onPageSize }: PaginationProps) {
  const totalPages = Math.max(1, Math.ceil(count / pageSize))
  const first = (page - 1) * pageSize + 1

  return (
    <div className="flex flex-wrap items-center justify-between gap-4 pt-4 text-sm">
      <p className="text-muted-foreground tabular-nums">
        {first}–{first + shown - 1} of {count}
      </p>
      <div className="flex items-center gap-2">
        <Select value={String(pageSize)} onValueChange={(value) => onPageSize(Number(value) as PageSize)}>
          <SelectTrigger size="sm" aria-label="Rows per page">
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            {pageSizes.map((size) => (
              <SelectItem key={size} value={String(size)}>
                {size} per page
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
        <Button variant="outline" size="sm" disabled={busy || page <= 1} onClick={() => onPage(page - 1)}>
          Previous
        </Button>
        <span className="tabular-nums">
          Page {page} of {totalPages}
        </span>
        <Button variant="outline" size="sm" disabled={busy || page >= totalPages} onClick={() => onPage(page + 1)}>
          Next
        </Button>
      </div>
    </div>
  )
}
