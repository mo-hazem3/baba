// Arabic search helpers (brief section 6): ignore diacritics and normalise letter variants,
// so "أحمد" is found by typing "احمد", and "مدرسة" by "مدرسه".

const diacritics = /[ً-ٰٟـ]/g // tashkeel, dagger alif and tatweel

export const normalizeArabic = (text: string): string =>
  text
    .normalize('NFKC')
    .replace(diacritics, '')
    .replace(/[أإآٱ]/g, 'ا')
    .replace(/ى/g, 'ي')
    .replace(/ة/g, 'ه')
    .replace(/ؤ/g, 'و')
    .replace(/ئ/g, 'ي')
    // Arabic-Indic and Persian digits become Western digits so numbers match too.
    .replace(/[٠-٩]/g, (d) => String(d.charCodeAt(0) - 0x0660))
    .replace(/[۰-۹]/g, (d) => String(d.charCodeAt(0) - 0x06f0))
    .toLowerCase()

/** True when every word typed appears (normalised) in at least one of the texts. Matches either name and the code. */
export const matchesSearch = (query: string, ...texts: (string | undefined)[]): boolean => {
  const words = normalizeArabic(query).split(/\s+/).filter(Boolean)
  const haystack = texts.map((t) => normalizeArabic(t ?? '')).join(' ')
  return words.every((w) => haystack.includes(w))
}
