using TelegramGateway.Api.Contracts;
using TelegramGateway.Core.Entities;

namespace TelegramGateway.Api.Mapping;

public interface IDeliveryMapper
{
    DeliveryResponse ToResponse(DeliveryEntity delivery);
}
