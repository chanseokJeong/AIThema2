using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AIThemaView2.Models;

namespace AIThemaView2.Services.Interfaces
{
    public interface IScraperService
    {
        string SourceName { get; }
        Task<List<StockEvent>> FetchEventsAsync(DateTime targetDate);
    }
}
