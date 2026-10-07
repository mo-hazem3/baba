import { App, Button, Card } from 'antd'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { clearBrandingImage } from '../../api/generated/baba'
import { ApiError } from '../../api/http'
import { refreshBooks, usePrintSettings } from '../../api/hooks'
import { errorMessage } from '../../layout/errors'

type Image = 'logo' | 'stamp'

/** Sends the picture's bytes as the request body. (The generated client cannot send files, so this one call is written by hand.) */
const upload = async (image: Image, file: File): Promise<void> => {
  let response: Response
  try {
    response = await fetch(`/api/branding/${image}`, {
      method: 'PUT',
      credentials: 'same-origin',
      headers: { 'X-File-Name': file.name },
      body: file,
    })
  } catch {
    throw new ApiError(0, 'Network', 'Cannot reach Baba.')
  }

  if (!response.ok) {
    const body = (await response.json().catch(() => ({}))) as { problem?: string; message?: string; issues?: { field: string; code: string }[] }
    throw new ApiError(response.status, body.problem ?? 'Unexpected', body.message ?? response.statusText, body.issues ?? [])
  }
}

/** The company logo and stamp (brief section 10.1), shown on printouts. PNG or JPEG up to 1 MB, kept inside the company file. */
export function BrandingCard() {
  const { t } = useTranslation()
  const { message } = App.useApp()
  const queryClient = useQueryClient()
  const settings = usePrintSettings()
  const [version, setVersion] = useState(0) // changes after each upload so the picture on screen is read again

  return (
    <Card title={t('branding.title')} className="settings-card">
      <p>{t('branding.intro')}</p>
      <div className="branding-grid">
        {(['logo', 'stamp'] as const).map((image) => (
          <ImageControl
            key={image}
            image={image}
            has={image === 'logo' ? (settings.data?.hasLogo ?? false) : (settings.data?.hasStamp ?? false)}
            version={version}
            onChanged={async () => {
              setVersion((v) => v + 1)
              await refreshBooks(queryClient)
            }}
            onError={(error) => {
              const code = error instanceof ApiError ? error.issues[0]?.code : undefined
              void message.error(code ? t(`branding.issues.${code}`, { defaultValue: errorMessage(error, t) }) : errorMessage(error, t))
            }}
          />
        ))}
      </div>
    </Card>
  )
}

function ImageControl({
  image,
  has,
  version,
  onChanged,
  onError,
}: {
  image: Image
  has: boolean
  version: number
  onChanged: () => Promise<void>
  onError: (error: unknown) => void
}) {
  const { t } = useTranslation()
  const input = useRef<HTMLInputElement>(null)

  const set = useMutation({ mutationFn: (file: File) => upload(image, file), onSuccess: onChanged, onError })
  const clear = useMutation({ mutationFn: () => clearBrandingImage(image), onSuccess: onChanged, onError })

  return (
    <div className="branding-item">
      <strong>{t(`branding.${image}`)}</strong>
      <div className="branding-preview">
        {has ? <img src={`/api/branding/${image}?v=${version}`} alt={t(`branding.${image}Alt`)} /> : <span className="muted">{t('branding.none')}</span>}
      </div>
      <input
        ref={input}
        type="file"
        accept="image/png,image/jpeg"
        hidden
        data-testid={`upload-${image}`}
        onChange={(e) => {
          const file = e.target.files?.[0]
          e.target.value = '' // so the same file can be chosen again
          if (file) set.mutate(file)
        }}
      />
      <div className="form-buttons">
        <Button onClick={() => input.current?.click()} loading={set.isPending}>
          {has ? t('branding.replace') : t('branding.upload')}
        </Button>
        {has && (
          <Button danger onClick={() => clear.mutate()} loading={clear.isPending}>
            {t('branding.remove')}
          </Button>
        )}
      </div>
    </div>
  )
}
