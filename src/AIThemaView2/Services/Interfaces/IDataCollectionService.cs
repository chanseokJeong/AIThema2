using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AIThemaView2.Models;

namespace AIThemaView2.Services.Interfaces
{
    public interface IDataCollectionService
    {
        Task<int> CollectTodayEventsAsync();
        Task<int> CollectEventsForDateAsync(DateTime targetDate);
        Task<List<StockEvent>> GetEventsForDateAsync(DateTime date);
        Task<List<StockEvent>> SearchEventsAsync(string searchTerm);
        Task CleanupOldDataAsync(int daysToKeep = 30);
    }
}
