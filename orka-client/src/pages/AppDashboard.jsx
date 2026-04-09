import React, { useEffect, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { MessageSquare, BookOpen } from 'lucide-react';
import { UserAPI, TopicsAPI, ChatAPI } from '../services/api';
import toast from 'react-hot-toast';

import AppTopBar    from '../components/AppTopBar';
import TopicSidebar from '../components/TopicSidebar';
import ChatPanel    from '../components/ChatPanel';
import WikiPanel    from '../components/WikiPanel';
import InfoPanel    from '../components/InfoPanel';
import TopicModal   from '../components/TopicModal';

export default function AppDashboard() {
  const navigate = useNavigate();
  const [user, setUser] = useState(null);
  const [topics, setTopics] = useState([]);
  const [selectedTopic, setSelectedTopic] = useState(null);
  const [messages, setMessages] = useState([]);
  const [sending, setSending] = useState(false);
  const [loading, setLoading] = useState(true);
  const [activeTab, setActiveTab] = useState('chat');
  const [isModalOpen, setModalOpen] = useState(false);
  const [isTopicCreating, setTopicCreating] = useState(false);
  const [sessionId, setSessionId] = useState(null);
  const [wikiRefreshKey, setWikiRefreshKey] = useState(0);

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
      if (list.length > 0) await loadTopic(list[0]);
    } catch {
      toast.error('Veriler yüklenemedi.');
    } finally {
      setLoading(false);
    }
  };

  const loadTopic = async (topicMeta) => {
    try {
      const res = await TopicsAPI.getTopic(topicMeta.id);
      setSelectedTopic(res.data.topic);
      setMessages([]);
      setSessionId(null);
    } catch {
      toast.error('Konu yüklenemedi.');
    }
  };

  const handleLogout = () => {
    localStorage.removeItem('orka_token');
    localStorage.removeItem('orka_refresh');
    localStorage.removeItem('orka_user');
    navigate('/login');
  };

  const handleSendMessage = async (content) => {
    if (!content.trim() || sending) return;

    // Optimistic UI
    setMessages(prev => [...prev, { role: 'user', content, id: Date.now() }]);
    setSending(true);

    try {
      const res = await ChatAPI.sendMessage({
        topicId: selectedTopic?.id,
        sessionId: sessionId ?? undefined,
        content,
      });
      const data = res.data;

      if (data.sessionId) setSessionId(data.sessionId);

      setMessages(prev => [...prev, {
        role: 'assistant',
        content: data.content,
        modelUsed: data.modelUsed,
        messageType: data.messageType,
        id: data.messageId || Date.now() + 1,
      }]);

      // Refresh topic to update phase / progress
      if (data.topicId) {
        try {
          const topicRes = await TopicsAPI.getTopic(data.topicId);
          const updated = topicRes.data.topic;
          setSelectedTopic(updated);
          setTopics(prev =>
            prev.map(t => t.id === updated.id
              ? { ...t, currentPhase: updated.currentPhase, completedSections: updated.completedSections, totalSections: updated.totalSections }
              : t
            )
          );
        } catch { /* non-critical */ }
      }

      if (data.wikiUpdated) {
        toast.success('Bilgi haritası güncellendi', { duration: 2500 });
        setWikiRefreshKey(prev => prev + 1);
        // Refresh topics list completely to get new subtopics structure in sidebar
        try {
          const topicsRes = await TopicsAPI.getTopics();
          const latestTopics = topicsRes.data || [];
          setTopics(latestTopics);

          if (data.topicId) {
            const newCurrentTopic = latestTopics.find(t => t.id === data.topicId);
            if (newCurrentTopic) {
              await loadTopic(newCurrentTopic);
              
              setSessionId(data.sessionId);
              setMessages([{
                  role: 'assistant',
                  content: data.content,
                  modelUsed: data.modelUsed,
                  messageType: data.messageType,
                  id: data.messageId || Date.now() + 1,
              }]);
            }
          }
        } catch { /* ignore */ }
      }

    } catch (err) {
      const status = err.response?.status;
      if (status === 429) {
        toast.error('Günlük mesaj limitine ulaştınız.');
      } else {
        toast.error('Mesaj gönderilemedi.');
      }
      // Remove optimistic user message
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
      await loadTopic(newTopic);
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
      <AppTopBar user={user} onLogout={handleLogout} />

      <div className="app-body">
        <TopicSidebar
          topics={topics}
          selectedTopic={selectedTopic}
          onSelect={loadTopic}
          onNew={() => setModalOpen(true)}
        />

        <div className="app-main">
          <div className="main-tabs">
            <button
              className={`tab-btn ${activeTab === 'chat' ? 'active' : ''}`}
              onClick={() => setActiveTab('chat')}
            >
              <MessageSquare size={13} strokeWidth={2.5} />
              Sohbet
            </button>
            <button
              className={`tab-btn ${activeTab === 'wiki' ? 'active' : ''}`}
              onClick={() => setActiveTab('wiki')}
            >
              <BookOpen size={13} strokeWidth={2.5} />
              Bilgi Haritam
            </button>
          </div>

          {/* Chat view */}
          <div className={`view-content ${activeTab === 'chat' ? 'active' : ''}`}>
            <div className="chat-layout">
              <ChatPanel
                topic={selectedTopic}
                messages={messages}
                onSend={handleSendMessage}
                sending={sending}
                user={user}
              />
              <InfoPanel
                topic={selectedTopic}
                onSendCommand={handleSendMessage}
              />
            </div>
          </div>

          {/* Wiki view */}
          <div className={`view-content ${activeTab === 'wiki' ? 'active' : ''}`}>
            <WikiPanel topicId={selectedTopic?.id} refreshKey={wikiRefreshKey} />
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
