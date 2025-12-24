using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using AIThemaView2.Models;
using AIThemaView2.Services.Interfaces;

namespace AIThemaView2.Data.Repositories
{
    public class EventRepository : IEventRepository
    {
        private readonly StockEventContext _context;

        public EventRepository(StockEventContext context)
        {
            _context = context;
        }

        public async Task<List<StockEvent>> GetEventsByDateAsync(DateTime date)
        {
            var startOfDay = date.Date;
            var endOfDay = startOfDay.AddDays(1).AddTicks(-1);

            return await _context.StockEvents
                .Where(e => e.EventTime >= startOfDay && e.EventTime <= endOfDay)
                .OrderBy(e => e.EventTime)
                .ToListAsync();
        }

        public async Task<List<StockEvent>> GetEventsByDateRangeAsync(DateTime startDate, DateTime endDate)
        {
            return await _context.StockEvents
                .Where(e => e.EventTime >= startDate && e.EventTime <= endDate)
                .OrderBy(e => e.EventTime)
                .ToListAsync();
        }

        public async Task<StockEvent?> GetEventByIdAsync(int id)
        {
            return await _context.StockEvents.FindAsync(id);
        }

        public async Task<bool> EventExistsAsync(string hash)
        {
            return await _context.StockEvents.AnyAsync(e => e.Hash == hash);
        }

        public async Task AddEventAsync(StockEvent stockEvent)
        {
            await _context.StockEvents.AddAsync(stockEvent);
            await _context.SaveChangesAsync();
        }

        public async Task AddEventsAsync(List<StockEvent> events)
        {
            await _context.StockEvents.AddRangeAsync(events);
            await _context.SaveChangesAsync();
        }

        public async Task UpdateEventAsync(StockEvent stockEvent)
        {
            stockEvent.UpdatedAt = DateTime.Now;
            _context.StockEvents.Update(stockEvent);
            await _context.SaveChangesAsync();
        }

        public async Task DeleteEventAsync(int id)
        {
            var stockEvent = await _context.StockEvents.FindAsync(id);
            if (stockEvent != null)
            {
                _context.StockEvents.Remove(stockEvent);
                await _context.SaveChangesAsync();
            }
        }

        public async Task<List<StockEvent>> SearchEventsAsync(string searchTerm, DateTime? startDate = null)
        {
            var query = _context.StockEvents.AsQueryable();

            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                query = query.Where(e =>
                    e.Title.Contains(searchTerm) ||
                    (e.Description != null && e.Description.Contains(searchTerm)) ||
                    (e.RelatedStockName != null && e.RelatedStockName.Contains(searchTerm)) ||
                    (e.RelatedStockCode != null && e.RelatedStockCode.Contains(searchTerm)));
            }

            if (startDate.HasValue)
            {
                query = query.Where(e => e.EventTime >= startDate.Value);
            }

            return await query.OrderBy(e => e.EventTime).ToListAsync();
        }

        public async Task<List<StockEvent>> GetEventsByCategoryAsync(string category, DateTime date)
        {
            var startOfDay = date.Date;
            var endOfDay = startOfDay.AddDays(1).AddTicks(-1);

            return await _context.StockEvents
                .Where(e => e.Category == category &&
                           e.EventTime >= startOfDay &&
                           e.EventTime <= endOfDay)
                .OrderBy(e => e.EventTime)
                .ToListAsync();
        }

        public async Task<int> GetEventCountBySourceAsync(string source, DateTime date)
        {
            var startOfDay = date.Date;
            var endOfDay = startOfDay.AddDays(1).AddTicks(-1);

            return await _context.StockEvents
                .CountAsync(e => e.Source == source &&
                               e.EventTime >= startOfDay &&
                               e.EventTime <= endOfDay);
        }

        public async Task CleanupOldEventsAsync(int daysToKeep)
        {
            var cutoffDate = DateTime.Now.AddDays(-daysToKeep);
            var oldEvents = await _context.StockEvents
                .Where(e => e.EventTime < cutoffDate)
                .ToListAsync();

            _context.StockEvents.RemoveRange(oldEvents);
            await _context.SaveChangesAsync();
        }
    }
}
