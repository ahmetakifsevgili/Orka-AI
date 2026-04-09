import React, { useState, useRef, useEffect } from 'react';
import { Link } from 'react-router-dom';
import { Search, Zap, LogOut, Settings, User } from 'lucide-react';

export default function AppTopBar({ user, onLogout }) {
  const [menuOpen, setMenuOpen] = useState(false);
  const menuRef = useRef(null);

  useEffect(() => {
    const handler = (e) => {
      if (menuRef.current && !menuRef.current.contains(e.target)) {
        setMenuOpen(false);
      }
    };
    document.addEventListener('mousedown', handler);
    return () => document.removeEventListener('mousedown', handler);
  }, []);

  const getInitials = () => {
    const f = user?.firstName?.charAt(0) || '';
    const l = user?.lastName?.charAt(0) || '';
    return (f + l).toUpperCase() || 'U';
  };

  const storageUsed = user?.storageUsedMB ?? 0;
  const storageLimit = user?.storageLimitMB ?? 3072;
  const storagePercent = Math.min(Math.round((storageUsed / storageLimit) * 100), 100);

  return (
    <div className="topbar">
      <Link to="/" className="topbar-logo">orka</Link>

      <div className="topbar-search">
        <Search size={12} strokeWidth={2.5} />
        <span style={{ flex: 1 }}>Konu ara veya başlat...</span>
        <span className="kbd">⌘K</span>
      </div>

      <div className="topbar-right">
        <div className="storage-pill">
          <span>{storageUsed.toFixed(0)} MB</span>
          <div className="storage-bar">
            <div className="storage-fill" style={{ width: `${storagePercent}%` }} />
          </div>
        </div>

        {user?.plan !== 'Pro' && (
          <button className="pro-badge">
            <Zap size={10} strokeWidth={2.5} />
            Pro'ya Geç
          </button>
        )}

        <div
          className="user-avatar"
          ref={menuRef}
          onClick={() => setMenuOpen(prev => !prev)}
        >
          {getInitials()}

          {menuOpen && (
            <div className="user-menu" onClick={e => e.stopPropagation()}>
              <div className="user-menu-email">{user?.email}</div>
              <div className="user-menu-divider" />
              <div className="user-menu-item">
                <User size={13} />
                Profil
              </div>
              <div className="user-menu-item">
                <Settings size={13} />
                Ayarlar
              </div>
              <div className="user-menu-divider" />
              <button className="user-menu-item danger" onClick={onLogout}>
                <LogOut size={13} />
                Çıkış Yap
              </button>
            </div>
          )}
        </div>
      </div>
    </div>
  );
}
