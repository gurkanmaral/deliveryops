import {
  BarChart3,
  AlertTriangle,
  Bell,
  Bike,
  Building2,
  ChevronDown,
  LayoutDashboard,
  Menu,
  PackageOpen,
  PlugZap,
  Search,
  Settings,
  Users,
  WalletCards,
  X,
  LogOut,
  LayoutGrid,
  Inbox,
} from 'lucide-react'
import { useState } from 'react'
import { NavLink, Outlet } from 'react-router-dom'
import clsx from 'clsx'
import type { LucideIcon } from 'lucide-react'
import { useAuth } from '../../features/auth/AuthProvider'
import { useQuery } from '@tanstack/react-query'
import { getJson } from '../../shared/api/httpClient'
import type { PagedResponse } from '../../shared/api/types'
import { useAlertRealtime } from '../../features/alerts/useAlertRealtime'
import type { OperationalAlert } from '../../features/alerts/AlertsPage'

interface NavigationItem { to: string; label: string; businessLabel?: string; icon: LucideIcon; end?: boolean; permission?: string; platformOnly?: boolean }

const navigation: NavigationItem[] = [
  { to: '/', label: 'Genel Bakış', icon: LayoutDashboard, end: true },
  { to: '/orders', label: 'Siparişler', icon: PackageOpen, permission: 'orders.read' },
  { to: '/couriers', label: 'Kuryeler', icon: Bike, permission: 'couriers.read' },
  { to: '/businesses', label: 'İşletmeler', businessLabel: 'Şubelerim', icon: Building2, permission: 'businesses.read' },
  { to: '/users', label: 'Kullanıcılar', icon: Users, permission: 'users.read' },
  { to: '/integrations', label: 'Entegrasyonlar', icon: PlugZap, permission: 'integrations.read' },
  { to: '/outbox', label: 'Hata Kuyrukları', icon: Inbox, permission: 'integrations.read' },
  { to: '/alerts', label: 'Uyarılar', icon: AlertTriangle, permission: 'dispatch.read' },
  { to: '/reports', label: 'Raporlar', icon: BarChart3, permission: 'orders.read' },
  { to: '/billing', label: 'Finans', businessLabel: 'Mutabakat', icon: WalletCards, permission: 'billing.read' },
]

const tabbarPaths = ['/', '/orders', '/couriers', '/alerts']

