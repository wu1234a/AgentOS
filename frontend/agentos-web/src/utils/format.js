export function formatUtc(value) {
  if (!value) return '—'
  // SQL datetime2 materializes as Unspecified; treat offset-free values as UTC.
  const hasZone = /Z$|[+-]\d{2}:?\d{2}$/.test(value)
  const date = new Date(hasZone ? value : `${value}Z`)
  return Number.isNaN(date.getTime()) ? value : date.toLocaleString()
}

export function formatSeconds(value) {
  if (value === null || value === undefined) return '未提供'
  return `${Number(value).toFixed(1)}s`
}

export function statusTagType(status) {
  switch (status) {
    case 'Succeeded':
    case 'Processed':
    case 'Online':
      return 'success'
    case 'Running':
    case 'Queued':
    case 'RetryWait':
    case 'Received':
      return 'warning'
    case 'Failed':
    case 'Lost':
    case 'Rejected':
    case 'Disabled':
      return 'danger'
    case 'Cancelled':
    case 'Ignored':
    case 'Offline':
      return 'info'
    default:
      return ''
  }
}

export function truncate(value, length = 120) {
  if (!value) return '—'
  return value.length > length ? `${value.slice(0, length)}…` : value
}
