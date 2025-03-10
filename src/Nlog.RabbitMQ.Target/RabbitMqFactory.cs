using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using RabbitMQ.Client;

namespace Nlog.RabbitMQ.Target;

public class RabbitMqFactory
{
    private readonly ConnectionFactory _factory;
    private readonly IEnumerable<AmqpTcpEndpoint> _endpoints;

    public RabbitMqFactory(ConnectionFactory factory, IEnumerable<AmqpTcpEndpoint> endpoints)
    {
        _factory = factory;
        _endpoints = endpoints;
    }

    public async Task<IConnection> CreateConnectionAsync()
    {
        if (!_endpoints.Any() && _factory.Uri == null)
        {
            throw new ArgumentException("No endpoints or uri provided");
        }
        
        return _endpoints.Any() ? await  _factory.CreateConnectionAsync(_endpoints) : await _factory.CreateConnectionAsync();
    }
}