export function AppLayout() {
  const [isMenuOpen, setMenuOpen] = useState(false)
  const auth = useAuth()
  useAlertRealtime()
  const alertPath = auth.user?.businessId ? `/api/v1/operations/alerts?businessId=${auth.user.businessId}` : '/api/v1/operations/alerts'
  const alerts = useQuery({ queryKey: ['operational-alerts', 'header', auth.user?.businessId], enabled: auth.hasPermission('dispatch.read'), queryFn: ({ signal }) => getJson<PagedResponse<OperationalAlert>>(`${alertPath}${alertPath.includes('?') ? '&' : '?'}pageSize=100`, signal), refetchInterval: 30_000 })
  const activeAlertCount = alerts.data?.items.filter(alert => alert.status === 0).length ?? 0
  const visibleNavigation = navigation.filter(item => (!item.platformOnly || auth.isPlatformAdmin) && (!item.permission || auth.hasPermission(item.permission)))
  const fullName = `${auth.user?.firstName ?? ''} ${auth.user?.lastName ?? ''}`.trim()
  const initials = `${auth.user?.firstName?.[0] ?? ''}${auth.user?.lastName?.[0] ?? ''}`.toLocaleUpperCase('tr-TR')
  const tabbarItems = visibleNavigation.filter(item => tabbarPaths.includes(item.to))
  const roleLabel = auth.isPlatformAdmin ? 'Sistem Yöneticisi' : auth.user?.roles.includes('BusinessAdmin') ? 'İşletme Yöneticisi' : 'İşletme Personeli'

  return (
    <div className="app-shell">
      <aside className={clsx('sidebar', isMenuOpen && 'sidebar--open')}>
        <div className="brand">
          <span className="brand__mark"><Bike size={21} /></span>
          <span>DeliveryOps</span>
          <button className="icon-button sidebar__close" onClick={() => setMenuOpen(false)} aria-label="Menüyü kapat">
            <X size={20} />
          </button>
        </div>

        <p className="sidebar__eyebrow">{auth.isPlatformAdmin ? 'OPERASYON' : 'İŞLETME PANELİ'}</p>
        <nav className="navigation" aria-label="Ana menü">
          {visibleNavigation.map(({ to, label, businessLabel, icon: Icon, end }) => (
            <NavLink
              key={to}
              to={to}
              end={end}
              onClick={() => setMenuOpen(false)}
              className={({ isActive }) => clsx('navigation__link', isActive && 'navigation__link--active')}
            >
              <Icon size={19} />
              <span>{!auth.isPlatformAdmin && businessLabel ? businessLabel : label}</span>
              {to === '/alerts' && activeAlertCount > 0 && <span className="navigation__badge">{activeAlertCount}</span>}
            </NavLink>
          ))}
        </nav>

        <div className="sidebar__bottom">
          {auth.hasPermission('dispatch.read') && <NavLink to="/settings" onClick={() => setMenuOpen(false)} className={({ isActive }) => clsx('navigation__link', isActive && 'navigation__link--active')}>
            <Settings size={19} />
            <span>Ayarlar</span>
          </NavLink>}
          <button className="navigation__link navigation__button" onClick={() => void auth.logout()}><LogOut size={19} /><span>Çıkış yap</span></button>
          <div className="support-card">
            <span className="status-dot" />
            <div><strong>Sistem çalışıyor</strong><small>Tüm servisler aktif</small></div>
          </div>
        </div>
      </aside>

      {isMenuOpen && <button className="backdrop" onClick={() => setMenuOpen(false)} aria-label="Menüyü kapat" />}

      <div className="workspace">
        <header className="topbar">
          <button className="icon-button menu-button" onClick={() => setMenuOpen(true)} aria-label="Menüyü aç">
            <Menu size={22} />
          </button>
          <NavLink to="/" className="topbar__brand"><span className="brand__mark"><Bike /></span>DeliveryOps</NavLink>
          <label className="search-box">
            <Search size={18} />
            <input placeholder={auth.isPlatformAdmin ? 'Sipariş, kurye veya işletme ara...' : 'Sipariş veya kurye ara...'} aria-label="Ara" />
            <kbd>⌘ K</kbd>
          </label>
          <div className="topbar__actions">
            {auth.hasPermission('dispatch.read') && <NavLink to="/alerts" className="icon-button notification-button" aria-label={`${activeAlertCount} açık uyarı`}><Bell size={20} />{activeAlertCount > 0 && <span />}</NavLink>}
            <button className="profile-button">
              <span className="avatar">{initials || 'DO'}</span>
              <span className="profile-button__copy"><strong>{fullName || 'DeliveryOps Kullanıcısı'}</strong><small>{roleLabel}</small></span>
              <ChevronDown size={16} />
            </button>
          </div>
        </header>
        <main className="content"><Outlet /></main>
      </div>

      <nav className="tabbar" aria-label="Hızlı menü">
        {tabbarItems.map(({ to, label, businessLabel, icon: Icon, end }) => (
          <NavLink key={to} to={to} end={end} className={({ isActive }) => clsx('tabbar__item', isActive && 'tabbar__item--active')}>
            <Icon />
            <span>{!auth.isPlatformAdmin && businessLabel ? businessLabel : label === 'Genel Bakış' ? 'Özet' : label}</span>
            {to === '/alerts' && activeAlertCount > 0 && <span className="tabbar__badge">{activeAlertCount > 9 ? '9+' : activeAlertCount}</span>}
          </NavLink>
        ))}
        <button className="tabbar__item" onClick={() => setMenuOpen(true)} aria-label="Tüm menü">
          <LayoutGrid />
          <span>Menü</span>
        </button>
      </nav>
    </div>
  )
}
