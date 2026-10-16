// shadcn's <FieldError errors> takes `{ message }` objects (the shape form libraries use); fieldErrors() returns strings.
export const asFieldErrors = (messages: readonly string[]) => messages.map((message) => ({ message }))
