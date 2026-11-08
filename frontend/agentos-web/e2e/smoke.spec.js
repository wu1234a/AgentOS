import { test, expect } from '@playwright/test'

const pages = [
  { path: '/', testId: 'dashboard-page', name: 'Dashboard' },
  { path: '/projects', testId: 'projects-page', name: 'Projects' },
  { path: '/usage', testId: 'usageview-page', name: 'Usage' },
  { path: '/workers', testId: 'workersview-page', name: 'Workers' },
  { path: '/pipelines', testId: 'pipelinesview-page', name: 'Pipelines' },
  { path: '/approvals', testId: 'approvals-page', name: 'Approvals' },
  { path: '/tasks', testId: 'tasks-page', name: 'Tasks' },
  { path: '/events', testId: 'events-page', name: 'Events' },
  { path: '/coding-runs', testId: 'coding-runs-page', name: 'Coding Runs' }
]

for (const pageInfo of pages) {
  test(`${pageInfo.name} loads without console errors`, async ({ page }) => {
    const errors = []
    page.on('console', message => {
      if (message.type() === 'error') errors.push(message.text())
    })

    await page.goto(pageInfo.path)
    await expect(page.locator(`[data-testid="${pageInfo.testId}"]`)).toBeVisible()
    expect(errors, errors.join('\n')).toEqual([])
  })
}

test('deep links survive reload', async ({ page }) => {
  await page.goto('/projects')
  await expect(page.locator('[data-testid="projects-page"]')).toBeVisible()
  await page.reload()
  await expect(page.locator('[data-testid="projects-page"]')).toBeVisible()
})

test('sidebar navigation works', async ({ page }) => {
  await page.goto('/')
  await expect(page.locator('[data-testid="dashboard-page"]')).toBeVisible()

  await page.locator('[data-testid="nav-projects"]').click()
  await expect(page).toHaveURL(/\/projects$/)
  await expect(page.locator('[data-testid="projects-page"]')).toBeVisible()

  await page.locator('[data-testid="nav-usage"]').click()
  await expect(page).toHaveURL(/\/usage$/)
  await expect(page.locator('[data-testid="usageview-page"]')).toBeVisible()

  await page.locator('[data-testid="nav-workers"]').click()
  await expect(page).toHaveURL(/\/workers$/)
  await expect(page.locator('[data-testid="workersview-page"]')).toBeVisible()

  await page.locator('[data-testid="nav-pipelines"]').click()
  await expect(page).toHaveURL(/\/pipelines$/)
  await expect(page.locator('[data-testid="pipelinesview-page"]')).toBeVisible()
})

test('projects can be created and disabled', async ({ page }) => {
  const suffix = Date.now().toString(36)
  const slug = `e2e-${suffix}`

  await page.goto('/projects')
  await page.locator('[data-testid="project-create"]').click()
  await page.locator('[data-testid="project-name"]').fill('E2E Project')
  await page.locator('[data-testid="project-slug"]').fill(slug)
  await page.locator('[data-testid="project-save"]').click()

  const table = page.locator('[data-testid="projects-table"]')
  await expect(table).toContainText(slug)

  const row = table.locator('tr', { hasText: slug })
  await row.getByRole('button', { name: '停用' }).click()
  await expect(row).toContainText('停用')
})

test('usage page renders summary', async ({ page }) => {
  await page.goto('/usage')
  await expect(page.locator('[data-testid="usageview-page"]')).toBeVisible()
  await expect(page.getByText('调用次数')).toBeVisible()
  await expect(page.getByText('总 Token').first()).toBeVisible()
})

test('pipelines shows recent tasks or empty state', async ({ page }) => {
  await page.goto('/pipelines')
  await expect(page.locator('[data-testid="pipelinesview-page"]')).toBeVisible()
  const table = page.locator('[data-testid="pipelinesview-table"]')
  await expect(table).toBeVisible()
})
