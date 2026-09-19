export type CrmSpreadsheetFormat = 'csv' | 'xlsx'
export type CrmSpreadsheetTable = {
  headers: string[]
  rows: Array<Array<string | number | boolean | null | undefined>>
}

export async function exportCrmSpreadsheet(
  baseName: string,
  table: CrmSpreadsheetTable,
  format: CrmSpreadsheetFormat,
) {
  const XLSX = await import('@keep-lts/xlsx')
  const sheet = XLSX.utils.aoa_to_sheet([table.headers, ...table.rows])
  const workbook = XLSX.utils.book_new()
  XLSX.utils.book_append_sheet(workbook, sheet, 'CRM Data')
  XLSX.writeFile(workbook, `${baseName}.${format}`, {
    bookType: format,
    compression: format === 'xlsx',
  })
}

export function pickCrmSpreadsheet(
  onRows: (rows: string[][], fileName: string) => void,
  onError: (message: string) => void,
) {
  const input = document.createElement('input')
  input.type = 'file'
  input.accept = '.csv,.xlsx,.xls,text/csv,application/vnd.openxmlformats-officedocument.spreadsheetml.sheet'
  input.onchange = async () => {
    const file = input.files?.[0]
    if (!file) return
    try {
      const XLSX = await import('@keep-lts/xlsx')
      const bytes = await file.arrayBuffer()
      const workbook = XLSX.read(bytes, { type: 'array', cellDates: false })
      const firstSheet = workbook.SheetNames[0]
      if (!firstSheet) throw new Error('Spreadsheet has no worksheet.')
      const sheet = workbook.Sheets[firstSheet]
      const raw = XLSX.utils.sheet_to_json<Array<string | number | boolean | null>>(sheet, {
        header: 1,
        defval: '',
        raw: false,
      })
      const rows = raw
        .map((row) => row.map((cell) => String(cell ?? '').trim()))
        .filter((row) => row.some(Boolean))
      if (rows.length === 0) throw new Error('Spreadsheet has no data rows.')
      onRows(rows, file.name)
    } catch (error) {
      onError(error instanceof Error ? error.message : String(error))
    }
  }
  input.click()
}
