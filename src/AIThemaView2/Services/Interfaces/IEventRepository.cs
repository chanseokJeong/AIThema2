using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AIThemaView2.Models;

namespace AIThemaView2.Services.Interfaces
{
    public interface IEventRepository
    {
        Task<List<StockEvent>> GetEventsByDateAsync(DateTime date);
        Task<List<StockEvent>> GetEventsByDateRangeAsync(DateTime startDate, DateTime endDate);
        Task<StockEvent?> GetEventByIdAsync(int id);
        Task<bool> EventExistsAsync(string hash);
        Task AddEventAsync(StockEvent stockEvent);
        Task AddEventsAsync(List<StockEvent> events);
        Task UpdateEventAsync(StockEvent stockEvent);
        Task DeleteEventAsync(int id);
        Task<List<StockEvent>> SearchEventsAsync(string searchTerm, DateTime? startDate = null);
        Task<List<StockEvent>> GetEventsByCategoryAsync(string category, DateTime date);
        Task<int> GetEventCountBySourceAsync(string source, DateTime date);
        Task CleanupOldEventsAsync(int daysToKeep);
    }
}
