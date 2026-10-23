export function money(amount: number | string, currency: string): string {
  return new Intl.NumberFormat("en-ZA", { style: "currency", currency }).format(Number(amount));
}
