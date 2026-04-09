using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Orka.Core.Entities;

namespace Orka.Core.Interfaces;

public interface IWikiService
{
    Task<IEnumerable<WikiPage>> GetTopicWikiPagesAsync(Guid topicId, Guid userId);
    Task<WikiPage?> GetWikiPageAsync(Guid pageId, Guid userId);
    Task<WikiBlock> AddUserNoteAsync(Guid pageId, Guid userId, string content);
    Task UpdateWikiBlockAsync(Guid blockId, Guid userId, string? title, string? content);
    Task DeleteWikiBlockAsync(Guid blockId, Guid userId);
    Task AutoUpdateWikiAsync(Guid topicId, string aiContent, string userQuestion, string modelUsed);
}
