import fs from 'node:fs'
import { dataDir } from '../playwright.config'

export default function globalSetup() {
  fs.rmSync(dataDir, { recursive: true, force: true })
  fs.mkdirSync(dataDir, { recursive: true })
}
