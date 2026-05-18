using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Orka.Core.DTOs;
using Orka.Core.Entities;

namespace Orka.Core.Interfaces;

public interface IWikiService
{
    Task<IEnumerable<WikiPage>> GetTopicWikiPagesAsync(Guid topicId, Guid userId);
    Task<WikiPage?> GetWikiPageAsync(Guid pageId, Guid userId);
    Task<WikiBlock> AddUserNoteAsync(Guid pageId, Guid userId, string content);
    Task<WikiBlockDto?> AddWikiBlockAsync(Guid pageId, Guid userId, CreateWikiBlockRequestDto request);
    Task UpdateWikiBlockAsync(Guid blockId, Guid userId, string? title, string? content);
    Task DeleteWikiBlockAsync(Guid blockId, Guid userId);
    Task AutoUpdateWikiAsync(Guid topicId, string aiContent, string userQuestion, string modelUsed);
    Task<string> GetWikiFullContentAsync(Guid topicId, Guid userId);
    Task<WikiGraphDto> GetWikiGraphAsync(Guid topicId, Guid userId);
    Task<WikiGraphDto?> GetLocalWikiGraphAsync(Guid pageId, Guid userId);
    Task<WikiGraphLinkDto?> LinkWikiPagesAsync(Guid userId, CreateWikiLinkRequestDto request);
    Task<WikiGraphSyncResultDto> SyncWikiGraphAsync(Guid topicId, Guid userId, WikiGraphSyncRequestDto request);
}
