import { useState } from "react";
import { BookOpen, Plus, Loader2, Search } from "lucide-react";
import { TopicsAPI } from "@/services/api";
import type { ApiTopic } from "@/lib/types";
import toast from "react-hot-toast";

interface OrkaLMDashboardProps {
  topics: ApiTopic[];
  onSelectTopic: (topic: ApiTopic) => void;
  onTopicCreated: (topic: ApiTopic) => void;
}

export default function OrkaLMDashboard({ topics, onSelectTopic, onTopicCreated }: OrkaLMDashboardProps) {
  const [isCreating, setIsCreating] = useState(false);
  const [search, setSearch] = useState("");
  
  // Sadece ana defterleri (parentTopicId'si olmayanlar) göster
  const notebooks = topics.filter(t => !t.parentTopicId && t.category === "Notebook" && t.title.toLowerCase().includes(search.toLowerCase()));

  const handleCreateNotebook = async () => {
    setIsCreating(true);
    try {
      const response = await TopicsAPI.create({
        title: "Yeni Defter",
        emoji: "📔",
        category: "Notebook",
      });
      // response.data içerisinden yeni topiği al
      onTopicCreated(response.data);
      toast.success("Yeni defter oluşturuldu!");
    } catch (err) {
      console.error(err);
      toast.error("Defter oluşturulamadı.");
    } finally {
      setIsCreating(false);
    }
  };

  return (
    <div className="flex-1 flex flex-col bg-[#faf9f6] h-full overflow-y-auto">
      {/* Header */}
      <div className="px-12 py-10">
        <h1 className="text-[28px] font-bold text-[#1a1a1a] tracking-tight">OrkaLM Stüdyosu</h1>
        <p className="text-[15px] text-[#555555] mt-2 max-w-2xl">
          Dokümanlarınızı buraya yükleyin, Orka sizin için otomatik bir wiki çıkarsın ve belgeyle etkileşime girmenizi sağlasın. 
          Hiçbir konu veya ders kısıtlaması olmadan, doğrudan dosyalarınızla çalışın.
        </p>
      </div>

      {/* Toolbar */}
      <div className="px-12 pb-6 flex items-center justify-between">
        <div className="relative w-[300px]">
          <Search className="absolute left-3 top-1/2 -translate-y-1/2 w-4 h-4 text-[#888888]" />
          <input 
            type="text" 
            placeholder="Defterlerde ara..." 
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            className="w-full pl-9 pr-4 py-2 bg-white border border-[#eaecf0] rounded-xl text-[14px] text-[#1a1a1a] outline-none focus:border-[#d0d4dc] transition-colors shadow-sm"
          />
        </div>
      </div>

      {/* Grid */}
      <div className="px-12 pb-12">
        <div className="grid grid-cols-1 md:grid-cols-3 lg:grid-cols-4 gap-6">
          {/* Create New Card */}
          <button 
            onClick={handleCreateNotebook}
            disabled={isCreating}
            className="group h-[200px] flex flex-col items-center justify-center gap-3 bg-white border border-dashed border-[#d0d4dc] hover:border-[#a0a5b1] hover:bg-[#f2f2f1]/50 rounded-2xl transition-all"
          >
            {isCreating ? (
              <Loader2 className="w-8 h-8 text-[#888888] animate-spin" />
            ) : (
              <div className="w-12 h-12 rounded-full bg-[#f2f2f1] group-hover:bg-white flex items-center justify-center border border-[#eaecf0] transition-colors">
                <Plus className="w-5 h-5 text-[#1a1a1a]" />
              </div>
            )}
            <span className="text-[15px] font-medium text-[#1a1a1a]">Yeni Defter Oluştur</span>
          </button>

          {/* Existing Notebooks */}
          {notebooks.map((topic) => (
            <div 
              key={topic.id}
              onClick={() => onSelectTopic(topic)}
              className="group h-[200px] bg-white border border-[#eaecf0] hover:border-[#d0d4dc] hover:shadow-sm rounded-2xl p-6 flex flex-col cursor-pointer transition-all"
            >
              <div className="w-10 h-10 rounded-xl bg-[#f2f2f1] flex items-center justify-center border border-[#eaecf0] mb-4 text-[18px]">
                {topic.emoji || "📔"}
              </div>
              <h3 className="text-[16px] font-semibold text-[#1a1a1a] leading-snug line-clamp-2">
                {topic.title}
              </h3>
              <div className="mt-auto flex items-center justify-between text-[#888888]">
                <span className="text-[12px] font-medium flex items-center gap-1.5">
                  <BookOpen className="w-3.5 h-3.5" />
                  Defter
                </span>
                <span className="text-[12px]">
                  {new Date(topic.updatedAt ?? topic.createdAt ?? Date.now()).toLocaleDateString("tr-TR")}
                </span>
              </div>
            </div>
          ))}
        </div>
      </div>
    </div>
  );
}
