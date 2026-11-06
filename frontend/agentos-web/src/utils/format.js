export function formatUtc(value) {
  if (!value) return '—'
  const date = new Date(value)
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
