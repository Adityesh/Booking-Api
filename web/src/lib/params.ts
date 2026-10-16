// Route ids are strings ("/bookings/abc" matches ":id" too). Only a positive integer is a real id; anything else is
// treated as "not found" without asking the API.
export function parseId(value: string | undefined): number | null {
  if (!value || !/^\d+$/.test(value)) return null
  const id = Number(value)
  return Number.isSafeInteger(id) && id > 0 ? id : null
}
