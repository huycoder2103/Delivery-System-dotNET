using Delivery_System.Models;

namespace Delivery_System.Services
{
    public interface IOrderService
    {
        Task<string> GenerateOrderNumberAsync();
        Task<(bool Success, string Message, TblOrder? Order)> CreateOrderAsync(TblOrder order, string userId, string role, string? userStationName);
        Task<(bool Success, string Message)> UpdateOrderAsync(TblOrder order, string userId, string role);
        Task<(int SuccessCount, string Message)> AssignOrdersToTripAsync(List<string> orderIds, string tripId, string userId, string role, string? userStationName);
    }
}
