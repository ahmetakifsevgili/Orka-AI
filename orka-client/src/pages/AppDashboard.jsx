import React, { useEffect, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { UserAPI, TopicsAPI, ChatAPI } from '../services/api';
import toast from 'react-hot-toast';

import AppTopBar    from '../components/AppTopBar';
import TopicSidebar from '../components/TopicSidebar';
import ChatPanel    from '../components/ChatPanel';
import WikiDrawer   from '../components/WikiDrawer';
import InfoPanel    from '../components/InfoPanel';
import TopicModal   from '../components/TopicModal';

export default function AppDashboard() {
  const navigate = useNavigate();
  const [user, setUser] = useState(null);
  const [topics, setTopics] = useState([]);

  // Global chat — ASLA sıfırlanmaz, tüm mesajlar birikir
  const [messages, setMessages] = useState([]);
  const [sending, setSending] = useState(false);
  const [sessionId, setSessionId] = useState(null);

  // Sidebar aktif (highlight) — chat context değil, sadece sidebar işaretçi
  const [activeSidebarTopic, setActiveSidebarTopic] = useState(null);

  // Wiki sağ panel — sidebar tıklaması ile açılır, chat'e dokunmaz
  const [wikiTopic, setWikiTopic] = useState(null);
  const [wikiRefreshKey, setWikiRefreshKey] = useState(0);

  const [loading, setLoading] = useState(true);
  const [isModalOpen, setModalOpen] = useState(false);
  const [isTopicCreating, setTopicCreating] = useState(false);

  useEffect(() => { fetchInitialData(); }, []);

  const fetchInitialData = async () => {
    try {
      setLoading(true);
      const [userRes, topicsRes] = await Promise.all([
        UserAPI.getMe(),
        TopicsAPI.getTopics(),
      ]);
      setUser(userRes.data);
      const list = topicsRes.data || [];
      setTopics(list);
      // Sidebar'daki ilk konuyu varsayılan olarak işaretle (chat mesajlarına dokunma)
      if (list.length > 0) {
        setActiveSidebarTopic(list[0]);
      }
    } catch {
      toast.error('Veriler yüklenemedi.');
    } finally {
      setLoading(false);
    }
  };

  const handleLogout = () => {
    localStorage.removeItem('orka_token');
    localStorage.removeItem('orka_refresh');
    localStorage.removeItem('orka_user');
    navigate('/login');
  };

  // Sidebar tıklaması: wiki panelini aç, CHAT DOKUNULMAZ
  const handleTopicSelect = (topic) => {
    setActiveSidebarTopic(topic);
    setWikiTopic(topic);         // Wiki drawer açılır
  };

  const handleSendMessage = async (content) => {
    if (!content.trim() || sending) return;

    setMessages(prev => [...prev, { role: 'user', content, id: Date.now() }]);
    setSending(true);

    try {
      const res = await ChatAPI.sendMessage({
        topicId: activeSidebarTopic?.id,
        sessionId: sessionId ?? undefined,
        content,
      });
      const data = res.data;

      if (data.sessionId) setSessionId(data.sessionId);

      // Global chat — mesajı sonuna ekle, asla temizleme
      setMessages(prev => [...prev, {
        role: 'assistant',
        content: data.content,
        modelUsed: data.modelUsed,
        messageType: data.messageType,
        id: data.messageId || Date.now() + 1,
      }]);

      if (data.wikiUpdated) {
        toast.success('Bilgi haritası güncellendi', { duration: 2500 });
        setWikiRefreshKey(prev => prev + 1);

        // Topic listesini yenile ve sidebar aktif topici güncelle
        try {
          const topicsRes = await TopicsAPI.getTopics();
          const latestTopics = topicsRes.data || [];
          setTopics(latestTopics);

          if (data.topicId) {
            // Sidebar'da aktif topic'i güncelle (mesajlara dokunma)
            const newTopic = latestTopics.find(t => t.id === data.topicId);
            if (newTopic) {
              // Tam topic datasını al (parentTopicId dahil)
              const topicRes = await TopicsAPI.getTopic(newTopic.id);
              const fullTopic = topicRes.data?.topic ?? newTopic;
              setActiveSidebarTopic(fullTopic);
            }
          }
        } catch { /* non-critical */ }
      } else if (data.topicId) {
        // Topic değişti ama wiki güncellemedi — sadece sidebar highlight güncelle
        try {
          const updated = topics.find(t => t.id === data.topicId);
          if (updated) setActiveSidebarTopic(updated);
        } catch { /* ignore */ }
      }

    } catch (err) {
      const status = err.response?.status;
      if (status === 429) {
        toast.error('Günlük mesaj limitine ulaştınız.');
      } else {
        toast.error('Mesaj gönderilemedi.');
      }
      setMessages(prev => prev.slice(0, -1));
    } finally {
      setSending(false);
    }
  };

  const handleCreateTopic = async (formData) => {
    try {
      setTopicCreating(true);
      const res = await TopicsAPI.createTopic({
        title: formData.title,
        emoji: formData.emoji,
        category: formData.category,
      });
      const newTopic = res.data;
      setTopics(prev => [newTopic, ...prev]);
      setActiveSidebarTopic(newTopic);
      // Global chat'i temizle sadece kullanıcı manuel yeni konu oluşturursa
      setMessages([]);
      setSessionId(null);
      setModalOpen(false);
      toast.success('Yeni konu oluşturuldu!');
    } catch (err) {
      toast.error(err.response?.data?.message || 'Konu oluşturulamadı.');
    } finally {
      setTopicCreating(false);
    }
  };

  if (loading) {
    return (
      <div className="loading-screen">
        <div className="loading-logo">orka</div>
        <div className="spinner" />
      </div>
    );
  }

  return (
    <div className="app-shell">
      {/* Ambient AI background — purely visual, pointer-events: none */}
      <div className="ambient-layer">
        <div className="ambient-orb ambient-orb-1" />
        <div className="ambient-orb ambient-orb-2" />
        <div className="ambient-orb ambient-orb-3" />
      </div>

      <AppTopBar user={user} onLogout={handleLogout} />

      <div className="app-body">
        <TopicSidebar
          topics={topics}
          selectedTopic={activeSidebarTopic}
          onSelect={handleTopicSelect}
          onNew={() => setModalOpen(true)}
        />

        <div className="app-main">
          {/* Global Chat — her zaman görünür, hiç kaybolmaz */}
          <div className="chat-layout">
            <ChatPanel
              topic={activeSidebarTopic}
              messages={messages}
              onSend={handleSendMessage}
              sending={sending}
              user={user}
            />

            {/* Sağ panel: Wiki Drawer (topic tıklanınca) veya InfoPanel */}
            {wikiTopic ? (
              <WikiDrawer
                topic={wikiTopic}
                onClose={() => setWikiTopic(null)}
                refreshKey={wikiRefreshKey}
              />
            ) : (
              <InfoPanel
                topic={activeSidebarTopic}
                onSendCommand={handleSendMessage}
              />
            )}
          </div>
        </div>
      </div>

      <TopicModal
        isOpen={isModalOpen}
        onClose={() => setModalOpen(false)}
        onSubmit={handleCreateTopic}
        isLoading={isTopicCreating}
      />
    </div>
  );
}
