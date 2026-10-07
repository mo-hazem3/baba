import { Input, Modal, Tree, Typography } from 'antd'
import type { DataNode } from 'antd/es/tree'
import { useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import type { AccountDto } from '../../api/generated/model'
import { useSettings } from '../../settings/SettingsContext'
import { accountLabel, buildTree, searchAccounts, type AccountNode } from './accountTree'

/**
 * The 🔍 window (brief section 10.1): the whole chart of accounts as a tree with a search box on top. Groups are shown for
 * orientation; only postable accounts can be chosen. Typing narrows the tree to the matches and the groups above them.
 */
export function AccountSearchDialog({
  open,
  accounts,
  onPick,
  onClose,
  cashOnly = false,
}: {
  open: boolean
  accounts: readonly AccountDto[]
  onPick: (accountId: string) => void
  onClose: () => void
  cashOnly?: boolean
}) {
  const { t } = useTranslation()
  const { settings } = useSettings()
  const [query, setQuery] = useState('')

  const usable = useMemo(
    () => accounts.filter((a) => a.isActive && (!cashOnly || a.role === 'CashOrBank')),
    [accounts, cashOnly],
  )

  const treeData = useMemo(() => {
    // Keep the matching accounts and every group above them, so each match still shows where it sits.
    const matches = new Set(searchAccounts(usable.filter((a) => a.isPosting), query).map((a) => a.id))
    const byId = new Map(accounts.map((a) => [a.id, a]))
    const keep = new Set<string>()
    for (const id of matches) {
      for (let node = byId.get(id); node && !keep.has(node.id); node = node.parentId ? byId.get(node.parentId) : undefined) keep.add(node.id)
    }

    const toNode = (node: AccountNode): DataNode => ({
      key: node.id,
      title: <span className={node.isPosting ? '' : 'muted'}>{accountLabel(node, settings.language)}</span>,
      selectable: node.isPosting && matches.has(node.id),
      children: node.children.filter((c) => keep.has(c.id)).map(toNode),
    })
    return buildTree(accounts.filter((a) => keep.has(a.id))).map(toNode)
  }, [accounts, usable, query, settings.language])

  const close = () => {
    setQuery('')
    onClose()
  }

  return (
    <Modal open={open} title={t('accounting.findAccount')} onCancel={close} footer={null} destroyOnHidden width={560}>
      <Input
        autoFocus
        value={query}
        onChange={(e) => setQuery(e.target.value)}
        placeholder={t('accounting.searchAccounts')}
        aria-label={t('accounting.searchAccounts')}
        allowClear
      />
      <div className="account-tree">
        {treeData.length === 0 ? (
          <Typography.Paragraph type="secondary">{t('accounting.noAccountFound')}</Typography.Paragraph>
        ) : (
          <Tree
            key={query} // start fully open again whenever the search changes
            treeData={treeData}
            defaultExpandAll
            showLine
            blockNode
            onSelect={(keys) => {
              const id = keys[0]
              if (typeof id === 'string') {
                setQuery('')
                onPick(id)
              }
            }}
          />
        )}
      </div>
      <Typography.Paragraph type="secondary" className="account-tree-hint">
        {t('accounting.findAccountHint')}
      </Typography.Paragraph>
    </Modal>
  )
}
