using AgentGateway.Api.Contracts;
using AgentGateway.Core.Entities;

namespace AgentGateway.Api.Mapping;

public interface IDeliveryMapper
{
    DeliveryResponse ToResponse(DeliveryEntity delivery);
}
